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
        var plan = await Plan(new()
        {
            Quiz = quiz,
            RegistryNavigator = p.RegistryNavigator,
            Semester = p.Semester,
            CancellationToken = p.CancellationToken
        });
        foreach (var action in plan.Actions)
        {
            await action.Execute(p.CancellationToken);
        }
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
                global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list = notices;
                string kind = "omitted";
                string detail = $"Moodle name could not be parsed: {q.UserName}";
                global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item = new(Kind: kind, Detail: detail);
                list.Add(item);
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
                global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list2 = notices;
                string kind2 = "omitted";
                string detail2 = $"Moodle student has no grade: {q.UserName}";
                global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item2 = new(Kind: kind2, Detail: detail2);
                list2.Add(item2);
                continue;
            }
            bool conditionResult2;
            if (!float.IsFinite(grade1))
            {
                conditionResult2 = true;
            }
            else
            {
                double conditionValue = (double) grade1;
                if (conditionValue > int.MaxValue)
                {
                    conditionResult2 = true;
                }
                else
                {
                    double conditionValue2 = (double) grade1;
                    conditionResult2 = conditionValue2 < int.MinValue;
                }
            }

            if (conditionResult2)
            {
                global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list3 = notices;
                string kind3 = "unsupported";
                string detail3 = $"Moodle grade is not a finite supported number: {q.UserName}";
                global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item3 = new(Kind: kind3, Detail: detail3);
                list3.Add(item3);
                continue;
            }
            if (gradeByName.ContainsKey(name))
            {
                global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list4 = notices;
                string kind4 = "omitted";
                string detail4 = $"Earlier attempt replaced by the last graded attempt: {name}";
                global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item4 = new(Kind: kind4, Detail: detail4);
                list4.Add(item4);
            }

            gradeByName[name] = grade1;
        }

        // determine course from path
        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        if (parsedPath is null || parsedPath.Grade == Grade.Invalid || parsedPath.TestNumber <= 0)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list5 = notices;
            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item5 = new(Kind: "unsupported", Detail: "Moodle course/test path could not be parsed.");
            list5.Add(item5);
            return new(actions, notices);
        }

        var courseId = _unifier.Find(new()
        {
            Lookup = _lookup,
            CourseName = parsedPath.CourseName.AsMemory(),
        });
        if (courseId is null)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list6 = notices;
            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item6 = new(Kind: "unsupported", Detail: "Moodle course name does not resolve in the configured schedule.");
            list6.Add(item6);
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
                bool CalculateConditionResult()
                {
                    if (group.Groups.IsWildcard)
                    {
                        return true;
                    }
                    else
                    {
                        if (group.Groups.Value.Count == 0)
                        {
                            return true;
                        }

                        return false;
                    }
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
                {
                    global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list7 = notices;
                    string kind5 = "unsupported";
                    string detail5 = $"Wildcard or empty registry group is unsupported: {group.EvaluationUri}";
                    global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item7 = new(Kind: kind5, Detail: detail5);
                    list7.Add(item7);
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
                    global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list8 = notices;
                    string kind6 = "unsupported";
                    string detail6 = $"Testarea {parsedPath.TestNumber} is missing for {group.EvaluationUri}";
                    global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item8 = new(Kind: kind6, Detail: detail6);
                    list8.Add(item8);
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
                            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list9 = notices;
                            string kind7 = "omitted";
                            string detail7 = $"Expelled registry student: {registryName}";
                            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item9 = new(Kind: kind7, Detail: detail7);
                            list9.Add(item9);
                            continue;
                        }
                        var nameParser = new SequenceReader(registryName);
                        nameParser.SkipWhitespace();
                        var parsedName = NameHelper.TryParseName(ref nameParser);
                        if (parsedName is null)
                        {
                            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list10 = notices;
                            string kind8 = "unsupported";
                            string detail8 = $"Registry name could not be parsed: {registryName}";
                            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item10 = new(Kind: kind8, Detail: detail8);
                            list10.Add(item10);
                            continue;
                        }
                        name = parsedName;
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list11 = notices;
                            string kind9 = "omitted";
                            string detail9 = $"Expelled registry student: {name}";
                            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item11 = new(Kind: kind9, Detail: detail9);
                            list11.Add(item11);
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list12 = notices;
                            string kind10 = "unsupported";
                            string detail10 = $"Registry name contains unsupported trailing text: {registryName}";
                            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item12 = new(Kind: kind10, Detail: detail10);
                            list12.Add(item12);
                            continue;
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list13 = notices;
                        string kind11 = "unmatched";
                        string detail11 = $"Registry student has no matching graded Moodle attempt: {name}";
                        global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item13 = new(Kind: kind11, Detail: detail11);
                        list13.Add(item13);
                        continue;
                    }

                    double round = Math.Round(gradeInDb);
                    var gradeRounded = (int) round;

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeChange> list14 = changes;
                        global::ScheduleLib.OnlineRegistry.RegistryGradeChange item14 = new(Student: name.ToString(), gradeInDb, gradeRounded, PreviousGrade: input.Value);
                        list14.Add(item14);
                        inputs.Add((input, gradeRounded));
                    }
                }

                if (changes.Count == 0)
                {
                    global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list15 = notices;
                    string kind12 = "omitted";
                    string detail12 = $"No mapped grades to submit for {testUrl.Href}";
                    global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item15 = new(Kind: kind12, Detail: detail12);
                    list15.Add(item15);
                    continue;
                }
                RegistryGradeForm.Validate(test1Doc);
                actions.Add(new(destination: testUrl.Href, course: _schedule.Get(course.CourseId).FullName, groups: string.Join(", ", group.Groups.Value.Select(x => _schedule.Get(x).Name)), parsedPath.TestNumber, changes, async token =>
                {
                    var started = false;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        foreach (var input in inputs)
                        {
                            input.Input.Value = input.Grade.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        }

                        token.ThrowIfCancellationRequested();
                        await registryNav.SubmitGrades(test1Doc, token, () =>
                        {
                            started = true;
                        });
                    }
                    catch (OperationCanceledException e) { throw new RegistryActionCancelledException(started, e); }
                    catch (Exception e) { throw new RegistryActionExecutionException(started, e); }
                }));
                continue;

                int FindColumnIndex(string name)
                {
                    return table.Rows[0].Cells.WithIndex()
                        .Where(x =>
                    {
                        var t = x.Item.TextContent.AsSpan()
                            .Trim();
                        return t.SequenceEqual(name);
                    })
                        .Single().Index;
                }
            }
        }

        if (actions.Count == 0)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list16 = notices;
            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item16 = new(Kind: "unmatched", Detail: "No registry test forms with supported mapped grades were planned.");
            list16.Add(item16);
        }

        foreach (var (name, value) in gradeByName)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryGradeNotice> list17 = notices;
            string kind13 = "unmatched";
            string detail13 = $"Moodle student was not matched in registry: {name} ({value})";
            global::ScheduleLib.OnlineRegistry.RegistryGradeNotice item17 = new(Kind: kind13, Detail: detail13);
            list17.Add(item17);
        }
        return new(actions, notices);
    }
}
