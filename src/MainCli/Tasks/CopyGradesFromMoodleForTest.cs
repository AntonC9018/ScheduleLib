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
        // How to do this without repeating this?
        // DI doesn't help with this, because to creating this is async.
        var quiz = await p.MoodleContext.ScrapeQuizAttempts(p.QuizId);

        var registryNav = p.RegistryNavigator;
        var coursesNav = registryNav.Courses();
        var groupsNav = registryNav.Groups();

        Dictionary<Name, float> gradeByName = new(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var q in quiz.Attempts)
        {
            var parser = new Parser(q.UserName);
            var name = NameHelper.TryParseName(ref parser);
            if (name is null)
            {
                Console.WriteLine($"{q.UserName} not parsed as name.");
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
                Console.WriteLine($"{q.UserName} not graded yet!");
                continue;
            }
            gradeByName[name] = grade1;
        }

        // determine course from path
        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        _ = parsedPath;
        if (parsedPath is null)
        {
            throw new InvalidOperationException("Could not parse path");
        }

        var courseId = _unifier.Find(new()
        {
            Lookup = _lookup,
            CourseName = parsedPath.CourseName.AsMemory(),
        });
        var grade = parsedPath.Grade;
        var qualificationType = parsedPath.QualificationType;

        foreach (var course in await coursesNav.Get(p.Semester))
        {
            if (course.CourseId != courseId)
            {
                continue;
            }

            foreach (var group in await groupsNav.Get(course))
            {
                if (group.Groups.IsWildcard)
                {
                    throw new NotImplementedException();
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

                var evaluareDoc = await registryNav.GetHtml(group.EvaluationUri);

                // Find anchor with text Testarea X
                IHtmlAnchorElement TestAnchor()
                {
                    var tables = evaluareDoc.QuerySelectorAll<IHtmlAnchorElement>("table a");
                    var matching = tables.Where(x =>
                    {
                        var parser = new Parser(x.TextContent);
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
                    var header = matching.First();
                    return header;
                }

                var testUrl = TestAnchor();
                var test1Doc = await registryNav.GetHtml(new(testUrl.Href));
                var table = test1Doc.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new InvalidOperationException("No table found");
                int nameColumnIndex = FindColumnIndex("Numele");
                int gradeColumnIndex = FindColumnIndex("Nota");

                for (int i = 1; i < table.Rows.Length; i++)
                {
                    var row = table.Rows[i];
                    var nameCell = row.Cells[nameColumnIndex];

                    Name name;
                    {
                        var nameParser = new Parser(nameCell.TextContent);
                        nameParser.SkipWhitespace();
                        name = NameHelper.ParseName(ref nameParser);
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            throw new InvalidOperationException("Extra text after name");
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        Console.WriteLine($"No student in moodle: {name}");
                        continue;
                    }

                    var gradeRounded = (int) Math.Round(gradeInDb);

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        input.Value = gradeRounded.ToString();
                    }
                }

                var form = test1Doc.QuerySelector<IHtmlFormElement>("form")
                    ?? throw new InvalidOperationException("No form found");
                _ = form;

                // var button = test1Doc.QuerySelector<IHtmlButtonElement>("form > div > div > button")
                //     ?? throw new InvalidOperationException("No submit button found");
                // await button.SubmitAsync();
                await form.SubmitAsync();
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

        foreach (var (name, value) in gradeByName)
        {
            Console.WriteLine($"Student not found in registry: {name} ({value})");
        }
    }
}
