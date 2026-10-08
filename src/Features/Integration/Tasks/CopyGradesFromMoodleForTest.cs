using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AutoConstructor.Attributes;
using QuizModels;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Moodle;

namespace ScheduleLib.Application.Core;

[AutoConstructor]
public sealed partial class CopyGradesFromMoodleForTestTaskHandler
{
    private readonly CourseNameUnifierModule _unifier;
    private readonly Schedule _schedule;
    private readonly LookupModule _lookup;

    public readonly record struct RunParams
    {
        public required MoodleScrapingContext MoodleContext { get; init; }
        public required OnlineRegistryNavigator RegistryNavigator { get; init; }
        public required Semester Semester { get; init; }
        public required string QuizId { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public async Task Run(RunParams p)
    {
        var quiz = await p.MoodleContext.ScrapeQuizAttempts(p.QuizId, cancellationToken: p.CancellationToken);
        var plan = await Plan(new() { Quiz = quiz, RegistryNavigator = p.RegistryNavigator, Semester = p.Semester, CancellationToken = p.CancellationToken });
        foreach (var action in plan.Actions) await action.Execute(p.CancellationToken);
    }

    public readonly record struct PlanParams
    {
        public required QuizAttemptsPage Quiz { get; init; }
        public required IRegistryGradeNavigator RegistryNavigator { get; init; }
        public required Semester Semester { get; init; }
        public required CancellationToken CancellationToken { get; init; }
    }

    public async Task<RegistryGradePlan> Plan(PlanParams p)
    {
        var quiz = p.Quiz;
        var registryNav = p.RegistryNavigator;
        var actions = new List<RegistryGradeAction>();
        var notices = new List<RegistryGradeNotice>();
        p.CancellationToken.ThrowIfCancellationRequested();
        Dictionary<Name, float> gradeByName = new(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var q in quiz.Attempts)
        {
            p.CancellationToken.ThrowIfCancellationRequested();
            var parser = new SequenceReader(q.UserName);
            var name = NameHelper.TryParseName(ref parser);
            if (name is null)
            {
                notices.Add(new("omitted", $"Moodle name could not be parsed: {q.UserName}"));
                continue;
            }

            // They go in different order on moodle.
            {
                var f = name.FirstName;
                var l = name.LastName;
                name.FirstName = l;
                name.LastName = f;
            }

            if (q.Grade is not { } grade1)
            {
                notices.Add(new("omitted", $"Moodle student has no grade: {q.UserName}"));
                continue;
            }
            if (!float.IsFinite(grade1) || (double) grade1 > int.MaxValue || (double) grade1 < int.MinValue)
            {
                notices.Add(new("unsupported", $"Moodle grade is not a finite supported number: {q.UserName}"));
                continue;
            }
            if (gradeByName.ContainsKey(name)) notices.Add(new("omitted", $"Earlier attempt replaced by the last graded attempt: {name}"));
            gradeByName[name] = grade1;
        }

        // determine course from path
        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        if (parsedPath is null || parsedPath.Grade == Grade.Invalid || parsedPath.TestNumber <= 0)
        {
            notices.Add(new("unsupported", "Moodle course/test path could not be parsed."));
            return new(actions, notices);
        }

        var courseId = _unifier.Find(new()
        {
            Lookup = _lookup,
            CourseName = parsedPath.CourseName.AsMemory(),
        });
        if (courseId is null)
        {
            notices.Add(new("unsupported", "Moodle course name does not resolve in the configured schedule."));
            return new(actions, notices);
        }
        var grade = parsedPath.Grade;
        var qualificationType = parsedPath.QualificationType;

        foreach (var course in await registryNav.GetCourses(p.Semester))
        {
            if (course.CourseId != courseId)
            {
                continue;
            }

            foreach (var group in await registryNav.GetGroups(course))
            {
                if (group.Groups.IsWildcard || group.Groups.Value.Count == 0)
                {
                    notices.Add(new("unsupported", $"Wildcard or empty registry group is unsupported: {group.EvaluationUri}"));
                    continue;
                }
                var groupInfo = _schedule.Get(group.Groups.Value[0]);
                if (groupInfo.QualificationType != qualificationType)
                {
                    continue;
                }
                if (groupInfo.Grade != grade)
                {
                    continue;
                }

                p.CancellationToken.ThrowIfCancellationRequested();
                var evaluareDoc = await registryNav.GetHtml(group.EvaluationUri);

                // Find anchor with text Testarea X
                IHtmlAnchorElement? TestAnchor()
                {
                    var tables = evaluareDoc.QuerySelectorAll<IHtmlAnchorElement>("table a");
                    var matching = tables.Where(x =>
                    {
                        var parser = new SequenceReader(x.TextContent);
                        parser.SkipWhitespace();
                        if (!parser.ConsumeExactString("Testarea"))
                        {
                            return false;
                        }
                        if (!parser.SkipWhitespace().SkippedAny)
                        {
                            return false;
                        }
                        var bparser = parser.BufferedView();
                        if (!bparser.SkipNumbers().SkippedAny)
                        {
                            return false;
                        }

                        var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
                        var number = int.Parse(numberSpan);
                        if (parsedPath.TestNumber != number)
                        {
                            return false;
                        }

                        return true;
                    });
                    var header = matching.FirstOrDefault();
                    return header;
                }

                var testUrl = TestAnchor();
                if (testUrl is null)
                {
                    notices.Add(new("unsupported", $"Testarea {parsedPath.TestNumber} is missing for {group.EvaluationUri}"));
                    continue;
                }
                var test1Doc = await registryNav.GetHtml(new(testUrl.Href));
                var table = test1Doc.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new InvalidOperationException("No table found");
                int nameColumnIndex = FindColumnIndex("Numele");
                int gradeColumnIndex = FindColumnIndex("Nota");

                var changes = new List<RegistryGradeChange>();
                var inputs = new List<(IHtmlInputElement Input, int Grade)>();
                for (int i = 1; i < table.Rows.Length; i++)
                {
                    p.CancellationToken.ThrowIfCancellationRequested();
                    var row = table.Rows[i];
                    var nameCell = row.Cells[nameColumnIndex];

                    Name name;
                    {
                        var registryName = nameCell.TextContent.Trim();
                        // A two-part name otherwise lets the name parser consume exmatr as a patronymic.
                        if (registryName.EndsWith(" exmatr", StringComparison.Ordinal))
                        {
                            notices.Add(new("omitted", $"Expelled registry student: {registryName}"));
                            continue;
                        }
                        var nameParser = new SequenceReader(registryName);
                        nameParser.SkipWhitespace();
                        var parsedName = NameHelper.TryParseName(ref nameParser);
                        if (parsedName is null)
                        {
                            notices.Add(new("unsupported", $"Registry name could not be parsed: {registryName}"));
                            continue;
                        }
                        name = parsedName;
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            notices.Add(new("omitted", $"Expelled registry student: {name}"));
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            notices.Add(new("unsupported", $"Registry name contains unsupported trailing text: {registryName}"));
                            continue;
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        notices.Add(new("unmatched", $"Registry student has no matching graded Moodle attempt: {name}"));
                        continue;
                    }

                    var gradeRounded = (int) Math.Round(gradeInDb);

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        changes.Add(new(name.ToString(), gradeInDb, gradeRounded, input.Value));
                        inputs.Add((input, gradeRounded));
                    }
                }

                if (changes.Count == 0)
                {
                    notices.Add(new("omitted", $"No mapped grades to submit for {testUrl.Href}"));
                    continue;
                }
                if (test1Doc.QuerySelector<IHtmlFormElement>("form") is null)
                    throw new InvalidOperationException("No registry grade form found");
                actions.Add(new(testUrl.Href, _schedule.Get(course.CourseId).FullName, string.Join(", ", group.Groups.Value.Select(x => _schedule.Get(x).Name)), parsedPath.TestNumber, changes, async token =>
                {
                    var started = false;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var input in inputs) input.Input.Value = input.Grade.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        token.ThrowIfCancellationRequested();
                        started = true;
                        await registryNav.SubmitGrades(test1Doc, token);
                    }
                    catch (OperationCanceledException e) { throw new RegistryActionCancelledException(started, e); }
                    catch (Exception e) { throw new RegistryActionExecutionException(started, e); }
                }));
                continue;

                int FindColumnIndex(string name)
                {
                    return table.Rows[0].Cells.WithIndex().Where(x =>
                    {
                        var t = x.Item.TextContent.AsSpan().Trim();
                        return t.SequenceEqual(name);
                    }).Single().Index;
                }
            }
        }

        if (actions.Count == 0) notices.Add(new("unmatched", "No registry test forms with supported mapped grades were planned."));
        foreach (var (name, value) in gradeByName)
        {
            notices.Add(new("unmatched", $"Moodle student was not matched in registry: {name} ({value})"));
        }
        return new(actions, notices);
    }
}
