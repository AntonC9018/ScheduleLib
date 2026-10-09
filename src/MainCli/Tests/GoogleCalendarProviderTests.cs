using System.Net;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Event = Google.Apis.Calendar.v3.Data.Event;

using Xunit;

public sealed class GoogleCalendarProviderTests
{
    [Theory]
    [InlineData("calendar", HttpStatusCode.Unauthorized)]
    [InlineData("calendar", HttpStatusCode.ServiceUnavailable)]
    [InlineData("calendar", HttpStatusCode.TemporaryRedirect)]
    [InlineData("event", HttpStatusCode.Unauthorized)]
    [InlineData("event", HttpStatusCode.ServiceUnavailable)]
    [InlineData("event", HttpStatusCode.TemporaryRedirect)]
    [InlineData("delete", HttpStatusCode.Unauthorized)]
    [InlineData("delete", HttpStatusCode.ServiceUnavailable)]
    [InlineData("delete", HttpStatusCode.TemporaryRedirect)]
    public async Task SdkMutationsNeverReplayOrFollowRedirects(string action, HttpStatusCode status)
    {
        using var transport = new Transport((request, _) =>
        {
            // A replay or response-triggered refresh would succeed, concealing the first failure.
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return Task.FromResult(Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}"));
            var response = Error(status);
            response.Headers.Location = new Uri("https://www.googleapis.com/redirected");
            return Task.FromResult(response);
        });
        using var flow = Flow(transport);
        var credential = new UserCredential(flow, "fixture", Authorization());
        using var service = Service(transport, credential);
        using var provider = new GoogleCalendarSyncProvider(service);
        await Assert.ThrowsAsync<Google.GoogleApiException>(async () =>
        {
            if (action == "calendar") await provider.CreateCalendar("Lessons", default);
            else if (action == "event") await provider.CreateEvent("calendar-id", new(), default);
            else await provider.DeleteCalendar("calendar-id", default);
        });
        var sent = Assert.Single(transport.Requests);
        Assert.Equal("www.googleapis.com", sent.Host);
        Assert.Equal(action == "delete" ? "DELETE" : "POST", sent.Method);
        Assert.DoesNotContain("redirected", sent.Path);
    }

