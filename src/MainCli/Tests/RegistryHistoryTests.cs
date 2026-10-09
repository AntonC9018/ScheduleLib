using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using Xunit;

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
    public void YearNavigationUsesObservedGetFormAndEncodedValue()
    {
        var years = RegistryHistoryHtml.Years(new HtmlParser().ParseDocument(Years), Index);
        Assert.Equal(new[] { "2025/26", "2024/25" }, years.Select(x => x.Value));
        Assert.True(years[0].Selected);
        Assert.Equal("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25", years[1].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("<form method='post'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>")]
    [InlineData("<form action='https://other.test/'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>")]
    [InlineData("<form><input name='token' value='private'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>")]
    [InlineData("<select id='AnUniv' name='AnUniv'><option>2024/25</option></select>")]
    [InlineData("<html>no year selector</html>")]
    public void UnsupportedYearMarkupFailsExplicitly(string html) =>
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Years(new HtmlParser().ParseDocument(html), Index));

    [Theory]
    [InlineData("<form name='lesson'></form>")]
    [InlineData("<form name='lesson'><div class='row'></div><div class='row'><div class='col'><a href='/studregistry/LessonAttendance/LessonsList/1'>changed layout</a></div></div></form>")]
    public void ChangedGroupContainerDoesNotSilentlyProduceAnEmptySummary(string html) =>
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Groups(new HtmlParser().ParseDocument(html), Index));

    [Fact]
    public void ChangedCourseContainerDoesNotSilentlyProduceAnEmptySummary() =>
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Courses(new HtmlParser().ParseDocument("""
            <div id='nav-1'><a href='/studregistry/LessonAttendance/groups/1'>changed layout</a></div><div id='nav-2'></div>
            """), Index));

    [Fact]
    public void RawHistoricalNamesSurviveWithoutScheduleMatchingAndUnsafeLinksFail()
    {
        var document = new HtmlParser().ParseDocument("""
            <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'> Old &amp; unmapped, </a></span></div></div>
            <div id='nav-2'><div><span>2</span><span><a href='/studregistry/LessonAttendance/groups/2'>Other,</a></span></div></div>
            """);
        var courses = RegistryHistoryHtml.Courses(document, Index);
        Assert.Equal(new[] { "Old & unmapped,", "Other," }, courses.Select(x => x.Name));
        Assert.Equal(new[] { 1, 2 }, courses.Select(x => x.Semester));
        var groups = RegistryHistoryHtml.Groups(new HtmlParser().ParseDocument("""
            <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
            <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Repeat / unknown notation</a>
            <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a>
            </div></div></div></form>
            """), Index);
        Assert.Equal("Repeat / unknown notation", Assert.Single(groups).Name);
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Courses(
            new HtmlParser().ParseDocument(document.DocumentElement.OuterHtml.Replace("/studregistry/LessonAttendance/groups/1", "https://other.test/studregistry/LessonAttendance/groups/1")), Index));
    }

    [Theory]
    [InlineData(401)] [InlineData(403)] [InlineData(429)] [InlineData(302)]
    public async Task RejectionStopsWithoutReplayOrFollowingRedirect(int status)
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new(Index, "../Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.DoesNotContain("registry.test", error.Message);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task ReadsRetryAtMostOnceAndMutationNeverReachesTransport()
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new(Index, "../Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(2, inner.Calls);
        using var content = new StringContent("canary-password");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task FreshHistorySessionBlocksMutationAndLessonDataRoutes()
    {
        using var inner = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new("https://registry.test/studregistry/Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        using var content = new StringContent("synthetic");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.DeleteAsync(new Uri(Index.AbsoluteUri + "/groups/1")));
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(new Uri(Index.AbsoluteUri + "/LessonsList/1")));
        Assert.Equal(0, inner.Calls);
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
        using var guardA = new RegistryHistoryRequestHandler(innerA, Index, new(Index, "../Account/Login"), TimeSpan.FromMilliseconds(30));
        using var guardB = new RegistryHistoryRequestHandler(innerB, Index, new(Index, "../Account/Login"), TimeSpan.FromMilliseconds(30));
        using var first = new HttpClient(guardA);
        using var second = new HttpClient(guardB);
        // Intentionally race only synthetic transports to prove the shared gate.
        var a = first.GetAsync(Index);
        var b = second.GetAsync(Index);
        using var responseA = await a;
        using var responseB = await b;
        Assert.Equal(1, maximum);
        Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public void ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext()
    {
        var log = new CaptureLogger();
        var logger = new RegistryErrorLogger(log);
        logger.StudentsNotInDbButInRegistry(new(PrivateStudents(), null!, default, default));
        logger.LessonsDecidedErroneous(new("canary-student canary-password canary-token"));
        logger.GroupParsingError(new(new GroupParsingException("canary-password canary-student"), "synthetic-group"));
        Assert.NotEmpty(log.Messages);
        Assert.DoesNotContain("canary", string.Join("\n", log.Messages));

        static IEnumerable<ScheduleLib.Parsing.Name> PrivateStudents() => new[] { "canary-student" }.Select<string, ScheduleLib.Parsing.Name>(_ => throw new Exception("Private students must not be enumerated by logging."));
    }

    [Fact]
    public void DuplicateStudentDiagnosticDoesNotIncludeTheirName()
    {
        var builder = new StudentAttendanceListBuilder();
        var name = ScheduleLib.Parsing.NameHelper.Parse("Canary Student");
        builder.Student(name);
        var error = Assert.Throws<InvalidOperationException>(() => builder.Student(name));
        Assert.DoesNotContain("Canary", error.Message);
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
            requests.Add(request.Method + " " + request.RequestUri!.PathAndQuery);
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Assert.Contains("canary-password", body);
                return new(HttpStatusCode.Redirect);
            }
            var historical = request.RequestUri.Query.Contains("2024%2F25", StringComparison.Ordinal);
            var years = historical ? Years.Replace("selected value='2025/26'", "value='2025/26'").Replace("value='2024/25'>2024/25", "selected value='2024/25'>2024/25") : Years;
            var html = request.RequestUri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal)
                ? """
                    <a href='/studregistry/Account/Logout'>Exit</a>
                    <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                    <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Unknown historical group</a>
                    <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a></div></div></div></form>
                    <table><tr><td>canary-student</td><td>private grade</td></tr></table>
                    """
                : years + """
                    <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped historical course,</a></span></div></div><div id='nav-2'></div>
                    """;
            return new(HttpStatusCode.OK) { Content = new StringContent(html) };
        });
        using var guard = new RegistryHistoryRequestHandler(inner, Index, login, TimeSpan.FromMilliseconds(1));
        using var client = new HttpClient(guard);
        using var transport = new HttpClientContext(new AngleSharp.Io.MemoryCookieProvider(), client, guard);
        var builder = new ScrapingContextBuilder();
        builder.AddLogging(logs);
        builder.TokenAuth(new TokenNamesConfig { BaseUrl = new("https://registry.test/studregistry/"), LoginUrl = login, TokenCookieName = "synthetic" },
            x => x.PasswordLoginCall(new() { Login = "canary-login", Password = "canary-password" }, new() { Login = "UserLogin", Password = "UserPassword" }));
        using var context = await builder.Build(default, transport);
        guard.CompleteAuthentication();
        var summary = await new RegistryHistoryReader(context.HttpClient, Index).Discover("2024/25", default);
        Assert.Equal("2024/25", Assert.Single(summary).Year);
        Assert.Equal("Unknown historical group", Assert.Single(Assert.Single(summary[0].Courses).Groups));
        Assert.Equal(new[] { "POST /studregistry/Account/Login", "GET /studregistry/LessonAttendance", "GET /studregistry/LessonAttendance?AnUniv=2024%2F25", "GET /studregistry/LessonAttendance/groups/1" }, requests);
        Assert.DoesNotContain("canary", string.Join("\n", log.Messages));
        Assert.DoesNotContain("canary-student", System.Text.Json.JsonSerializer.Serialize(summary));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task LoginPageOrIgnoredYearStopsBeforeGroupReads(bool loginPage)
    {
        using var inner = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(loginPage ? "<input type='password' value='canary-password'>" : Years + "<div id='nav-1'></div><div id='nav-2'></div>")
        }));
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new("https://registry.test/studregistry/Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => new RegistryHistoryReader(client, Index).Discover("2024/25", default));
        Assert.DoesNotContain("canary", error.Message);
        Assert.Equal(loginPage ? 1 : 2, inner.Calls);
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
                : Years + """
                    <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped course</a></span></div></div><div id='nav-2'></div>
                    """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        });
        using var guard = new RegistryHistoryRequestHandler(inner, Index, new("https://registry.test/studregistry/Account/Login"), TimeSpan.FromMilliseconds(1));
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var summary = Assert.Single(await new RegistryHistoryReader(client, Index).Discover("current", default));
        Assert.Equal("2025/26", summary.Year);
        Assert.Equal("Unmapped group", Assert.Single(Assert.Single(summary.Courses).Groups));
        Assert.Equal(new[] { "/studregistry/LessonAttendance", "/studregistry/LessonAttendance/groups/1" }, queries);
    }

    private sealed class CaptureProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;
        public void Dispose() { }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Calls++; return respond(request, token); }
    }
    private sealed class CaptureLogger : ILogger<RegistryErrorLogger>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull { Messages.Add(state.ToString()!); return null; }
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception) + exception);
    }
}
