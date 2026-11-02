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
            CancellationToken = p.CancellationToken,
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

    /// <summary>A grade form input field and the grade that will be submitted through it.</summary>
    private readonly record struct GradeFormInput(IHtmlInputElement Element, int Grade);

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
                notices.Add(RegistryGradeNotice.MoodleNameUnparsed(q.UserName));
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
                notices.Add(RegistryGradeNotice.MoodleGradeMissing(q.UserName));
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
                notices.Add(RegistryGradeNotice.MoodleGradeUnsupported(q.UserName));
                continue;
            }
            if (gradeByName.ContainsKey(name))
            {
                notices.Add(RegistryGradeNotice.MoodleAttemptSuperseded(name));
            }

            gradeByName[name] = grade1;
        }

        // determine course from path
        RegistryGradePlan UnsupportedPath()
        {
            notices.Add(RegistryGradeNotice.MoodlePathUnparsed());
            return new(actions, notices);
        }

        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        if (parsedPath is null)
        {
            return UnsupportedPath();
        }
        if (parsedPath.Grade == Grade.Invalid)
        {
            return UnsupportedPath();
        }
        if (parsedPath.TestNumber <= 0)
        {
            return UnsupportedPath();
        }

        var courseId = _unifier.Find(new()
        {
            Lookup = _lookup,
            CourseName = parsedPath.CourseName.AsMemory(),
        });
        if (courseId is null)
        {
            notices.Add(RegistryGradeNotice.MoodleCourseUnresolved());
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

                    if (group.Groups.Value.Count == 0)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
                {
                    notices.Add(RegistryGradeNotice.RegistryGroupUnsupported(group.EvaluationUri));
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
                        if (parsedPath.TestNumber == number)
                        {
                            return true;
                        }

                        return false;
                    });
                    var header = matching.FirstOrDefault();
                    return header;
                }

                var testUrl = TestAnchor();
                if (testUrl is null)
                {
                    notices.Add(RegistryGradeNotice.RegistryTestMissing(parsedPath.TestNumber, group.EvaluationUri));
                    continue;
                }
                var test1Doc = await registryNav.GetHtml(new(testUrl.Href));
                var table = test1Doc.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new InvalidOperationException("No table found");
                int nameColumnIndex = FindColumnIndex("Numele");
                int gradeColumnIndex = FindColumnIndex("Nota");

                var changes = new List<RegistryGradeChange>();
                var inputs = new List<GradeFormInput>();
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
                            notices.Add(RegistryGradeNotice.RegistryStudentExpelled(registryName));
                            continue;
                        }
                        var nameParser = new SequenceReader(registryName);
                        nameParser.SkipWhitespace();
                        var parsedName = NameHelper.TryParseName(ref nameParser);
                        if (parsedName is null)
                        {
                            notices.Add(RegistryGradeNotice.RegistryNameUnparsed(registryName));
                            continue;
                        }
                        name = parsedName;
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            notices.Add(RegistryGradeNotice.RegistryStudentExpelled(name.ToString()));
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            notices.Add(RegistryGradeNotice.RegistryNameTrailingTextUnsupported(registryName));
                            continue;
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        notices.Add(RegistryGradeNotice.RegistryStudentUnmatched(name));
                        continue;
                    }

                    double round = Math.Round(gradeInDb);
                    var gradeRounded = (int) round;

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        var
                            list14 = changes;
                        RegistryGradeChange item14 = new(
                            Student: name.ToString(),
                            gradeInDb,
                            gradeRounded,
                            PreviousGrade: input.Value);
                        list14.Add(item14);
                        inputs.Add(new(input, gradeRounded));
                    }
                }

                if (changes.Count == 0)
                {
                    notices.Add(RegistryGradeNotice.NoGradesToSubmit(testUrl.Href));
                    continue;
                }
                RegistryGradeForm.Validate(test1Doc);
                actions.Add(new(
                    destination: testUrl.Href,
                    course: _schedule.Get(course.CourseId).FullName,
                    groups: string.Join(", ", group.Groups.Value.Select(x => _schedule.Get(x).Name)),
                    parsedPath.TestNumber,
                    changes,
                    async token =>
                    {
                        await RegistryActionGuard.Run(async onSubmissionStarted =>
                        {
                            token.ThrowIfCancellationRequested();
                            foreach (var input in inputs)
                            {
                                input.Element.Value =
                                    input.Grade.ToString(System.Globalization.CultureInfo.InvariantCulture);
                            }

                            token.ThrowIfCancellationRequested();
                            await registryNav.SubmitGrades(
                                test1Doc,
                                token,
                                onSubmissionStarted);
                        });
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
            notices.Add(RegistryGradeNotice.NoTestFormsPlanned());
        }

        foreach (var (name, value) in gradeByName)
        {
            notices.Add(RegistryGradeNotice.MoodleStudentUnmatched(name, value));
        }
        return new(actions, notices);
    }
}
