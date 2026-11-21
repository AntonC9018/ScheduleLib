using AngleSharp;
using AngleSharp.Html.Dom;
using QuizModels;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.Moodle;

namespace MainCli.Tests;

public sealed class MoodleInteropTests
{
    [Fact]
    public void PathParserParsesStandardRomanBreadcrumb()
    {
        string[] segments =
        [
            "Cursuri USM",
            "Matematică și Informatică",
            "Ciclul I Licență",
            "Informatică aplicată",
            "Anul III",
            "Modele de design software",
            "Atestare 1",
            "Note",
        ];

        var parsed = MoodlePathParser.TryParse(segments);

        Assert.NotNull(parsed);
        Assert.Equal(expected: "Cursuri USM", actual: parsed.Base);
        Assert.Equal(expected: QualificationType.Licenta, actual: parsed.QualificationType);
        Assert.Equal(expected: new Grade(3), actual: parsed.Grade);
        Assert.Equal(expected: "Modele de design software", actual: parsed.CourseName);
        Assert.Equal(expected: "Atestare 1", actual: parsed.SectionName);
        Assert.Equal(expected: 1, actual: parsed.TestNumber);
    }

    [Fact]
    public void PathParserParsesArabicNumbersAndMaster()
    {
        string[] segments =
        [
            "Cursuri USM",
            "FMI",
            "Ciclul 2 Masterat",
            "Securitate",
            "Anul 1",
            "Criptografie",
            "Atestare 2",
        ];

        var parsed = MoodlePathParser.TryParse(segments);

        Assert.NotNull(parsed);
        Assert.Equal(expected: QualificationType.Master, actual: parsed.QualificationType);
        Assert.Equal(expected: new Grade(1), actual: parsed.Grade);
        Assert.Equal(expected: 2, actual: parsed.TestNumber);
    }

    [Fact]
    public void PathParserRequiresKeywordWhitespaceBoundary()
    {
        string[] segments =
        [
            "Cursuri USM",
            "FMI",
            "CiclulX I Licență",
            "IA",
            "Anul II",
            "Algoritmi",
            "Atestare 1",
        ];

        Assert.Null(MoodlePathParser.TryParse(segments));
    }

    [Fact]
    public void PathParserAcceptsTrailingPunctuationAndGradeSuffix()
    {
        string[] segments =
        [
            "Cursuri USM",
            "FMI",
            "Ciclul I Licență",
            "IA",
            "Anul III (2025-2026)",
            "Algoritmi",
            "Atestare 1: Grafuri",
        ];

        var parsed = MoodlePathParser.TryParse(segments);

        Assert.NotNull(parsed);
        Assert.Equal(expected: new Grade(3), actual: parsed.Grade);
        Assert.Equal(expected: 1, actual: parsed.TestNumber);
    }

    [Theory]
    [InlineData("correct", "correct")]
    [InlineData("incorrect", "incorrect")]
    [InlineData("partiallycorrect", "partiallycorrect")]
    [InlineData("requiresgrading", "requiresgrading")]
    [InlineData("que multichoice incorrect", "incorrect")]
    [InlineData("que multichoice partiallycorrect", "partiallycorrect")]
    [InlineData("que multichoice correct", "correct")]
    [InlineData("", "unknown")]
    public void ParseQuestionStatusMatchesExactCssClassTokens(string classes, string expectedStatus)
    {
        var actual = MoodleQuizHtmlParser.ParseQuestionStatus(classes);
        Assert.Equal(expected: expectedStatus, actual: actual.Value);
    }

    [Theory]
    [InlineData("52 min 41 secunde", 0, 0, 52, 41)]
    [InlineData("1 oră 15 min 3 secunde", 0, 1, 15, 3)]
    [InlineData("2 ore 5 min", 0, 2, 5, 0)]
    [InlineData("1 zi 2 ore", 1, 2, 0, 0)]
    [InlineData("2 zile 3 ore 5 min", 2, 3, 5, 0)]
    public void ParseDurationIncludesDaysHoursMinutesAndSeconds(
        string text,
        int days,
        int hours,
        int minutes,
        int seconds)
    {
        var actual = MoodleQuizHtmlParser.ParseDuration(text);
        var expected = new TimeSpan(
            days: days,
            hours: hours,
            minutes: minutes,
            seconds: seconds);
        Assert.Equal(expected: expected, actual: actual);
    }

