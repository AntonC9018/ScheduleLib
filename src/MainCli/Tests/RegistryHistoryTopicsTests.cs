using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using ScheduleLib;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class RegistryHistoryTopicsTests
{
    private static readonly Uri Index = new("https://registry.test/studregistry/LessonAttendance");
    private const string Lessons = """
        <table class='table table-bordered'><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr>
        <tr><td><a href='/studregistry/LessonAttendance/View/1'>03.10.2024 09:45</a> laborator </td>
        <td>1 Synthetic first, &quot;quoted&quot; topic</td><td><a href='/studregistry/LessonAttendance/Edit/1'>Edit</a></td></tr>
        <tr><td><a>01.10.2024 08:00</a> curs </td><td>2 Synthetic theory</td><td></td></tr></table>
        <table><tr><td>canary-student</td><td>canary-attendance</td></tr></table>
        <a href='/studregistry/LessonAttendance/Create/1'>Add</a>
        """;

    [Fact]
    public void ExtractsOnlyDateTypeAndTopicWithoutAttendanceOrMutationLinks()
    {
        var lessons = RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(Lessons));
        Assert.Equal(2, lessons.Count);
        Assert.Equal(new DateTime(2024, 10, 3, 9, 45, 0), lessons[0].Date);
        Assert.Equal(LessonType.Lab, lessons[0].LessonType);
        Assert.Equal("Synthetic first, \"quoted\" topic", lessons[0].Topic);
        var json = JsonSerializer.Serialize(lessons);
        Assert.DoesNotContain("canary", json);
        Assert.DoesNotContain("href", json);
    }

    [Theory]
    [InlineData("1 ", "")]
    [InlineData("1 3 dimensional geometry", "3 dimensional geometry")]
    [InlineData("2024", "2024")]
    [InlineData("1 3D geometry", "3D geometry")]
    public void SequenceRemovalPreservesBlankPositionsAndTopicNumbers(string cell, string expected)
    {
        var html = "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr>" +
            "<tr><td><a>01.10.2024 08:00</a> curs</td><td>" + cell + "</td><td></td></tr></table>";
        Assert.Equal(expected, Assert.Single(RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(html))).Topic);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("<table><tr><td>canary-student</td></tr></table>")]
    [InlineData("<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr><tr><td><a>bad date</a> curs</td><td>private topic</td><td></td></tr></table>")]
    [InlineData("<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr><tr><td><a>01.10.2024 08:00</a> unknown</td><td>private topic</td><td></td></tr></table>")]
    public void ChangedLessonMarkupFailsWithGenericDiagnostics(string html)
    {
        var error = Assert.Throws<RegistryHistoryException>(() => RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(html)));
        Assert.DoesNotContain("canary", error.Message);
        Assert.DoesNotContain("private topic", error.Message);
    }

    [Fact]
    public void LatestPreviousYearPreservesAllGroupsAndTypesAndReportsAmbiguousCourses()
    {
        var current = new[] { Course(" Systems, ", 1, "current"), Course("Ambiguous", 1, "a"), Course("Missing", 2, "m"), Course("Știință", 1, "s") };
        var offered = new[] { "2025/26", "2023/24", "2024/25", "2026/27" };
        var historical = new[]
        {
            Snapshot("2023/24", "systems", 1, "old"), Snapshot("2024/25", "SYSTEMS", 1, "new"),
            Snapshot("2024/25", "Ambiguous", 1, "first"), Snapshot("2024/25", "ambiguous,", 1, "second"),
            Snapshot("2024/25", "Stiinta", 1, "diacritics")
        };
        var selection = RegistryHistoryTopicSelection.Select("2025/26", offered, current, historical);
        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal("2024/25", candidate.Historical.Year);
        Assert.Equal("new", Assert.Single(candidate.Historical.Groups).Name);
        Assert.Equal(2, candidate.Historical.Groups[0].Lessons.Count);
        Assert.Equal(3, selection.Unresolved.Count);
        Assert.Contains(selection.Unresolved, x => x.Reason == RegistryHistoryTopicMappingFailure.AmbiguousCourse);
        Assert.DoesNotContain(selection.Candidates, x => x.CurrentCourse.Name == "Știință");
    }

    [Fact]
    public void UnknownAcademicYearCannotBeOrderedOrGuessed() => Assert.Throws<RegistryHistoryException>(() =>
        RegistryHistoryTopicSelection.Select("current", ["current"], [], []));

    [Theory]
    [InlineData("/studregistry/LessonAttendance/LessonsList/1", true)]
    [InlineData("/studregistry/LessonAttendance/LessonsList/1?groupId=2", true)]
    [InlineData("/studregistry/LessonAttendance/LessonsList/1/Edit", false)]
    [InlineData("/studregistry/LessonAttendance/LessonsList/Create", false)]
    [InlineData("/studregistry/LessonAttendance/LessonsList/1?groupId=2&action=Delete", false)]
    [InlineData("/studregistry/LessonAttendance/LessonsList/1?unknown=2", false)]
    [InlineData("/studregistry/Evaluation/Index/1", false)]
    [InlineData("/studregistry/LessonAttendance/Edit/1", false)]
    public async Task OnlyExactLessonListReadsReachTransport(string path, bool allowed)
    {
        using var inner = new FakeHandler();
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new("https://registry.test/studregistry/Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        if (allowed) { using var response = await client.GetAsync(new Uri(Index, path)); }
        else await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(new Uri(Index, path)));
        Assert.Equal(allowed ? 1 : 0, inner.Calls);
    }

    [Fact]
    public async Task CandidateFilesArePrivateAppendOnlyCsvWithSeparateUnresolvedProvenance()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), ".registry-history");
        try
        {
            var selection = RegistryHistoryTopicSelection.Select("2025/26", ["2025/26", "2024/25"],
                [Course("Systems", 1, "current")], [Snapshot("2024/25", "Systems,", 1, "raw ambiguous group")]);
            var first = await RegistryHistoryTopicFiles.Save(directory, selection, default);
            var original = await File.ReadAllTextAsync(Path.Combine(directory, first.InventoryFile));
            var second = await RegistryHistoryTopicFiles.Save(directory, selection, default);
            Assert.NotEqual(first.InventoryFile, second.InventoryFile);
            Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(directory, first.InventoryFile)));
            var csv = await File.ReadAllTextAsync(Path.Combine(directory, first.CsvFiles[0]));
            Assert.StartsWith("name,lessonType", csv);
            Assert.Contains("\"Synthetic first, \"\"quoted\"\" topic\"", csv);
            using var csvInput = new StringReader(csv);
            var imported = new List<ScheduleLib.Application.Core.Topics.LessonTopic>();
            await foreach (var topic in ScheduleLib.Application.Core.Topics.LessonTopicCsvSerializer.Deserialize(csvInput, new(), default)) imported.Add(topic);
            Assert.Equal(2, first.CsvFiles.Count);
            Assert.Single(imported);
            Assert.Contains(imported, x => x.Name == "Synthetic first, \"quoted\" topic" && x.LessonType == LessonType.Lab);
            Assert.DoesNotContain("language", csv);
            Assert.DoesNotContain("faculty", original);
            Assert.Contains("raw ambiguous group", original);
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        finally { Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true); }
    }

    [Fact]
    public async Task ReaderUsesSerialCurrentAndMatchingHistoricalCourseReadsOnly()
    {
        var requests = new List<string>();
        var year = "2025/26";
        const string logout = "<a href='/studregistry/Account/Logout'>Exit</a>";
        using var inner = new FakeHandler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.PathAndQuery);
            string html;
            if (uri.AbsolutePath == Index.AbsolutePath)
            {
                if (uri.Query.Length > 0) year = "2024/25";
                html = "<form><select id='AnUniv' name='AnUniv'>" +
                    $"<option {(year == "2025/26" ? "selected" : "")} value='2025/26'>2025/26</option>" +
                    $"<option {(year == "2024/25" ? "selected" : "")} value='2024/25'>2024/25</option>" +
                    "</select></form><div id='nav-1'><div><span>1</span><span>" +
                    "<a href='/studregistry/LessonAttendance/groups/1'>Systems,</a></span></div>" +
                    (year == "2024/25" ? "<div><span>2</span><span><a href='/studregistry/LessonAttendance/groups/99'>Unrelated course</a></span></div>" : "") +
                    "</div><div id='nav-2'></div>";
            }
            else if (uri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal))
                html = """
                    <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                    <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Raw unknown partition</a>
                    <a href='/studregistry/Evaluation/Index/1'>grades</a></div></div></div></form>
                    """;
            else html = Lessons;
            return new(HttpStatusCode.OK) { Content = new StringContent(logout + html) };
        });
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new("https://registry.test/studregistry/Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var analysis = await new RegistryHistoryTopicsReader(client, Index).Discover(default);
        Assert.Equal("2025/26", analysis.Selection.CurrentYear);
        Assert.Equal("2024/25", Assert.Single(analysis.Selection.Candidates).Historical.Year);
        Assert.Single(analysis.Current);
        Assert.Equal(new[]
        {
            "/studregistry/LessonAttendance", "/studregistry/LessonAttendance/groups/1", "/studregistry/LessonAttendance/LessonsList/1?groupId=2",
            "/studregistry/LessonAttendance?AnUniv=2024%2F25", "/studregistry/LessonAttendance/groups/1", "/studregistry/LessonAttendance/LessonsList/1?groupId=2"
        }, requests);
        Assert.DoesNotContain("canary", JsonSerializer.Serialize(analysis));
    }

    [Fact]
    public void EmptyLessonTableDoesNotRequestStudentOrCreateData()
    {
        var lessons = RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(
            "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr></table><table><tr><td>canary-student</td></tr></table>"));
        Assert.Empty(lessons);
    }

    [Fact]
    public void LatestYearWithoutTopicsFallsBackAndSemesterRemainsPartOfMatching()
    {
        var empty = new RegistryHistoryCourseTopics("2024/25", Course("Systems", 1, "empty"), []);
        var selection = RegistryHistoryTopicSelection.Select("2025/26", ["2025/26", "2024/25", "2023/24"],
            [Course("Systems", 1, "current"), Course("Systems", 2, "other semester")],
            [empty, Snapshot("2023/24", "Systems", 1, "older")]);
        Assert.Equal("2023/24", Assert.Single(selection.Candidates).Historical.Year);
        Assert.Equal(2, Assert.Single(selection.Unresolved).CurrentCourse.Semester);
    }

    private static RegistryHistoryCourse Course(string name, int semester, string id) => new(name, semester, new(Index, "groups/" + id));
    private static RegistryHistoryCourseTopics Snapshot(string year, string name, int semester, string group) => new(year,
        Course(name, semester, group), [new(group, new(Index, "LessonsList/1"), RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(Lessons)))]);

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (respond is not null) return Task.FromResult(respond(request));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
