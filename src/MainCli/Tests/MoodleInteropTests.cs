using AngleSharp;
using AngleSharp.Html.Dom;
using QuizModels;
using ScheduleLib;
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
    [InlineData("52 min 41 secunde", 0, 52, 41)]
    [InlineData("1 oră 15 min 3 secunde", 1, 15, 3)]
    [InlineData("2 ore 5 min", 2, 5, 0)]
    public void ParseDurationIncludesHoursMinutesAndSeconds(
        string text,
        int hours,
        int minutes,
        int seconds)
    {
        var actual = MoodleQuizHtmlParser.ParseDuration(text);
        var expected = new TimeSpan(
            hours: hours,
            minutes: minutes,
            seconds: seconds);
        Assert.Equal(expected: expected, actual: actual);
    }

    [Fact]
    public async Task ParseAttemptsTableSkipsMalformedRowWithoutTruncatingLaterAttempts()
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
                  <th><a data-sortby="sumgrades">Grade</a></th>
                  <th><a data-sortby="qsgrade1">Q1</a></th>
                </tr>
              </thead>
              <tbody>
                <tr>
                  <td><input type="checkbox" value="1" /></td>
                  <td>Popescu Ion<a class="reviewlink" href="/review/1">Review</a></td>
                  <td>ion@example.com</td>
                  <td>Finalizate</td>
                  <td>9,50</td>
                  <td><a href="/q/1"><span class="incorrect">0,00</span></a></td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="2" /></td>
                  <td></td>
                  <td>empty@example.com</td>
                  <td>Finalizate</td>
                  <td>0,00</td>
                  <td></td>
                </tr>
                <tr>
                  <td><input type="checkbox" value="3" /></td>
                  <td>Rusu Ana<a class="reviewlink" href="/review/3">Review</a></td>
                  <td>ana@example.com</td>
                  <td>Finalizate</td>
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
        Assert.Equal(expected: QuestionStatus.Incorrect, actual: attempts[0].QuestionGrades[0].Status);
        Assert.Equal(expected: "Rusu Ana", actual: attempts[1].UserName);
        Assert.Equal(expected: QuestionStatus.PartiallyCorrect, actual: attempts[1].QuestionGrades[0].Status);
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
