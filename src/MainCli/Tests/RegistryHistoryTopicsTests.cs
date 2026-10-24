using System.Net;
using System.Text.Json;
using AngleSharp.Html.Parser;
using ScheduleLib;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

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
        Assert.Equal(expected: 2, actual: lessons.Count);
        Assert.Equal(
            expected: new DateTime(
                year: 2024,
                month: 10,
                day: 3,
                hour: 9,
                minute: 45,
                second: 0),
            actual: lessons[0].Date);
        Assert.Equal(expected: LessonType.Lab, actual: lessons[0].LessonType);
        Assert.Equal(expected: "Synthetic first, \"quoted\" topic", actual: lessons[0].Topic);
        var json = JsonSerializer.Serialize(lessons);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: json);
        Assert.DoesNotContain(expectedSubstring: "href", actualString: json);
    }

    [Theory]
    [InlineData("1 ", "")]
    [InlineData("1 3 dimensional geometry", "3 dimensional geometry")]
    [InlineData("2024", "2024")]
    [InlineData("1 3D geometry", "3D geometry")]
    public void SequenceRemovalPreservesBlankPositionsAndTopicNumbers(string cell, string expected)
    {
        var html = (string) $"<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr> <tr><td><a>01.10.2024 08:00</a> curs</td><td>{cell}</td><td></td></tr></table>";
        Assert.Equal(
            expected: expected,
            actual: Assert.Single(RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(html))).Topic);
    }

    [Theory]
    [InlineData("<html></html>")]
    [InlineData("<table><tr><td>canary-student</td></tr></table>")]
    [InlineData(
        "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr><tr><td><a>bad date</a> curs</td><td>private topic</td><td></td></tr></table>")]
    [InlineData(
        "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr><tr><td><a>01.10.2024 08:00</a> unknown</td><td>private topic</td><td></td></tr></table>")]
    public void ChangedLessonMarkupFailsWithGenericDiagnostics(string html)
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
            RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(html)));
        Assert.Equal(expected: RegistryHistoryPhase.Lessons, actual: error.Phase);
        Assert.Equal(expected: RegistryHistoryDiagnostic.TopicTable, actual: error.Diagnostic);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        Assert.DoesNotContain(expectedSubstring: "private topic", actualString: error.Message);
    }

    [Fact]
    public void LatestPreviousYearPreservesAllGroupsAndTypesAndReportsAmbiguousCourses()
    {
        var current = new[]
        {
            Course(
                name: " Systems, ",
                1,
                id: "current"),
            Course(
                name: "Ambiguous",
                1,
                id: "a"),
            Course(
                name: "Missing",
                2,
                id: "m"),
            Course(
                name: "Știință",
                1,
                id: "s"),
        };
        var offered = new[]
        {
            "2025/26",
            "2023/24",
            "2024/25",
            "2026/27",
        };
        var historical = new[]
        {
            Snapshot(
                year: "2023/24",
                name: "systems",
                1,
                @group: "old"),
            Snapshot(
                year: "2024/25",
                name: "SYSTEMS",
                1,
                @group: "new"),
            Snapshot(
                year: "2024/25",
                name: "Ambiguous",
                1,
                @group: "first"),
            Snapshot(
                year: "2024/25",
                name: "ambiguous,",
                1,
                @group: "second"),
            Snapshot(
                year: "2024/25",
                name: "Stiinta",
                1,
                @group: "diacritics"),
        };
        var selection = RegistryHistoryTopicSelection.Select(
            "2025/26",
            offered,
            current,
            historical);
        var candidate = Assert.Single(selection.Candidates);
        Assert.Equal(expected: "2024/25", actual: candidate.Historical.Year);
        Assert.Equal(expected: "new", actual: Assert.Single(candidate.Historical.Groups).Name);
        Assert.Equal(expected: 2, actual: candidate.Historical.Groups[0].Lessons.Count);
        Assert.Equal(expected: 3, actual: selection.Unresolved.Count);
        Assert.Contains(selection.Unresolved, x => x.Reason == RegistryHistoryTopicMappingFailure.AmbiguousCourse);
        Assert.DoesNotContain(selection.Candidates, x => x.CurrentCourse.Name == "Știință");
    }

    [Fact]
    public void UnknownAcademicYearCannotBeOrderedOrGuessed()
    {
        Assert.Throws<RegistryHistoryException>(() =>
            RegistryHistoryTopicSelection.Select(
                "current",
                ["current"],
                [],
                []));
    }

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
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        if (allowed)
        {
            using var response = await client.GetAsync(new Uri(Index, path));
        }
        else
        {
            await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(new Uri(Index, path)));
        }

        Assert.Equal(expected: allowed ? 1 : 0, actual: inner.Calls);
    }

    [Fact]
    public async Task CandidateFilesArePrivateAppendOnlyCsvWithSeparateUnresolvedProvenance()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            ".registry-history");
        try
        {
            var selection = RegistryHistoryTopicSelection.Select(
                "2025/26",
                ["2025/26", "2024/25"],
                [
                    Course(
                        name: "Systems",
                        1,
                        id: "current"),
                ],
                [
                    Snapshot(
                        year: "2024/25",
                        name: "Systems,",
                        1,
                        @group: "raw ambiguous group"),
                ]);
            var first = await RegistryHistoryTopicFiles.Save(
                directory,
                selection,
                default);
            var original = await File.ReadAllTextAsync(Path.Combine(directory, first.InventoryFile));
            var second = await RegistryHistoryTopicFiles.Save(
                directory,
                selection,
                default);
            Assert.NotEqual(expected: first.InventoryFile, actual: second.InventoryFile);
            Assert.Equal(expected: original, actual: await File.ReadAllTextAsync(Path.Combine(directory, first.InventoryFile)));
            var csv = await File.ReadAllTextAsync(Path.Combine(directory, first.CsvFiles[0]));
            Assert.StartsWith(expectedStartString: "name,lessonType", actualString: csv);
            Assert.Contains(expectedSubstring: "\"Synthetic first, \"\"quoted\"\" topic\"", actualString: csv);
            using var csvInput = new StringReader(csv);
            var imported = new List<ScheduleLib.Application.Core.Topics.LessonTopic>();
            await foreach (var topic in ScheduleLib.Application.Core.Topics.LessonTopicCsvSerializer.Deserialize(
                               csvInput,
                               new(),
                               default))
            {
                imported.Add(topic);
            }

            Assert.Equal(expected: 2, actual: first.CsvFiles.Count);
            Assert.Single(imported);
            Assert.Contains(
                imported,
                x => x.Name == "Synthetic first, \"quoted\" topic" && x.LessonType == LessonType.Lab);
            Assert.DoesNotContain(expectedSubstring: "language", actualString: csv);
            Assert.DoesNotContain(expectedSubstring: "faculty", actualString: original);
            Assert.Contains(expectedSubstring: "raw ambiguous group", actualString: original);
            using var inventory = JsonDocument.Parse(original);
            Assert.All(
                inventory.RootElement.GetProperty("sources").EnumerateArray(),
                source =>
                    Assert.Equal(expected: "requires-group-mapping", actual: source.GetProperty("courseMapping").GetString()));
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
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
                if (uri.Query.Length > 0)
                {
                    year = "2024/25";
                }

                html = (string) $"<form><select id='AnUniv' name='AnUniv'>{($"<option {(year == "2025/26" ? "selected" : "")} value='2025/26'>2025/26</option>")}{($"<option {(year == "2024/25" ? "selected" : "")} value='2024/25'>2024/25</option>")}</select></form><div id='nav-1'><div><span>1</span><span> <a href='/studregistry/LessonAttendance/groups/1'>Systems,</a></span></div>{(year == "2024/25"
                        ? "<div><span>2</span><span><a href='/studregistry/LessonAttendance/groups/99'>Unrelated course</a></span></div>"
                        : "")}</div><div id='nav-2'></div>";
            }
            else if (uri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal))
            {
                html = """
                       <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                       <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Raw unknown partition</a>
                       <a href='/studregistry/Evaluation/Index/1'>grades</a></div></div></div></form>
                       """;
            }
            else
            {
                html = Lessons;
            }

            return new(HttpStatusCode.OK) { Content = new StringContent((string) $"{logout}{html}") };
        });
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var analysis = await new RegistryHistoryTopicsReader(client, Index).Discover(default);
        Assert.Equal(expected: "2025/26", actual: analysis.Selection.CurrentYear);
        Assert.Equal(expected: "2024/25", actual: Assert.Single(analysis.Selection.Candidates).Historical.Year);
        Assert.Single(analysis.Current);
        Assert.Equal(
            expected: new[]
            {
                "/studregistry/LessonAttendance",
                "/studregistry/LessonAttendance/groups/1",
                "/studregistry/LessonAttendance/LessonsList/1?groupId=2",
                "/studregistry/LessonAttendance?AnUniv=2024%2F25",
                "/studregistry/LessonAttendance/groups/1",
                "/studregistry/LessonAttendance/LessonsList/1?groupId=2",
            },
            actual: requests);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: JsonSerializer.Serialize(analysis));
    }

    [Fact]
    public async Task ReaderStopsBeforeOlderYearWhenLatestTopicsResolveCourse()
    {
        var (analysis, requests) = await ReadThreeYearHistory();
        Assert.Equal(expected: "2024/25", actual: Assert.Single(analysis.Selection.Candidates).Historical.Year);
        Assert.Single(analysis.Current);
        Assert.DoesNotContain(requests, path => path.Contains("2023%2F24", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, path => path.EndsWith("/groups/5", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, path => path.EndsWith("/LessonsList/5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReaderFallsBackAfterBlankLatestTopics()
    {
        var (analysis, requests) = await ReadThreeYearHistory(latestBlank: true);
        Assert.Equal(expected: "2023/24", actual: Assert.Single(analysis.Selection.Candidates).Historical.Year);
        Assert.Equal(expected: 2, actual: analysis.Historical!.Count);
        Assert.Contains(requests, path => path.Contains("2023%2F24", StringComparison.Ordinal));
        Assert.Contains(requests, path => path.EndsWith("/LessonsList/5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReaderSkipsResolvedCourseWhileAnotherCourseFallsBack()
    {
        var (analysis, requests) = await ReadThreeYearHistory(secondCourseFallsBack: true);
        Assert.Equal(expected: 2, actual: analysis.Current.Count);
        Assert.Equal(expected: 2, actual: analysis.Selection.Candidates.Count);
        Assert.Contains(
            analysis.Selection.Candidates,
            candidate => candidate.CurrentCourse.Name == "Systems" && candidate.Historical.Year == "2024/25");
        Assert.Contains(
            analysis.Selection.Candidates,
            candidate => candidate.CurrentCourse.Name == "Other" && candidate.Historical.Year == "2023/24");
        Assert.Contains(requests, path => path.EndsWith("/LessonsList/8", StringComparison.Ordinal));
        Assert.DoesNotContain(
            requests,
            path => path.EndsWith("/groups/5", StringComparison.Ordinal)
                || path.EndsWith("/LessonsList/5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReaderKeepsAmbiguousHistoricalSnapshotsButSkipsOlderYears()
    {
        var (analysis, requests) = await ReadThreeYearHistory(latestDuplicate: true);
        Assert.Empty(analysis.Selection.Candidates);
        Assert.Equal(
            expected: RegistryHistoryTopicMappingFailure.AmbiguousCourse,
            actual: Assert.Single(analysis.Selection.Unresolved).Reason);
        Assert.Equal(expected: 2, actual: analysis.Historical!.Count);
        Assert.Contains(requests, path => path.EndsWith("/groups/3", StringComparison.Ordinal));
        Assert.Contains(requests, path => path.EndsWith("/groups/4", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, path => path.Contains("2023%2F24", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateCurrentCoursesStopAfterNewestUsableHistoricalSnapshots()
    {
        var (analysis, requests) = await ReadThreeYearHistory(duplicateCurrent: true);
        Assert.Equal(expected: 2, actual: analysis.Current.Count);
        Assert.Equal(expected: 2, actual: analysis.Selection.Unresolved.Count);
        Assert.All(
            analysis.Selection.Unresolved,
            item => Assert.Equal(expected: RegistryHistoryTopicMappingFailure.AmbiguousCourse, actual: item.Reason));
        Assert.Single(analysis.Historical!);
        Assert.Contains(requests, path => path.EndsWith("/LessonsList/3", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, path => path.Contains("2023%2F24", StringComparison.Ordinal));
        Assert.DoesNotContain(requests, path => path.EndsWith("/LessonsList/5", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DuplicateCurrentCoursesFallBackWhenNewestTopicsAreBlank()
    {
        var (analysis, requests) = await ReadThreeYearHistory(duplicateCurrent: true, latestBlank: true);
        Assert.Equal(expected: 2, actual: analysis.Historical!.Count);
        Assert.Contains(requests, path => path.EndsWith("/LessonsList/5", StringComparison.Ordinal));
        Assert.All(
            analysis.Selection.Unresolved,
            item => Assert.Equal(expected: RegistryHistoryTopicMappingFailure.AmbiguousCourse, actual: item.Reason));
    }

    [Fact]
    public async Task AmbiguousCurrentCourseExportsPrivateCsvCandidatesAndKeepsUnresolvedStatus()
    {
        var (analysis, _) = await ReadThreeYearHistory(duplicateCurrent: true);
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            ".registry-history");
        try
        {
            var result = await RegistryHistoryTopicFiles.Save(
                directory,
                analysis,
                default);
            Assert.Equal(expected: 2, actual: result.CsvFiles.Count);
            using var inventory =
                JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, result.InventoryFile)));
            Assert.Equal(expected: 2, actual: inventory.RootElement.GetProperty("unresolved").GetArrayLength());
            Assert.Equal(expected: 1, actual: inventory.RootElement.GetProperty("historical").GetArrayLength());
            var sources = inventory.RootElement.GetProperty("sources");
            Assert.Equal(expected: 2, actual: sources.GetArrayLength());
            Assert.All(
                sources.EnumerateArray(),
                source =>
                {
                    Assert.Equal(expected: "ambiguous-course", actual: source.GetProperty("courseMapping").GetString());
                    Assert.Equal(expected: "2024/25", actual: source.GetProperty("year").GetString());
                });
            Assert.Contains(
                expectedSubstring: "Synthetic first",
                actualString: await File.ReadAllTextAsync(Path.Combine(directory, result.CsvFiles[0])));
            Assert.False(File.Exists(Path.Combine(directory, "manifest.json")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    [Fact]
    public async Task AmbiguousHistoricalYearExportsEveryMatchingSnapshot()
    {
        var (analysis, _) = await ReadThreeYearHistory(latestDuplicate: true);
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            ".registry-history");
        try
        {
            var result = await RegistryHistoryTopicFiles.Save(
                directory,
                analysis,
                default);
            Assert.Equal(expected: 4, actual: result.CsvFiles.Count);
            using var inventory =
                JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, result.InventoryFile)));
            var sources = inventory.RootElement.GetProperty("sources");
            Assert.Equal(expected: 4, actual: sources.GetArrayLength());
            Assert.All(
                sources.EnumerateArray(),
                source => Assert.Equal(expected: "ambiguous-course", actual: source.GetProperty("courseMapping").GetString()));
            Assert.Equal(expected: 1, actual: inventory.RootElement.GetProperty("unresolved").GetArrayLength());
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    private static async Task<(RegistryHistoryTopicsAnalysis Analysis, List<string> Requests)> ReadThreeYearHistory(
        bool latestBlank = false,
        bool latestDuplicate = false,
        bool duplicateCurrent = false,
        bool secondCourseFallsBack = false)
    {
        var requests = new List<string>();
        const string logout = "<a href='/studregistry/Account/Logout'>Exit</a>";
        using var inner = new FakeHandler(request =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.PathAndQuery);
            string html;
            if (uri.AbsolutePath == Index.AbsolutePath)
            {
                var year = uri.Query.Contains("2024%2F25", StringComparison.Ordinal) ? "2024/25"
                    : uri.Query.Contains("2023%2F24", StringComparison.Ordinal) ? "2023/24" : "2025/26";
                var courseIds = year == "2025/26" ? duplicateCurrent ? new[]
                    {
                        1,
                        2,
                    } :
                    secondCourseFallsBack ? [1, 6] : [1]
                    : year == "2024/25" ? latestDuplicate ? new[]
                    {
                        3,
                        4,
                    } :
                    secondCourseFallsBack ? [3, 7] : [3]
                    : secondCourseFallsBack ? [5, 8] : [5];
                html = (string) $"<form><select id='AnUniv' name='AnUniv'>{string.Concat(
                        new[]
                        {
                            "2025/26",
                            "2024/25",
                            "2023/24",
                        }.Select(value =>
                            $"<option {(value == year ? "selected" : "")} value='{value}'>{value}</option>"))}</select></form><div id='nav-1'>{string.Concat(
                        courseIds.Select(id =>
                            $"<div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/{id}'>{(id >= 6 ? "Other" : "Systems")}</a></span></div>"))}</div><div id='nav-2'></div>";
            }
            else if (uri.AbsolutePath.Contains("/groups/", StringComparison.Ordinal))
            {
                var id = uri.AbsolutePath.Split('/').Last();
                html =
                    (string) $"<form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>{($"<a href='/studregistry/LessonAttendance/LessonsList/{id}'>Group</a>")}<a href='/studregistry/Evaluation/Index/1'>grades</a></div></div></div></form>";
            }
            else
            {
                html = (latestBlank && uri.AbsolutePath.EndsWith("/LessonsList/3", StringComparison.Ordinal)
                    || secondCourseFallsBack && uri.AbsolutePath.EndsWith("/LessonsList/7", StringComparison.Ordinal))
                    ? "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr>"
                    + "<tr><td><a>01.10.2024 08:00</a> curs</td><td>1 </td><td></td></tr></table>"
                    : Lessons;
            }

            return new(HttpStatusCode.OK) { Content = new StringContent((string) $"{logout}{html}") };
        });
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        return (await new RegistryHistoryTopicsReader(client, Index).Discover(default), requests);
    }

    [Fact]
    public void EmptyLessonTableDoesNotRequestStudentOrCreateData()
    {
        var lessons = RegistryHistoryTopicsHtml.Lessons(
            new HtmlParser().ParseDocument(
                "<table><tr><th>Data</th><th>Tema</th><th>Redactare</th></tr></table><table><tr><td>canary-student</td></tr></table>"));
        Assert.Empty(lessons);
    }

    [Fact]
    public void LatestYearWithoutTopicsFallsBackAndSemesterRemainsPartOfMatching()
    {
        var empty = new RegistryHistoryCourseTopics(
            "2024/25",
            Course(
                name: "Systems",
                1,
                id: "empty"),
            []);
        var selection = RegistryHistoryTopicSelection.Select(
            "2025/26",
            ["2025/26", "2024/25", "2023/24"],
            [
                Course(
                    name: "Systems",
                    1,
                    id: "current"),
                Course(
                    name: "Systems",
                    2,
                    id: "other semester"),
            ],
            [
                empty, Snapshot(
                    year: "2023/24",
                    name: "Systems",
                    1,
                    @group: "older"),
            ]);
        Assert.Equal(expected: "2023/24", actual: Assert.Single(selection.Candidates).Historical.Year);
        Assert.Equal(expected: 2, actual: Assert.Single(selection.Unresolved).CurrentCourse.Semester);
    }

    [Fact]
    public async Task ExportKeepsOnlyRegularTopicsAndTheirDates()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString("N"),
            ".registry-history");
        try
        {
            var course = Course(
                name: "Synthetic course",
                1,
                id: "current");
            var date = new DateTime(
                year: 2024,
                month: 10,
                day: 7,
                hour: 9,
                minute: 45,
                second: 0);
            var historical = new RegistryHistoryCourseTopics(
                "2024/25",
                course,
                [
                    new(
                        "IA2401",
                        new(Index, "LessonsList/1"),
                        [
                            new(
                                date,
                                LessonType.Curs,
                                "Synthetic first"),
                            new(
                                date.AddDays(7),
                                LessonType.Curs,
                                "Atestare 1"),
                            new(
                                date.AddDays(14),
                                LessonType.Curs,
                                "Synthetic second"),
                        ]),
                ]);
            var selection = new RegistryHistoryTopicSelectionResult(
                "2025/26",
                [new(course, historical)],
                []);
            var result = await RegistryHistoryTopicFiles.Save(
                directory,
                selection,
                default);
            var csv = await File.ReadAllTextAsync(Path.Combine(directory, Assert.Single(result.CsvFiles)));
            Assert.DoesNotContain(expectedSubstring: "Atestare", actualString: csv);
            Assert.Contains(expectedSubstring: "Synthetic first", actualString: csv);
            Assert.Contains(expectedSubstring: "Synthetic second", actualString: csv);
            using var inventory =
                JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory, result.InventoryFile)));
            var dates = Assert.Single(inventory.RootElement.GetProperty("sources").EnumerateArray())
                .GetProperty("dates");
            Assert.Equal(
                expected: new[]
                {
                    date,
                    date.AddDays(14),
                },
                actual: dates.EnumerateArray().Select(x => x.GetDateTime()));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(directory)!, recursive: true);
        }
    }

    private static RegistryHistoryCourse Course(
        string name,
        int semester,
        string id)
    {
        return new(
        name,
        semester,
        new(Index, (string) $"groups/{id}"));
    }

    private static RegistryHistoryCourseTopics Snapshot(
        string year,
        string name,
        int semester,
        string group)
    {
        return new(
        year,
        Course(
            name: name,
            semester,
            id: group),
        [
            new(
                group,
                new(Index, "LessonsList/1"),
                RegistryHistoryTopicsHtml.Lessons(new HtmlParser().ParseDocument(Lessons))),
        ]);
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage>? respond = null) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            if (respond is not null)
            {
                return Task.FromResult(respond(request));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
