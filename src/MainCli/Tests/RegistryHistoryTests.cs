using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;

public sealed class RegistryHistoryTests
{
    private static readonly Uri Index = new("https://registry.test/studregistry/LessonAttendance");

    private const string Years = """
                                 <a href='/studregistry/Account/Logout'>Exit</a>
                                 <form id='search'><select name='AnUniv' id='AnUniv'>
                                 <option selected value='2025/26'>2025/26</option><option value='2024/25'>2024/25</option>
                                 <option value='2024/25'>duplicate</option><option disabled value='2023/24'>disabled</option>
                                 </select></form>
                                 """;

    [Fact]
    public void TokenOnlyHistoricalLessonFormHasNoAssignedGroups()
    {
        using var document = new HtmlParser().ParseDocument(
            """
            <h1>Synthetic course</h1><h3>Groups</h3>
            <form name='lesson'><input type='hidden' name='__RequestVerificationToken' value='canary-token'></form>
            """);
        Assert.Empty(RegistryHistoryHtml.Groups(document, Index));
    }

    [Theory]
    [InlineData("<form name='lesson'></form>")]
    [InlineData("<form name='lesson'><input type='hidden' name='unknown' value='canary-token'></form>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'><div>Changed group structure</div></form>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'></form><a href='/studregistry/LessonAttendance/LessonsList/1'>Synthetic group</a>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'></form><a href='https://foreign.test/other/LessonAttendance/LessonsList/1'>Synthetic group</a>")]
    public void UnrecognizedEmptyOrMovedGroupLayoutStillFails(string html)
    {
        using var document = new HtmlParser().ParseDocument(html);
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Groups(document, Index));
    }

    [Fact]
    public async Task HistoricalNavigationReselectsCurrentBeforeReadingItsGroups()
    {
        var activeYear = "2025/26";
        var groupYears = new List<string>();
        var requests = new List<string>();
        using var inner = new FakeHandler((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.PathAndQuery);
            string html;
            if (uri.AbsolutePath == Index.AbsolutePath)
            {
                if (uri.Query.Length > 0)
                {
                    activeYear = Uri.UnescapeDataString(uri.Query[8..]);
                }

                html = (string) $"<form><select id='AnUniv' name='AnUniv'>{($"<option {(activeYear == "2024/25" ? "selected" : "")} value='2024/25'>2024/25</option>")}{($"<option {(activeYear == "2025/26" ? "selected" : "")} value='2025/26'>2025/26</option>")}</select></form><div id='nav-1'><div><span>1</span><span> <a href='/studregistry/LessonAttendance/groups/1'>Synthetic</a></span></div></div><div id='nav-2'></div>";
            }
            else
            {
                groupYears.Add(activeYear);
                html =
                    (string) $"<form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>{($"<a href='/studregistry/LessonAttendance/LessonsList/1'>{activeYear}</a>")}<a href='/studregistry/Evaluation/Index/1'>grades</a></div></div></div></form>";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent((string) $"<a href='/studregistry/Account/Logout'>Exit</a>{html}"),
            });
        });
        using var client = new HttpClient(inner);
        var summaries = await new RegistryHistoryReader(client, Index).Discover(null, default);
        Assert.Equal(
            expected: new[]
            {
                "2024/25",
                "2025/26",
            },
            actual: groupYears);
        Assert.Equal(
            expected: new[]
            {
                "2024/25",
                "2025/26",
            },
            actual: summaries.Select(x => x.Year));
        Assert.Equal(expected: "2025/26", actual: Assert.Single(Assert.Single(summaries[1].Courses).Groups));
        Assert.Contains("/studregistry/LessonAttendance?AnUniv=2025%2F26", requests);
    }

    [Fact]
    public void YearNavigationUsesObservedGetFormAndEncodedValue()
    {
        var years = RegistryHistoryHtml.Years(new HtmlParser().ParseDocument(Years), Index);
        Assert.Equal(
            expected: new[]
            {
                "2025/26",
                "2024/25",
            },
            actual: years.Select(x => x.Value));
        Assert.True(years[0].Selected);
        Assert.Equal(expected: "https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25", actual: years[1].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(
        "<form method='post'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<form action='https://other.test/'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Unknown,
        RegistryHistoryDiagnostic.RequestPolicy)]
    [InlineData(
        "<form><input name='token' value='private'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<select id='AnUniv' name='AnUniv'><option>2024/25</option></select>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<html>no year selector</html>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    public void UnsupportedYearMarkupFailsExplicitly(
        string html,
        RegistryHistoryPhase phase,
        RegistryHistoryDiagnostic diagnostic)
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
            RegistryHistoryHtml.Years(new HtmlParser().ParseDocument(html), Index));
        Assert.Equal(expected: phase, actual: error.Phase);
        Assert.Equal(expected: diagnostic, actual: error.Diagnostic);
    }

    [Theory]
    [InlineData("<form name='lesson'></form>")]
    [InlineData(
        "<form name='lesson'><div class='row'></div><div class='row'><div class='col'><a href='/studregistry/LessonAttendance/LessonsList/1'>changed layout</a></div></div></form>")]
    public void ChangedGroupContainerDoesNotSilentlyProduceAnEmptySummary(string html)
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
            RegistryHistoryHtml.Groups(new HtmlParser().ParseDocument(html), Index));
        Assert.Equal(expected: RegistryHistoryPhase.Groups, actual: error.Phase);
        Assert.Contains(
            error.Diagnostic,
            new[]
            {
                RegistryHistoryDiagnostic.GroupContainer,
                RegistryHistoryDiagnostic.GroupLayout,
            });
    }

    [Fact]
    public void ChangedCourseContainerDoesNotSilentlyProduceAnEmptySummary()
    {
        var error = Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Courses(
            new HtmlParser().ParseDocument(
                """
                <div id='nav-1'><a href='/studregistry/LessonAttendance/groups/1'>changed layout</a></div><div id='nav-2'></div>
                """),
            Index));
        Assert.Equal(expected: RegistryHistoryPhase.Index, actual: error.Phase);
        Assert.Equal(expected: RegistryHistoryDiagnostic.CourseLayout, actual: error.Diagnostic);
    }

    [Fact]
    public void RawHistoricalNamesSurviveWithoutScheduleMatchingAndUnsafeLinksFail()
    {
        var document = new HtmlParser().ParseDocument(
            """
            <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'> Old &amp; unmapped, </a></span></div></div>
            <div id='nav-2'><div><span>2</span><span><a href='/studregistry/LessonAttendance/groups/2'>Other,</a></span></div></div>
            """);
        var courses = RegistryHistoryHtml.Courses(document, Index);
        Assert.Equal(
            expected: new[]
            {
                "Old & unmapped,",
                "Other,",
            },
            actual: courses.Select(x => x.Name));
        Assert.Equal(
            expected: new[]
            {
                1,
                2,
            },
            actual: courses.Select(x => x.Semester));
        var groups = RegistryHistoryHtml.Groups(
            new HtmlParser().ParseDocument(
                """
                <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Repeat / unknown notation</a>
                <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a>
                </div></div></div></form>
                """),
            Index);
        Assert.Equal(expected: "Repeat / unknown notation", actual: Assert.Single(groups).Name);
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Courses(
            new HtmlParser().ParseDocument(
                document.DocumentElement.OuterHtml.Replace(
                    "/studregistry/LessonAttendance/groups/1",
                    "https://other.test/studregistry/LessonAttendance/groups/1")),
            Index));
    }

    [Theory]
    [InlineData("https://registry.test/studregistry/LessonAttendance", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=1999%2F00", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F26", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25&AnUniv=2023%2F24", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%252F25", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?action=Delete", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance#Edit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1?groupId=2", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1?action=Delete", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1/Edit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/Create", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1%2FEdit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1/", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1#Edit", false)]
    [InlineData("https://other.test/studregistry/LessonAttendance/groups/1", false)]
    [InlineData("https://registry.test/other/LessonAttendance/groups/1", false)]
    public async Task OnlyExactIndexAndObservedNumericGroupReadsReachTransport(string address, bool allowed)
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new(Index, "../Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        if (allowed)
        {
            using var response = await client.GetAsync(address);
        }
        else
        {
            await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(address));
        }

        Assert.Equal(expected: allowed ? 1 : 0, actual: inner.Calls);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(302)]
    public async Task RejectionStopsWithoutReplayOrFollowingRedirect(int status)
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode) status)));
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new(Index, "../Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.DoesNotContain(expectedSubstring: "registry.test", actualString: error.Message);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(expected: 1, actual: inner.Calls);
    }

    [Theory]
    [InlineData(
        "login",
        400,
        RegistryHistoryPhase.Login)]
    [InlineData(
        "login",
        403,
        RegistryHistoryPhase.Login)]
    [InlineData(
        "index",
        302,
        RegistryHistoryPhase.Index)]
    [InlineData(
        "index",
        429,
        RegistryHistoryPhase.Index)]
    [InlineData(
        "groups",
        403,
        RegistryHistoryPhase.Groups)]
    public async Task RejectionCarriesOnlyFixedPhaseAndNumericStatus(
        string route,
        int status,
        RegistryHistoryPhase phase)
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode) status) { Content = new StringContent("canary-response-body") }));
        var login = new Uri("https://registry.test/studregistry/Account/Login");
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: login,
            TimeSpan.FromMilliseconds(1));
        using var client = new HttpClient(guard);
        using var content = new StringContent("canary-password");
        Task<HttpResponseMessage> request = route switch
        {
            "login" => client.PostAsync(login, content),
            "index" => client.GetAsync(Index),
            "groups" => client.GetAsync(new Uri("https://registry.test/studregistry/LessonAttendance/groups/1")),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => request);
        Assert.Equal(expected: phase, actual: error.Phase);
        Assert.Equal(expected: status, actual: error.StatusCode);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        Assert.Equal(expected: 1, actual: inner.Calls);
    }

    [Fact]
    public async Task ReadsRetryAtMostOnceAndMutationNeverReachesTransport()
    {
        using var inner = new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new(Index, "../Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(expected: 2, actual: inner.Calls);
        using var content = new StringContent("canary-password");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        Assert.Equal(expected: 2, actual: inner.Calls);
    }

    [Fact]
    public async Task FreshHistorySessionBlocksMutationAndLessonEditRoutes()
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        using var content = new StringContent("synthetic");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        await Assert.ThrowsAsync<RegistryHistoryException>(() =>
            client.DeleteAsync(new Uri((string) $"{Index.AbsoluteUri}/groups/1")));
        await Assert.ThrowsAsync<RegistryHistoryException>(() =>
            client.GetAsync(new Uri((string) $"{Index.AbsoluteUri}/Edit/1")));
        Assert.Equal(expected: 0, actual: inner.Calls);
    }

    [Fact]
    public async Task GlobalGateSerializesDifferentAccountsAndSpacesRequests()
    {
        var active = 0;
        var maximum = 0;
        var starts = new List<DateTimeOffset>();

        async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken token)
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            starts.Add(DateTimeOffset.UtcNow);
            await Task.Delay(20, token);
            Interlocked.Decrement(ref active);
            return new(HttpStatusCode.OK) { Content = new StringContent("safe") };
        }

        using var innerA = new FakeHandler(Respond);
        using var innerB = new FakeHandler(Respond);
        using var guardA = new RegistryHistoryRequestHandler(
            innerA,
            index: Index,
            login: new(Index, "../Account/Login"),
            TimeSpan.FromMilliseconds(30));
        using var guardB = new RegistryHistoryRequestHandler(
            innerB,
            index: Index,
            login: new(Index, "../Account/Login"),
            TimeSpan.FromMilliseconds(30));
        using var first = new HttpClient(guardA);
        using var second = new HttpClient(guardB);
        // Intentionally race only synthetic transports to prove the shared gate.
        var a = first.GetAsync(Index);
        var b = second.GetAsync(Index);
        using var responseA = await a;
        using var responseB = await b;
        Assert.Equal(expected: 1, actual: maximum);
        Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public void ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext()
    {
        var log = new CaptureLogger();
        var logger = new RegistryErrorLogger(log);
        logger.StudentsNotInDbButInRegistry(new(
                PrivateStudents(),
                null!,
                default,
                default));
        logger.LessonsDecidedErroneous(new("canary-student canary-password canary-token"));
        logger.GroupParsingError(new(new GroupParsingException("canary-password canary-student"),
            "canary-student canary-token"));
        logger.GroupNotFound("canary-student canary-token");
        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: string.Join("\n", log.Messages));

        static IEnumerable<ScheduleLib.Parsing.Name> PrivateStudents()
        {
            return new[]
            {
                "canary-student",
            }.Select<string, ScheduleLib.Parsing.Name>(_ =>
                throw new Exception("Private students must not be enumerated by logging."));
        }
    }

    [Fact]
    public void DuplicateStudentDiagnosticDoesNotIncludeTheirName()
    {
        var builder = new StudentAttendanceListBuilder();
        var name = ScheduleLib.Parsing.NameHelper.Parse("Canary Student");
        builder.Student(name);
        var error = Assert.Throws<InvalidOperationException>(() => builder.Student(name));
        Assert.DoesNotContain(expectedSubstring: "Canary", actualString: error.Message);
    }

    [Fact]
    public async Task ProductionLoginBuilderAndYearDiscoveryUseOnlySyntheticCredentialsAndSerialReads()
    {
        var log = new CaptureLogger();
        using var logs = LoggerFactory.Create(x => x.AddProvider(new CaptureProvider(log)));
        var login = new Uri("https://registry.test/studregistry/Account/Login");
        var requests = new List<string>();
        using var inner = new FakeHandler(async (request, token) =>
        {
            requests.Add((string) $"{(request.Method + " ")}{request.RequestUri!.PathAndQuery}");
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Assert.Contains(expectedSubstring: "canary-password", actualString: body);
                return new(HttpStatusCode.Redirect);
            }
            var historical = request.RequestUri.Query.Contains("2024%2F25", StringComparison.Ordinal);
            var years = historical
                ? Years.Replace("selected value='2025/26'", "value='2025/26'").Replace(
                    "value='2024/25'>2024/25",
                    "selected value='2024/25'>2024/25")
                : Years;
            var html = request.RequestUri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal)
                ? """
                  <a href='/studregistry/Account/Logout'>Exit</a>
                  <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                  <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Unknown historical group</a>
                  <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a></div></div></div></form>
                  <table><tr><td>canary-student</td><td>private grade</td></tr></table>
                  """
                : (string) $"{years}<div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped historical course,</a></span></div></div><div id='nav-2'></div>";
            return new(HttpStatusCode.OK) { Content = new StringContent(html) };
        });
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: login,
            TimeSpan.FromMilliseconds(1));
        using var client = new HttpClient(guard);
        using var transport = new HttpClientContext(
            new AngleSharp.Io.MemoryCookieProvider(),
            client,
            guard);
        var builder = new ScrapingContextBuilder();
        builder.AddLogging(logs);
        builder.TokenAuth(
            new TokenNamesConfig
            {
                BaseUrl = new("https://registry.test/studregistry/"),
                LoginUrl = login,
                TokenCookieName = "synthetic",
            },
            x => x.PasswordLoginCall(
                new()
                {
                    Login = "canary-login",
                    Password = "canary-password",
                },
                new()
                {
                    Login = "UserLogin",
                    Password = "UserPassword",
                }));
        using var context = await builder.Build(default, transport);
        guard.CompleteAuthentication();
        var summary = await new RegistryHistoryReader(context.HttpClient, Index).Discover("2024/25", default);
        Assert.Equal(expected: "2024/25", actual: Assert.Single(summary).Year);
        Assert.Equal(expected: "Unknown historical group", actual: Assert.Single(Assert.Single(summary[0].Courses).Groups));
        Assert.Equal(
            expected: new[]
            {
                "POST /studregistry/Account/Login",
                "GET /studregistry/LessonAttendance",
                "GET /studregistry/LessonAttendance?AnUniv=2024%2F25",
                "GET /studregistry/LessonAttendance/groups/1",
            },
            actual: requests);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: string.Join("\n", log.Messages));
        Assert.DoesNotContain(expectedSubstring: "canary-student", actualString: System.Text.Json.JsonSerializer.Serialize(summary));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginPageOrIgnoredYearStopsBeforeGroupReads(bool loginPage)
    {
        using var inner = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                loginPage
                    ? "<input type='password' value='canary-password'>"
                    : Years + "<div id='nav-1'></div><div id='nav-2'></div>"),
        }));
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() =>
            new RegistryHistoryReader(client, Index).Discover("2024/25", default));
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        Assert.Equal(expected: loginPage ? 1 : 2, actual: inner.Calls);
    }

    [Fact]
    public async Task CurrentYearUsesSelectedRegistryValueWithoutHistoricalRequests()
    {
        var queries = new List<string>();
        using var inner = new FakeHandler((request, _) =>
        {
            queries.Add(request.RequestUri!.PathAndQuery);
            var html = request.RequestUri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal)
                ? """
                  <a href='/studregistry/Account/Logout'>Exit</a>
                  <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                  <a href='/studregistry/LessonAttendance/LessonsList/1'>Unmapped group</a><a href='/studregistry/Evaluation/Index/1'>grades</a>
                  </div></div></div></form>
                  """
                : Years
                + """
                  <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped course</a></span></div></div><div id='nav-2'></div>
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        });
        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: new("https://registry.test/studregistry/Account/Login"),
            TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var summary = Assert.Single(await new RegistryHistoryReader(client, Index).Discover("current", default));
        Assert.Equal(expected: "2025/26", actual: summary.Year);
        Assert.Equal(expected: "Unmapped group", actual: Assert.Single(Assert.Single(summary.Courses).Groups));
        Assert.Equal(
            expected: new[]
            {
                "/studregistry/LessonAttendance",
                "/studregistry/LessonAttendance/groups/1",
            },
            actual: queries);
    }

    private sealed class CaptureProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return respond(request, token);
        }
    }

    private sealed class CaptureLogger : ILogger<RegistryErrorLogger>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Messages.Add(state.ToString()!);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}