    [Fact]
    public void ParseDurationReturnsNullWhenCombinedIntervalExceedsTimeSpanMaxValue()
    {
        Assert.Null(MoodleQuizHtmlParser.ParseDuration("10675198 zile 3 ore 1428 min 6 sec"));
    }

    [Fact]
    public void ParseDateTimeHandlesPlaceholdersAndRomanianFormats()
    {
        Assert.Null(MoodleQuizHtmlParser.ParseDateTime("-"));
        Assert.Null(MoodleQuizHtmlParser.ParseDateTime(" – "));

        var parsed = MoodleQuizHtmlParser.ParseDateTime("16 octombrie 2025  13:28");
        var expected = new DateTime(
            year: 2025,
            month: 10,
            day: 16,
            hour: 13,
            minute: 28,
            second: 0);
        Assert.Equal(expected: expected, actual: parsed);

        Assert.Throws<MoodleDateTimeParseException>(() => MoodleQuizHtmlParser.ParseDateTime("not a date"));
    }

    [Fact]
    public async Task ParseAttemptsTableSkipsBlankRowAndHandlesFormattedNameAndDashFinish()
    {
        const string html = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                  <th><a data-sortby="email">Email</a></th>
                  <th><a data-sortby="state">State</a></th>
                  <th><a data-sortby="timefinish">Finish</a></th>
                  <th><a data-sortby="sumgrades">Grade</a></th>
                  <th><a data-sortby="qsgrade1">Q1</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="1" /></td>
                  <td>
                    <a href="/user/1">Popescu Ion</a>
                    <a class="reviewlink" href="/review/1">Review</a>
                  </td>
                  <td>ion@example.com</td>
                  <td>În progres</td>
                  <td>-</td>
                  <td>9,50</td>
                  <td><a href="/q/1"><span class="incorrect">0,00</span></a></td>
                </tr>
                <tr>
                  <td></td>
                  <td></td>
                  <td>empty@example.com</td>
                  <td>Finalizate</td>
                  <td>-</td>
                  <td>0,00</td>
                  <td></td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="3" /></td>
                  <td>Rusu Ana<a class="reviewlink" href="/review/3">Review</a></td>
                  <td>ana@example.com</td>
                  <td>Finalizate</td>
                  <td>16 octombrie 2025  13:28</td>
                  <td>10,00</td>
                  <td><a href="/q/3"><span class="partiallycorrect">1,50</span></a></td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html));
        var table = Assert.IsAssignableFrom<IHtmlTableElement>(document.QuerySelector("table#attempts"));

        var parsed = MoodleQuizHtmlParser.ParseAttemptsTable(table);

        Assert.Empty(parsed.ParseIssues);
        Assert.Equal(expected: 2, actual: parsed.Attempts.Count);
        Assert.Equal(expected: "Popescu Ion", actual: parsed.Attempts[0].UserName);
        Assert.Null(parsed.Attempts[0].TimeFinish);
        Assert.Equal(expected: QuestionStatus.Incorrect, actual: parsed.Attempts[0].QuestionGrades[0].Status);
        Assert.Equal(expected: "Rusu Ana", actual: parsed.Attempts[1].UserName);
        Assert.NotNull(parsed.Attempts[1].TimeFinish);
        Assert.Equal(expected: QuestionStatus.PartiallyCorrect, actual: parsed.Attempts[1].QuestionGrades[0].Status);
    }

    [Fact]
    public async Task ParseAttemptsTableThrowsWhenAttemptIdHasMissingUserName()
    {
        const string html = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="7" /></td>
                  <td></td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html));
        var table = Assert.IsAssignableFrom<IHtmlTableElement>(document.QuerySelector("table#attempts"));

        var ex = Assert.Throws<MoodleAttemptUserNameMissingException>(() =>
            MoodleQuizHtmlParser.ParseAttemptsTable(table));
        Assert.Equal(expected: "7", actual: ex.AttemptId);
    }

    [Fact]
    public async Task ParseAttemptsTableKeepsAttemptWithMalformedTimestampAndRecordsParseIssue()
    {
        const string html = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                  <th><a data-sortby="timefinish">Finish</a></th>
                  <th><a data-sortby="duration">Duration</a></th>
                  <th><a data-sortby="sumgrades">Grade</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="1" /></td>
                  <td>Popescu Ion<a class="reviewlink" href="/review/1">Review</a></td>
                  <td>16 octombrie 2025  13:28</td>
                  <td>10675198 zile 3 ore 1428 min 6 sec</td>
                  <td>9,00</td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="2" /></td>
                  <td>Bad Timestamp<a class="reviewlink" href="/review/2">Review</a></td>
                  <td>not a date</td>
                  <td>5 min</td>
                  <td>8,00</td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="3" /></td>
                  <td>Rusu Ana<a class="reviewlink" href="/review/3">Review</a></td>
                  <td>16 octombrie 2025  14:00</td>
                  <td>12 min 5 sec</td>
                  <td>10,00</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html));
        var table = Assert.IsAssignableFrom<IHtmlTableElement>(document.QuerySelector("table#attempts"));

        var parsed = MoodleQuizHtmlParser.ParseAttemptsTable(table);

        Assert.Equal(expected: 3, actual: parsed.Attempts.Count);
        Assert.Equal(expected: "Popescu Ion", actual: parsed.Attempts[0].UserName);
        Assert.Null(parsed.Attempts[0].Duration);

        Assert.Equal(expected: "Bad Timestamp", actual: parsed.Attempts[1].UserName);
        Assert.Null(parsed.Attempts[1].TimeFinish);
        Assert.Equal(expected: 8f, actual: parsed.Attempts[1].Grade);

        Assert.Equal(expected: "Rusu Ana", actual: parsed.Attempts[2].UserName);
        Assert.Equal(expected: TimeSpan.FromSeconds(725), actual: parsed.Attempts[2].Duration);

        var issue = Assert.Single(parsed.ParseIssues);
        Assert.Equal(expected: "2", actual: issue.AttemptId);
        Assert.Equal(expected: "Bad Timestamp", actual: issue.UserName);
        Assert.Equal(expected: "timefinish", actual: issue.FieldName);
        Assert.Equal(expected: "not a date", actual: issue.RawValue);
    }

    [Fact]
    public async Task CopyGradesPlanEmitsNoticeForMoodleParseIssues()
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        var schedule = builder.Build();
        var handler = new CopyGradesFromMoodleForTestTaskHandler(schedule, new LookupFacade(schedule));

        var page = new QuizAttemptsPage
        {
            Attempts =
            [
                new QuizAttempt
                {
                    AttemptId = "2",
                    UserName = "Popescu Ion",
                    Grade = 9f,
                    TimeFinish = null,
                },
            ],
            Path = [],
            ParseIssues =
            [
                new QuizAttemptParseIssue(
                    AttemptId: "2",
                    UserName: "Popescu Ion",
                    FieldName: "timefinish",
                    RawValue: "not a date"),
            ],
        };

        var plan = await handler.Plan(new()
        {
            Quiz = page,
            RegistryNavigator = new UnusedRegistryGradeNavigator(),
            Semester = Semester.Sem1,
            CancellationToken = CancellationToken.None,
        });

        Assert.Contains(
            plan.Notices,
            n => n == RegistryGradeNotice.MoodleAttemptFieldUnparsed(
                userName: "Popescu Ion",
                fieldName: "timefinish",
                rawValue: "not a date"));
    }

    [Fact]
    public async Task ParseAttemptsTableSkipsNoAttemptUsersAndRejectsBrokenAttemptLinks()
    {
        const string withNoAttemptHtml = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                  <th><a data-sortby="sumgrades">Grade</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="1" /></td>
                  <td>Popescu Ion<a class="reviewlink" href="/review/1">Review</a></td>
                  <td>9,00</td>
                </tr>
                <tr>
                  <td></td>
                  <td><a href="/user/2">No Attempt User</a></td>
                  <td>-</td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="3" /></td>
                  <td>Rusu Ana<a class="reviewlink" href="/review/3">Review</a></td>
                  <td>10,00</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(withNoAttemptHtml));
        var table = Assert.IsAssignableFrom<IHtmlTableElement>(document.QuerySelector("table#attempts"));

        var parsed = MoodleQuizHtmlParser.ParseAttemptsTable(table);

        Assert.Equal(expected: 2, actual: parsed.Attempts.Count);
        Assert.Equal(expected: "Popescu Ion", actual: parsed.Attempts[0].UserName);
        Assert.Equal(expected: "Rusu Ana", actual: parsed.Attempts[1].UserName);

        const string brokenAttemptHtml = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="42" /></td>
                  <td>Broken Attempt</td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var brokenDoc = await context.OpenAsync(req => req.Content(brokenAttemptHtml));
        var brokenTable = Assert.IsAssignableFrom<IHtmlTableElement>(brokenDoc.QuerySelector("table#attempts"));
        var ex = Assert.Throws<MoodleAttemptReviewLinkMissingException>(() =>
            MoodleQuizHtmlParser.ParseAttemptsTable(brokenTable));
        Assert.Equal(expected: "42", actual: ex.AttemptId);
    }

    [Fact]
    public async Task ParseAttemptsTableHandlesQuestionSlotGapsAndDashPlaceholders()
    {
        const string html = """
            <html><body>
            <table id="attempts">
              <thead>
                <tr>
                  <th>Select</th>
                  <th><a data-sortby="firstname">Name</a></th>
                  <th><a data-sortby="qsgrade1">Q1</a></th>
                  <th><a data-sortby="qsgrade3">Q3</a></th>
                  <th><a data-sortby="qsgrade4">Q4</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="1" /></td>
                  <td>Popescu Ion<a class="reviewlink" href="/review/1">Review</a></td>
                  <td>-</td>
                  <td><a href="/q/3"><span class="correct">2,00</span></a></td>
                  <td><a href="/q/4"><span class="incorrect">0,00</span></a></td>
                </tr>
              </tbody>
            </table>
            </body></html>
            """;

        using var context = BrowsingContext.New(Configuration.Default);
        using var document = await context.OpenAsync(req => req.Content(html));
        var table = Assert.IsAssignableFrom<IHtmlTableElement>(document.QuerySelector("table#attempts"));

        var parsed = MoodleQuizHtmlParser.ParseAttemptsTable(table);
        var attempt = Assert.Single(parsed.Attempts);

        Assert.Equal(expected: 2, actual: attempt.QuestionGrades.Count);
        Assert.Equal(expected: 3, actual: attempt.QuestionGrades[0].QuestionNumber);
        Assert.Equal(expected: QuestionStatus.Correct, actual: attempt.QuestionGrades[0].Status);
        Assert.Equal(expected: 4, actual: attempt.QuestionGrades[1].QuestionNumber);
        Assert.Equal(expected: QuestionStatus.Incorrect, actual: attempt.QuestionGrades[1].Status);
    }

    private sealed class UnusedRegistryGradeNavigator : IRegistryGradeNavigator
    {
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) =>
            throw new NotSupportedException();

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink course) =>
            throw new NotSupportedException();

        public Task<AngleSharp.Dom.IDocument> GetHtml(Uri uri) =>
            throw new NotSupportedException();

        public Task SubmitGrades(
            AngleSharp.Dom.IDocument document,
            CancellationToken token,
            Action? onSubmissionStarted = null) =>
            throw new NotSupportedException();
    }
}
