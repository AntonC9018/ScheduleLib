using AngleSharp;
using AngleSharp.Html.Dom;
using QuizModels;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
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
    public void ParseDateTimeHandlesPlaceholdersAndRomanianFormats()
    {
        Assert.Null(MoodleQuizHtmlParser.ParseDateTime("-"));
        Assert.Null(MoodleQuizHtmlParser.ParseDateTime(" – "));

        var parsed = MoodleQuizHtmlParser.ParseDateTime("16 octombrie 2025  13:28");
        Assert.Equal(
            expected: new DateTime(
                year: 2025,
                month: 10,
                day: 16,
                hour: 13,
                minute: 28,
                second: 0),
            actual: parsed);

        Assert.Throws<MoodleDateTimeParseException>(() => MoodleQuizHtmlParser.ParseDateTime("not a date"));
    }

    [Fact]
    public async Task ParseAttemptsTableSkipsMalformedRowAndHandlesFormattedNameAndDashFinish()
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
                  <td><input type="checkbox" value="2" /></td>
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

        var attempts = MoodleQuizHtmlParser.ParseAttemptsTable(table);

        Assert.Equal(expected: 2, actual: attempts.Count);
        Assert.Equal(expected: "Popescu Ion", actual: attempts[0].UserName);
        Assert.Null(attempts[0].TimeFinish);
        Assert.Equal(expected: QuestionStatus.Incorrect, actual: attempts[0].QuestionGrades[0].Status);
        Assert.Equal(expected: "Rusu Ana", actual: attempts[1].UserName);
        Assert.NotNull(attempts[1].TimeFinish);
        Assert.Equal(expected: QuestionStatus.PartiallyCorrect, actual: attempts[1].QuestionGrades[0].Status);
    }

    [Fact]
    public void OutputDirectoryEnforcesBoundaryAndAllowsRoot()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "schedulelib-boundary-check");
        var output = new OutputDirectory(baseDir);

        string inside = output.BuildPath("nested/file.txt");
        Assert.Equal(expected: Path.GetFullPath(Path.Combine(baseDir, "nested/file.txt")), actual: inside);

        Assert.Throws<FullPathOutsideTempOutputDirectoryException>(() => output.BuildPath("../escape.txt"));
        Assert.Throws<FullPathOutsideTempOutputDirectoryException>(() => output.BuildPath($"{baseDir}-evil/file.txt"));

        var rootPath = Path.GetPathRoot(baseDir)!;
        var rootOutput = new OutputDirectory(rootPath);
        string underRoot = rootOutput.BuildPath("file.txt");
        Assert.Equal(expected: Path.GetFullPath(Path.Combine(rootPath, "file.txt")), actual: underRoot);
    }

    [Fact]
    public void ScheduleBuilderCourseRejectsDuplicateAliasesWithoutMutatingCourses()
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        builder.Course("Math", "Mathematics");

        int countBefore = builder.Courses.Count;
        Assert.Throws<DuplicateCourseNameException>(() => builder.Course("Algebra", "Mathematics"));
        Assert.Throws<DuplicateCourseNameException>(() => builder.Course("Physics", "physics"));
        Assert.Equal(expected: countBefore, actual: builder.Courses.Count);
    }

    [Fact]
    public void DayHelperGetDayOfThisWeekHandlesSundayAsEndOfCurrentWeek()
    {
        var sunday = new DateOnly(
            year: 2026,
            month: 11,
            day: 22);
        var monday = sunday.GetDayOfThisWeek(DayOfWeek.Monday);
        Assert.Equal(
            expected: new DateOnly(
                year: 2026,
                month: 11,
                day: 16),
            actual: monday);
    }
}