    [Fact]
    public async Task ProvisionedCredentialRefreshesBeforeSendAndPersistsAuthorization()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var transport = new Transport((request, _) => Task.FromResult(Json(request.RequestUri!.Host == "oauth2.googleapis.com"
            ? "{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}" : "{\"id\":\"created\"}")));
        using var tokens = new SdkTokens(transport);
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new BuiltGoogleCredentialsConfig
        {
            CredentialsPath = "unused",
            ApiKeysSource = new ManualGoogleApiKeysSource { Value = new() { ClientId = "fixture", ClientSecret = "fixture" } },
        };
        try
        {
            // Provision fixture state only; no browser or live authorization.
            await auth.Login(config, "fixture", services, default);
            var credential = await auth.Resolve(config, [CalendarService.Scope.Calendar], "fixture", services, default);
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = Service(transport, credential);
            using var provider = new GoogleCalendarSyncProvider(service);
            Assert.Equal("created", await provider.CreateCalendar("Lessons", default));
            Assert.Equal(new[] { "oauth2.googleapis.com", "www.googleapis.com" }, transport.Requests.Select(x => x.Host));
            var second = await auth.Resolve(config, [CalendarService.Scope.Calendar], "fixture", services, default);
            Assert.Equal("fresh", second.Token.AccessToken);
            Assert.Equal("fixture", second.Token.RefreshToken);
            Assert.Contains(CalendarService.Scope.Calendar, second.Token.Scope);
            Assert.Equal(2, transport.Requests.Count);
        }
        finally { Directory.Delete(directory, true); }
    }

    private static GoogleAuthorizationCodeFlow Flow(Transport transport) => new(new GoogleAuthorizationCodeFlow.Initializer
    { ClientSecrets = new ClientSecrets { ClientId = "fixture", ClientSecret = "fixture" }, HttpClientFactory = new Factory(transport) });
    private static TokenResponse Authorization() => new()
    { AccessToken = "fixture", RefreshToken = "fixture", ExpiresInSeconds = 3600, IssuedUtc = DateTime.UtcNow, Scope = string.Join(' ', GoogleAuthentication.LoginScopes) };
    private static CalendarService Service(Transport transport, UserCredential credential) => new(new BaseClientService.Initializer
    { HttpClientFactory = new Factory(transport), HttpClientInitializer = credential, ApplicationName = "fixture" });
    private sealed class SdkTokens(Transport transport, string? refreshToken = "fixture") : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];
        public Task<TokenResponse> Consent(ClientSecrets secrets, string teacher, string[] scopes, CancellationToken token)
        {
            var authorization = Authorization();
            authorization.RefreshToken = refreshToken;
            return Task.FromResult(authorization);
        }
        public Task<TokenResponse?> Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token)
            => throw new Xunit.Sdk.XunitException("Refresh must happen in the production SDK after resolution.");
        public UserCredential CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, Google.Apis.Util.Store.IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(new()
            { ClientSecrets = secrets, Scopes = scopes, DataStore = store, HttpClientFactory = new Factory(transport) });
            _flows.Add(flow);
            return new(flow, teacher, authorization);
        }
        public void Dispose() { foreach (var flow in _flows) flow.Dispose(); }
    }

    [Theory]
    [InlineData("create-503", 6, 1, 0, 2, "uncertain")]
    [InlineData("create-401-refresh", 4, 1, 0, 2, "failed")]
    [InlineData("read-401-refresh-failed", 4, 0, 0, 1, null)]
    [InlineData("read-401-no-refresh", 4, 0, 0, 1, null)]
    [InlineData("create-401-refresh-failed", 4, 1, 0, 2, "failed")]
    [InlineData("create-expired-invalid-grant", 4, 0, 1, 2, "failed")]
    [InlineData("create-expired-no-refresh", 4, 0, 0, 2, "failed")]
    [InlineData("event-expired-invalid-grant", 6, 1, 1, 2, "failed")]
    [InlineData("event-expired-no-refresh", 6, 1, 0, 2, "failed")]
    [InlineData("read-expired-invalid-grant", 4, 0, 1, 0, null)]
    [InlineData("read-expired-no-refresh", 4, 0, 0, 0, null)]
    [InlineData("read-timeout", 5, 0, 0, 1, null)]
    [InlineData("create-timeout", 6, 1, 0, 2, "uncertain")]
    [InlineData("read-connection", 5, 0, 0, 1, null)]
    [InlineData("read-cancel", 130, 0, 0, 1, null)]
    [InlineData("create-cancel", 130, 1, 0, 2, "uncertain")]
    [InlineData("create-missing-id", 6, 1, 0, 2, "uncertain")]
    [InlineData("create-blank-id", 6, 1, 0, 2, "uncertain")]
    [InlineData("preview-pagination", 0, 0, 0, 5, null)]
    [InlineData("create-401-no-refresh", 4, 1, 0, 2, "failed")]
    [InlineData("event-401", 6, 2, 0, 2, "failed")]
    [InlineData("event-timeout", 6, 2, 0, 2, "uncertain")]
    [InlineData("event-second-cancel", 130, 3, 0, 2, "uncertain")]
    [InlineData("event-503", 6, 2, 0, 2, "uncertain")]
    [InlineData("create-timeout-after-delete", 6, 2, 0, 4, "uncertain")]
    [InlineData("event-missing-id", 6, 2, 0, 2, "uncertain")]
    [InlineData("event-blank-id", 6, 2, 0, 2, "uncertain")]
    public async Task ProductionTransportHasCorrectCliOutcomes(string scenario, int expectedExit, int expectedMutations, int expectedRefreshes, int expectedReads, string? expectedState)
    {
        UserCredential? credential = null;
        using var cancel = new CancellationTokenSource();
        var createAttempts = 0;
        using var transport = new Transport((request, _) =>
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return Task.FromResult(scenario == "create-401-refresh" ? Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
                    : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"synthetic-private-detail\"}", Encoding.UTF8, "application/json") });
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Post) createAttempts++;
            if (scenario == "read-timeout" || scenario.StartsWith("create-timeout", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
                throw new TaskCanceledException("synthetic-timeout", new TimeoutException());
            if (scenario == "event-timeout" && createAttempts == 2 && request.Method == HttpMethod.Post)
                throw new TaskCanceledException("synthetic-timeout", new TimeoutException());
            if (scenario == "read-connection") throw new HttpRequestException("synthetic-connection-failure");
            if (scenario == "read-cancel" || scenario == "create-cancel" && request.Method == HttpMethod.Post
                || scenario == "event-second-cancel" && createAttempts == 3 && request.Method == HttpMethod.Post)
            { cancel.Cancel(); throw new OperationCanceledException(cancel.Token); }
            if (scenario.StartsWith("read-401", StringComparison.Ordinal) || scenario.StartsWith("create-401", StringComparison.Ordinal) && request.Method == HttpMethod.Post && createAttempts == 1)
                return Task.FromResult(Error(HttpStatusCode.Unauthorized));
            if (scenario == "event-401" && createAttempts == 2 && request.Method == HttpMethod.Post)
                return Task.FromResult(Error(HttpStatusCode.Unauthorized));
            if ((scenario == "create-503" && createAttempts == 1 || scenario == "event-503" && createAttempts == 2) && request.Method == HttpMethod.Post)
                return Task.FromResult(Error(HttpStatusCode.ServiceUnavailable));
            if (path.EndsWith("/calendars/primary", StringComparison.Ordinal)) return Task.FromResult(Json("{\"id\":\"actual@example.test\"}"));
            if (request.Method == HttpMethod.Get && path.EndsWith("/calendarList", StringComparison.Ordinal))
            {
                if (scenario is "create-expired-invalid-grant" or "create-expired-no-refresh" or "read-expired-invalid-grant" or "read-expired-no-refresh") credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                if (scenario == "preview-pagination") return Task.FromResult(Json(request.RequestUri.Query.Contains("pageToken") ? "{\"items\":[{\"id\":\"old\",\"summary\":\"lessons\"}]}" : "{\"items\":[],\"nextPageToken\":\"next\"}"));
                return Task.FromResult(Json(scenario == "create-timeout-after-delete" ? "{\"items\":[{\"id\":\"old\",\"summary\":\"lessons\"}]}" : "{\"items\":[]}"));
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/events", StringComparison.Ordinal))
                return Task.FromResult(Json(request.RequestUri.Query.Contains("pageToken") ? "{\"items\":[{\"id\":\"event2\",\"summary\":\"Second\"}]}" : "{\"items\":[{\"id\":\"event1\",\"summary\":\"First\"}],\"nextPageToken\":\"next\"}"));
            if (request.Method == HttpMethod.Post && path.EndsWith("/calendars", StringComparison.Ordinal))
            {
                if (scenario is "event-expired-invalid-grant" or "event-expired-no-refresh") credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                return Task.FromResult(Json(scenario == "create-missing-id" ? "{}" : scenario == "create-blank-id" ? "{\"id\":\" \"}" : "{\"id\":\"new-calendar\"}"));
            }
            if (request.Method == HttpMethod.Post) return Task.FromResult(Json(scenario == "event-missing-id" ? "{}" : scenario == "event-blank-id" ? "{\"id\":\" \"}" : "{\"id\":\"new-event\"}"));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var tokens = new SdkTokens(transport, scenario.EndsWith("no-refresh", StringComparison.Ordinal) ? null : "fixture");
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new BuiltGoogleCredentialsConfig
        {
            CredentialsPath = "unused",
            ApiKeysSource = new ManualGoogleApiKeysSource { Value = new() { ClientId = "fixture", ClientSecret = "fixture" } },
        };
        try
        {
            await auth.Login(config, "fixture", services, default);
            credential = await auth.Resolve(config, [CalendarService.Scope.Calendar], "fixture", services, default);
            if (scenario.StartsWith("read-expired", StringComparison.Ordinal)) credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = new CalendarService(new BaseClientService.Initializer
            { HttpClientFactory = new Factory(transport), HttpClientInitializer = credential, ApplicationName = "fixture" });
            using var provider = new GoogleCalendarSyncProvider(service);
            var beforeOut = Console.Out;
            var beforeError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exit;
            try
            {
                Console.SetOut(stdout); Console.SetError(stderr);
                exit = await new FixtureCalendar(provider).Sync(
                    new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
                    new() { Profile = "Nartea Nichita" }, new() { Json = true },
                    new() { Apply = !scenario.StartsWith("read-", StringComparison.Ordinal) && scenario != "preview-pagination" }, cancel.Token);
            }
            finally { Console.SetOut(beforeOut); Console.SetError(beforeError); }
            using var json = JsonDocument.Parse(stdout.ToString());
            var envelope = json.RootElement;
            var data = envelope.GetProperty("data");
            Assert.Equal(expectedExit, exit);
            Assert.Equal(expectedExit == 130 ? "cancelled" : expectedExit == 0 ? "preview" : expectedExit == 6 ? "partial" : "failed", envelope.GetProperty("status").GetString());
            var mutations = transport.Requests.Where(x => x.Host != "oauth2.googleapis.com" && x.Method != "GET").ToArray();
            Assert.Equal(expectedMutations, mutations.Length);
            Assert.Equal(expectedRefreshes, transport.Requests.Count(x => x.Host == "oauth2.googleapis.com"));
            Assert.Equal(expectedReads, transport.Requests.Count(x => x.Host != "oauth2.googleapis.com" && x.Method == "GET"));
            if (expectedState is not null)
            {
                var outcomes = data.GetProperty("outcomes").EnumerateArray().ToArray();
                Assert.Equal(expectedMutations == 0 ? 1 : expectedMutations + (scenario.Contains("expired", StringComparison.Ordinal) ? 1 : 0), outcomes.Length);
                Assert.Equal(expectedState, outcomes.Last().GetProperty("state").GetString());
                Assert.All(outcomes.SkipLast(1), x => Assert.Equal("completed", x.GetProperty("state").GetString()));
                if (outcomes.Last().GetProperty("action").GetString() == "create event")
                {
                    Assert.Equal("new-calendar", data.GetProperty("createdCalendarId").GetString());
                    Assert.Equal(scenario == "event-second-cancel" ? 1 : 0, outcomes.Last().GetProperty("desiredEventIndex").GetInt32());
                    if (scenario == "event-second-cancel") Assert.Equal("new-event", outcomes[1].GetProperty("eventId").GetString());
                }
            }
            if (expectedExit == 4 || scenario.Contains("expired", StringComparison.Ordinal) || scenario == "event-401") Assert.Contains("auth login google", stderr.ToString());
            Assert.DoesNotContain("synthetic-private-detail", stdout.ToString() + stderr);
            if (scenario == "preview-pagination")
            {
                Assert.Equal(2, data.GetProperty("deletedEvents").GetArrayLength());
                Assert.Empty(data.GetProperty("outcomes").EnumerateArray());
            }
            Assert.Equal(1, service.HttpClient.MessageHandler.NumTries);
            Assert.False(service.HttpClient.MessageHandler.FollowRedirect);
        }
        finally { Directory.Delete(directory, true); }
    }
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Error(HttpStatusCode status) => new(status) { Content = new StringContent("{\"error\":{\"code\":" + (int)status + ",\"message\":\"synthetic-rejection\"}}", Encoding.UTF8, "application/json") };
    private sealed class FixtureCalendar(ICalendarSyncProvider provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
        protected override Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token) => Task.FromResult(provider);
    }
    private sealed class Factory(Transport transport) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => transport;
    }
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Method, string Host, string Path)> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { Requests.Add((request.Method.Method, request.RequestUri!.Host, request.RequestUri.AbsolutePath)); return respond(request, token); }
    }
}
