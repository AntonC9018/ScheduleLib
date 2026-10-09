using System.Net;
using System.Text;
using Google.Apis.Drive.v3;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using Xunit;

public sealed class GoogleDriveProviderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AmbiguousFolderStopsPreviewAndApplyBeforeListingOrMutation(bool apply, bool splitPages)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var call = 0;
        using var handler = new Transport((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri!.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"user\":{\"permissionId\":\"account\"}}"));
            Assert.Contains("mimeType", Uri.UnescapeDataString(request.RequestUri.Query));
            call++;
            return Task.FromResult(Json(!splitPages
                ? "{\"files\":[{\"id\":\"one\",\"name\":\"Configured\"},{\"id\":\"two\",\"name\":\"Configured\"}]}"
                : call == 1 ? "{\"nextPageToken\":\"next\",\"files\":[{\"id\":\"one\",\"name\":\"Configured\"}]}"
                : "{\"files\":[{\"id\":\"two\",\"name\":\"Configured\"}]}"));
        });
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => DriveSync.Run(provider, "Configured", [], apply, default, directory));
            Assert.Equal(splitPages ? 2 : 1, call);
            if (splitPages) Assert.Contains("pageToken=next", handler.Requests.Last().Uri);
            Assert.All(handler.Requests, x => Assert.Equal("GET", x.Method));
            Assert.Empty(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderLookupScansRemainingPagesForSingleOrMissingMatch(bool found)
    {
        var call = 0;
        using var handler = new Transport((_, _) => Task.FromResult(Json(++call == 1
            ? "{\"nextPageToken\":\"next\",\"files\":[]}"
            : found ? "{\"files\":[{\"id\":\"only\",\"name\":\"Configured\"}]}" : "{\"files\":[]}")));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        if (found) Assert.Equal("only", (await provider.FindFolder("Configured", default)).Id);
        else await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.FindFolder("Configured", default));
        Assert.Equal(2, call);
        Assert.Contains("pageToken=next", handler.Requests.Last().Uri);
    }

    [Theory]
    [InlineData("create", HttpStatusCode.ServiceUnavailable)]
    [InlineData("update", HttpStatusCode.BadRequest)]
    [InlineData("delete", HttpStatusCode.ServiceUnavailable)]
    [InlineData("create", HttpStatusCode.Unauthorized)]
    public async Task ProductionMutationsSendExactlyOnceAndSurfaceHttpFailure(string action, HttpStatusCode status)
    {
        using var handler = new Transport((request, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = new StringContent("{\"error\":{\"code\":" + (int)status + ",\"message\":\"Rejected\"}}", Encoding.UTF8, "application/json") }));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("artifact bytes"));
        await Assert.ThrowsAsync<Google.GoogleApiException>(async () =>
        {
            if (action == "delete") await provider.Delete("remote", default);
            else if (action == "create") await provider.Create("folder", "schedule.pdf", input, default);
            else await provider.Update("remote", input, default);
        });
        Assert.Single(handler.Requests);
        Assert.Equal(action == "create" ? "POST" : action == "update" ? "PATCH" : "DELETE", handler.Requests[0].Method);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCreateLostOrCancelledResponseHasNoReplay(bool cancel)
    {
        using var handler = new Transport((_, _) => cancel ? throw new OperationCanceledException() : throw new HttpRequestException("Response lost"));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream([1, 2, 3]);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.Create("folder", "schedule.pdf", input, default));
        else await Assert.ThrowsAnyAsync<HttpRequestException>(() => provider.Create("folder", "schedule.pdf", input, default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProductionMultipartUploadsIncludeMetadataAndBytesAndRequireReturnedId()
    {
        using var handler = new Transport((_, _) => Task.FromResult(Json("{\"id\":\"created-id\"}")));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("fixture artifact bytes"));
        Assert.Equal("created-id", await provider.Create("folder-id", "schedule.pdf", input, default));
        var sent = Assert.Single(handler.Requests);
        Assert.Contains("uploadType=multipart", sent.Uri);
        Assert.Contains("\"name\":\"schedule.pdf\"", sent.Body);
        Assert.Contains("\"parents\":[\"folder-id\"]", sent.Body);
        Assert.Contains("fixture artifact bytes", sent.Body);
        using var missingHandler = new Transport((_, _) => Task.FromResult(Json("{}")));
        using var missingService = Service(missingHandler);
        using var missing = new GoogleDriveSyncProvider(missingService);
        using var missingInput = new MemoryStream([1]);
        await Assert.ThrowsAsync<IOException>(() => missing.Create("folder", "name", missingInput, default));
    }

    [Fact]
    public async Task ProductionReadsActualIdentityEscapesFolderNamesAndFollowsFilePagination()
    {
        var call = 0;
        using var handler = new Transport((_, _) => Task.FromResult(Json(++call switch
        {
            1 => "{\"user\":{\"permissionId\":\"account-id\",\"emailAddress\":\"actual@example.test\"}}",
            2 => "{\"files\":[{\"id\":\"folder-id\",\"name\":\"Actual folder\"}]}",
            3 => "{\"nextPageToken\":\"next\",\"files\":[{\"id\":\"one\",\"name\":\"a.pdf\"}]}",
            _ => "{\"files\":[{\"id\":\"two\",\"name\":\"b.pdf\"}]}"
        })));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        var account = await provider.GetAccount(default);
        Assert.Equal("account-id", account.Id);
        Assert.Equal("actual@example.test", account.Email);
        Assert.Equal("folder-id", (await provider.FindFolder("O'Brien", default)).Id);
        Assert.Equal(2, (await provider.ListFiles("folder-id", default)).Count);
        Assert.Contains("O\\'Brien", Uri.UnescapeDataString(handler.Requests[1].Uri));
        Assert.Contains("pageToken=next", handler.Requests[3].Uri);
        Assert.All(handler.Requests, x => Assert.Equal("GET", x.Method));
    }

    [Theory]
    [InlineData("read-sent-real-deadline", 5, "failed", 1, 0)]
    [InlineData("write-sent-real-deadline", 6, "partial", 5, 0)]
    [InlineData("expired-refresh-real-deadline", 5, "failed", 0, 1)]
    [InlineData("apply-expired-refresh-real-deadline", 5, "failed", 4, 1)]
    [InlineData("apply-after-create-expired-refresh-real-deadline", 6, "partial", 5, 1)]
    [InlineData("expired-refresh-real-cancel", 130, "cancelled", 0, -1)]
    [InlineData("apply-expired-refresh-real-cancel", 130, "cancelled", 4, -1)]
    [InlineData("apply-after-create-expired-refresh-real-cancel", 130, "cancelled", 5, -1)]
    [InlineData("expired-refresh-timeout", 5, "failed", 0, 3)]
    [InlineData("expired-refresh-connection", 5, "failed", 0, 1)]
    [InlineData("expired-refresh-cancel", 130, "cancelled", 0, -1)]
    [InlineData("apply-expired-refresh-timeout", 5, "failed", 4, 3)]
    [InlineData("apply-expired-refresh-connection", 5, "failed", 4, 1)]
    [InlineData("apply-expired-refresh-cancel", 130, "cancelled", 4, -1)]
    [InlineData("apply-after-create-expired-refresh-timeout", 6, "partial", 5, 3)]
    [InlineData("apply-after-create-expired-refresh-connection", 6, "partial", 5, 1)]
    [InlineData("apply-after-create-expired-refresh-cancel", 130, "cancelled", 5, -1)]
    [InlineData("preview-401", 4, "failed", 1, 0)]
    [InlineData("apply-401", 4, "failed", 5, 0)]
    [InlineData("expired", 4, "failed", 0, 1)]
    [InlineData("apply-expired", 4, "failed", 4, 1)]
    [InlineData("refresh-success", 0, "applied", 1, 1)]
    [InlineData("read-cancel", 130, "cancelled", 1, 0)]
    [InlineData("write-cancel", 130, "cancelled", 5, 0)]
    [InlineData("read-timeout", 5, "failed", 1, 0)]
    [InlineData("write-timeout", 6, "partial", 5, 0)]
    [InlineData("connection", 5, "failed", 1, 0)]
    public async Task ProductionHttpFailuresHaveCorrectCliResults(string scenario, int exit, string status, int driveRequests, int tokenRequests)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        UserCredential? credential = null;
        using var handler = new Transport(async (request, requestToken) =>
        {
            // The API HttpClient cancels the interceptor's linked token when its deadline
            // expires. Wait on that real token inside OAuth; do not synthesize an exception.
            if (request.RequestUri!.Host == "oauth2.googleapis.com" &&
                (scenario.EndsWith("real-deadline", StringComparison.Ordinal) || scenario.EndsWith("real-cancel", StringComparison.Ordinal)))
            {
                if (scenario.EndsWith("real-cancel", StringComparison.Ordinal)) cancellation.Cancel();
                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("Refresh must be interrupted before the API request is sent.");
            }
            if (request.RequestUri!.Host == "oauth2.googleapis.com" && scenario.Contains("expired-refresh", StringComparison.Ordinal))
            {
                if (scenario.EndsWith("connection", StringComparison.Ordinal)) throw new HttpRequestException("private-provider-diagnostic");
                if (scenario.EndsWith("cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                throw new TaskCanceledException("private-provider-diagnostic", new TimeoutException());
            }
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
                return scenario == "refresh-success" ? Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}")
                    : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"private-provider-diagnostic\"}", Encoding.UTF8, "application/json") };
            if (scenario == "read-sent-real-deadline" || scenario == "write-sent-real-deadline" && request.Method == HttpMethod.Post)
            {
                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("The sent API request must time out.");
            }
            if (scenario == "read-cancel" || scenario == "write-cancel" && request.Method == HttpMethod.Post)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            if (scenario == "read-timeout" || scenario == "write-timeout" && request.Method == HttpMethod.Post)
                throw new TaskCanceledException("private-provider-diagnostic", new TimeoutException());
            if (scenario == "connection") throw new HttpRequestException("private-provider-diagnostic");
            if (scenario == "preview-401" || scenario == "apply-401" && request.Method == HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"error\":{\"code\":401,\"message\":\"rejected\"}}", Encoding.UTF8, "application/json") };
            if (request.RequestUri.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
                return Json("{\"user\":{\"permissionId\":\"account\"}}");
            if (request.Method == HttpMethod.Post)
            {
                if (scenario.StartsWith("apply-after-create-expired", StringComparison.Ordinal)) credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                return Json("{\"id\":\"created\"}");
            }
            var folderQuery = Uri.UnescapeDataString(request.RequestUri.Query).Contains("mimeType", StringComparison.Ordinal);
            if (!folderQuery && scenario.StartsWith("apply-expired", StringComparison.Ordinal)) credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            // One create then stop on failures; success uses an empty remote/local plan.
            return Json(folderQuery ? "{\"files\":[{\"id\":\"folder\",\"name\":\"Configured\"}]}" : "{\"files\":[]}");
        });
        using var flow = new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        { ClientSecrets = new ClientSecrets { ClientId = "fixture", ClientSecret = "fixture" }, HttpClientFactory = new Factory(handler) });
        credential = new UserCredential(flow, "fixture", new TokenResponse
        { AccessToken = "fixture", RefreshToken = "fixture", ExpiresInSeconds = 3600, IssuedUtc = scenario.StartsWith("expired", StringComparison.Ordinal) ? DateTime.UtcNow.AddHours(-2) : DateTime.UtcNow });
        using var service = new DriveService(new BaseClientService.Initializer
        { HttpClientFactory = new Factory(handler), HttpClientInitializer = credential, ApplicationName = "fixture", DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None });
        if (scenario.EndsWith("real-deadline", StringComparison.Ordinal)) service.HttpClient.Timeout = TimeSpan.FromMilliseconds(200);
        using var provider = new GoogleDriveSyncProvider(service);
        // Successful refresh and mutation are also exercised directly without generating a second bundle.
        if (scenario == "refresh-success")
        {
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var input = new MemoryStream([1]);
            Assert.Equal("created", await provider.Create("folder", "a.pdf", input, default));
            Assert.Equal(driveRequests, handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com"));
            Assert.Equal(tokenRequests, handler.Requests.Count(x => new Uri(x.Uri).Host == "oauth2.googleapis.com"));
            Directory.Delete(directory, true);
            return;
        }
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var actual = await new DriveSyncTests.FixtureDrive(provider).Publish(
                new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Profile = "Curmanschii Anton" }, new() { Directory = directory }, new() { Json = true },
                new() { Apply = scenario.StartsWith("apply", StringComparison.Ordinal) || scenario.StartsWith("write-", StringComparison.Ordinal) }, cancellation.Token);
            Assert.Equal(exit, actual);
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(status, json.RootElement.GetProperty("status").GetString());
            if (scenario.StartsWith("apply", StringComparison.Ordinal) || scenario.StartsWith("write-", StringComparison.Ordinal))
            {
                var outcomes = json.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
                var completed = scenario.StartsWith("apply-after-create-expired", StringComparison.Ordinal) ? 1 : 0;
                Assert.All(outcomes.Take(completed), x => Assert.Equal("completed", x.GetProperty("state").GetString()));
                Assert.Equal(scenario.StartsWith("write-", StringComparison.Ordinal) || (scenario.EndsWith("refresh-cancel", StringComparison.Ordinal) || scenario.EndsWith("real-cancel", StringComparison.Ordinal)) ? "uncertain" : "failed", outcomes[completed].GetProperty("state").GetString());
                Assert.All(outcomes.Skip(completed + 1), x => Assert.Equal("not-attempted", x.GetProperty("state").GetString()));
            }
            if (scenario.Contains("expired-refresh", StringComparison.Ordinal))
            {
                Assert.DoesNotContain("auth login google", stdout.ToString() + stderr);
                Assert.Equal("fixture", credential.Token.RefreshToken);
            }
            if (exit == 4) Assert.Contains("auth login google", stderr.ToString());
            Assert.DoesNotContain("private-provider-diagnostic", stdout.ToString() + stderr);
            Assert.Equal(driveRequests, handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com"));
            if (scenario.Contains("real-", StringComparison.Ordinal))
                Assert.Equal(scenario.StartsWith("apply-after-create", StringComparison.Ordinal) || scenario == "write-sent-real-deadline" ? 1 : 0,
                    handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com" && x.Method != "GET"));
            var refreshes = handler.Requests.Count(x => new Uri(x.Uri).Host == "oauth2.googleapis.com");
            // SDK refresh cancellation races its timeout retry loop; API sends remain exact.
            if (tokenRequests == -1) Assert.InRange(refreshes, 1, 3);
            else Assert.Equal(tokenRequests, refreshes);
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("Unrelated credential invariant.")]
    [InlineData("The access token has expired and could not be refreshed. Errors: timeout, timeout, timeout")]
    public async Task UnrelatedCredentialInvalidOperationIsNotAuthenticationFailure(string message)
    {
        using var handler = new Transport((_, _) => throw new Xunit.Sdk.XunitException("Request must not be sent."));
        using var service = Service(handler);
        service.HttpClient.MessageHandler.Credential = new BrokenCredential(message);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.Create("folder", "a.pdf", input, default));
        Assert.Empty(handler.Requests);
    }

    private sealed class BrokenCredential(string message) : IHttpExecuteInterceptor
    {
        public Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://www.googleapis.com/auth/drive")]
    [InlineData("")]
    public async Task SdkRefreshPersistsOmittedScopesAndHonorsExplicitGrants(string? responseScope)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var handler = new Transport((request, _) => Task.FromResult(Json(
            request.RequestUri!.Host == "oauth2.googleapis.com"
                ? "{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\""
                    + (responseScope is null ? "" : ",\"scope\":\"" + responseScope + "\"") + "}"
                : "{\"id\":\"created\"}")));
        using var tokens = new SdkTokens(handler);
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            await auth.Login(AuthConfig, "fixture", services, default);
            var credential = await auth.Resolve(AuthConfig, GoogleAuthentication.LoginScopes, "fixture", services, default);
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = CredentialService(handler, credential);
            using var provider = new GoogleDriveSyncProvider(service);
            using var input = new MemoryStream([1]);
            Assert.Equal("created", await provider.Create("folder", "a.pdf", input, default));
            var expected = responseScope ?? string.Join(' ', GoogleAuthentication.LoginScopes);
            Assert.Equal(expected, credential.Token.Scope);
            var file = Assert.Single(Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories));
            var persisted = await new AtomicTokenStore(file).GetAsync<TokenResponse>("fixture");
            Assert.Equal(expected, persisted.Scope);
            Assert.Equal("refresh", persisted.RefreshToken);
            if (responseScope is null)
            {
                var second = await auth.Resolve(AuthConfig, GoogleAuthentication.LoginScopes, "fixture", services, default);
                Assert.Equal(expected, second.Token.Scope);
                Assert.Equal("fresh", second.Token.AccessToken);
                using var secondService = CredentialService(handler, second);
                using var secondProvider = new GoogleDriveSyncProvider(secondService);
                using var secondInput = new MemoryStream([2]);
                Assert.Equal("created", await secondProvider.Create("folder", "b.pdf", secondInput, default));
            }
            else
                await Assert.ThrowsAsync<AuthenticationRequiredException>(() => auth.Resolve(
                    AuthConfig, GoogleAuthentication.LoginScopes, "fixture", services, default));
            Assert.Equal(responseScope is null ? 2 : 1, handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com"));
            Assert.Single(handler.Requests.Where(x => new Uri(x.Uri).Host == "oauth2.googleapis.com"));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("planning", 4, 0, 0)]
    [InlineData("first-mutation", 4, 4, 0)]
    [InlineData("after-completed", 6, 5, 1)]
    public async Task ExpiryAfterResolutionWithoutRefreshTokenFailsBeforeSending(string stage, int exit, int readsAndWrites, int writes)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var original = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        UserCredential? credential = null;
        using var handler = new Transport((request, _) =>
        {
            Assert.NotEqual("oauth2.googleapis.com", request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
                return Task.FromResult(Json("{\"user\":{\"permissionId\":\"account\"}}"));
            if (request.Method == HttpMethod.Delete)
            {
                credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            var folderQuery = Uri.UnescapeDataString(request.RequestUri.Query).Contains("mimeType", StringComparison.Ordinal);
            if (!folderQuery && stage == "first-mutation") credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            return Task.FromResult(Json(folderQuery ? "{\"files\":[{\"id\":\"folder\",\"name\":\"Configured\"}]}"
                : stage == "after-completed" ? "{\"files\":[{\"id\":\"obsolete\",\"name\":\"obsolete.pdf\"}]}" : "{\"files\":[]}"));
        });
        using var tokens = new SdkTokens(handler, refreshToken: null);
        using var auth = new GoogleAuthentication(Path.Combine(directory, "auth"), tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            await auth.Login(AuthConfig, "fixture", services, default);
            credential = await auth.Resolve(AuthConfig, GoogleAuthentication.LoginScopes, "fixture", services, default);
            if (stage == "planning") credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = CredentialService(handler, credential);
            using var provider = new GoogleDriveSyncProvider(service);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(exit, await new DriveSyncTests.FixtureDrive(provider).Publish(
                new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Profile = "Curmanschii Anton" }, new() { Directory = Path.Combine(directory, "output") },
                new() { Json = true }, new() { Apply = stage != "planning" }));
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(exit, json.RootElement.GetProperty("exitCode").GetInt32());
            if (stage != "planning")
            {
                var outcomes = json.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
                var failedIndex = stage == "after-completed" ? 1 : 0;
                if (failedIndex == 1) Assert.Equal("completed", outcomes[0].GetProperty("state").GetString());
                Assert.Equal("failed", outcomes[failedIndex].GetProperty("state").GetString());
                Assert.All(outcomes.Skip(failedIndex + 1), x => Assert.Equal("not-attempted", x.GetProperty("state").GetString()));
            }
            if (exit == 4) Assert.Contains("auth login google", stderr.ToString());
            Assert.Equal(readsAndWrites, handler.Requests.Count);
            Assert.Equal(writes, handler.Requests.Count(x => x.Method != "GET"));
            Assert.DoesNotContain("access-secret", stdout.ToString() + stderr);
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
            Directory.Delete(directory, true);
        }
    }

    private static BuiltGoogleCredentialsConfig AuthConfig => new()
    {
        CredentialsPath = "unused",
        ApiKeysSource = new ManualGoogleApiKeysSource { Value = new() { ClientId = "fixture", ClientSecret = "fixture" } },
    };
    private static DriveService CredentialService(Transport handler, UserCredential credential) => new(new BaseClientService.Initializer
    {
        HttpClientFactory = new Factory(handler), HttpClientInitializer = credential,
        ApplicationName = "fixture", DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
    });
    private sealed class SdkTokens(Transport handler, string? refreshToken = "refresh") : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];
        public Task<TokenResponse> Consent(ClientSecrets secrets, string teacher, string[] scopes, CancellationToken token) => Task.FromResult(new TokenResponse
        {
            AccessToken = "access-secret", RefreshToken = refreshToken, Scope = string.Join(' ', scopes),
            IssuedUtc = DateTime.UtcNow, ExpiresInSeconds = 3600,
        });
        public Task<TokenResponse?> Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token)
            => throw new Xunit.Sdk.XunitException("Refresh must use the production SDK after resolution.");
        public UserCredential CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(new()
            {
                ClientSecrets = secrets, Scopes = scopes, DataStore = store, HttpClientFactory = new Factory(handler),
            });
            _flows.Add(flow);
            return new(flow, teacher, authorization);
        }
        public void Dispose() { foreach (var flow in _flows) flow.Dispose(); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    public async Task InvalidOutputPathFailsBeforeScheduleOrAuthentication(string path)
    {
        var original = Console.Out;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Assert.Equal(2, await new UninitializedDrive().Publish(new() { DataDirectory = "missing-source" },
                new(), new() { Directory = path }, new() { Json = true }, new()));
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(2, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Empty(json.RootElement.GetProperty("outputs").EnumerateArray());
        }
        finally { Console.SetOut(original); }
    }

    private sealed class UninitializedDrive : DriveCommands
    {
        protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
            => throw new Xunit.Sdk.XunitException("Output arguments must fail before initialization.");
    }

    private static DriveService Service(Transport handler) => new(new BaseClientService.Initializer
    { HttpClientFactory = new Factory(handler), ApplicationName = "fixture" });
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Factory(Transport handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Method, string Uri, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await respond(request, cancellationToken);
        }
    }
}
