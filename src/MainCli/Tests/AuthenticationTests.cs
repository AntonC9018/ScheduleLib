using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class AuthenticationTests
{
    private const string Teacher = "Curmanschii Anton";
    private static readonly string[] Scopes = GoogleAuthentication.LoginScopes;

    [Fact]
    public async Task OrdinaryResolutionNeverConsentsAndReturnsExactLoginCommand()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        var missing = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default));
        Assert.Equal("schedulelib auth login google --profile \"Curmanschii Anton\"", missing.LoginCommand);
        Assert.Equal(0, fixture.Tokens.Consents);
        Assert.Equal(0, fixture.Tokens.Refreshes);
        Assert.DoesNotContain("client-secret", missing.Message);
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
    }

    [Fact]
    public async Task ExplicitLoginPersistsAndResolutionRefreshesWithoutConsent()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(fixture.Config, Teacher, fixture.Services, default);
        Assert.Equal(1, fixture.Tokens.Consents);
        var credential = await auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default);
        Assert.Equal("access-secret", credential.Token.AccessToken);
        Assert.Equal(0, fixture.Tokens.Refreshes);
        var file = Assert.Single(Directory.GetFiles(fixture.Root, "*.json", SearchOption.AllDirectories));
        var stored = await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher);
        stored.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await new AtomicTokenStore(file).Write(stored, default);
        Assert.Equal("refreshable", Assert.Single((await auth.Status(Teacher, default)).Accounts).State);
        var refreshed = await auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default);
        Assert.Equal("refreshed-secret", refreshed.Token.AccessToken);
        Assert.Equal("refresh-secret", refreshed.Token.RefreshToken);
        Assert.Equal(1, fixture.Tokens.Refreshes);
        Assert.Equal(1, fixture.Tokens.Consents);
        Assert.Equal("authorized", Assert.Single((await auth.Status(Teacher, default)).Accounts).State);
        Assert.Equal("refreshed-secret", (await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher)).AccessToken);
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public async Task MissingScopesAndFailedRefreshRequireLoginAndRedactProviderErrors()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(fixture.Config, Teacher, fixture.Services, default);
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => auth.Resolve(fixture.Config, ["ungranted"], Teacher, fixture.Services, default));
        var file = Assert.Single(Directory.GetFiles(fixture.Root, "*.json", SearchOption.AllDirectories));
        var stored = await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher);
        stored.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await new AtomicTokenStore(file).Write(stored, default);
        fixture.Tokens.FailRefresh = true;
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default));
        Assert.DoesNotContain("access-secret", error.ToString());
        Assert.DoesNotContain("client-secret", error.ToString());
        Assert.Equal(1, fixture.Tokens.Consents);
        Assert.Equal("access-secret", (await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher)).AccessToken);
    }

    [Fact]
    public async Task TeacherAndClientPartitionsLogoutLocallyWithoutSecrets()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        var second = fixture.Config with { ApiKeysSource = new ManualGoogleApiKeysSource { Value = new() { ClientId = "other-client", ClientSecret = "other-secret" } } };
        await auth.Login(new[] { fixture.Config, fixture.Config, second }, Teacher, fixture.Services, default);
        Assert.Equal(2, fixture.Tokens.Consents);
        await auth.Login(fixture.Config, "Nartea Nichita", fixture.Services, default);
        Assert.Equal(2, (await auth.Status(Teacher, default)).Accounts.Count);
        Assert.Single((await auth.Status("Nartea Nichita", default)).Accounts);
        var first = await auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default);
        await auth.Logout(Teacher, default);
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
        Assert.Single((await auth.Status("Nartea Nichita", default)).Accounts);
        await Assert.ThrowsAsync<IOException>(() => first.Flow.DataStore.StoreAsync(Teacher, first.Token));
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
        await auth.Logout(Teacher, default);
        Assert.Equal(0, fixture.Tokens.Refreshes);
    }

    [Fact]
    public async Task CancelledLoginPreservesAuthorizationAndHeldLockReportsBusy()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(fixture.Config, Teacher, fixture.Services, default);
        fixture.Tokens.CancelConsent = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => auth.Login(fixture.Config, Teacher, fixture.Services, default));
        Assert.Single((await auth.Status(Teacher, default)).Accounts);
        var directory = Assert.Single(Directory.GetDirectories(fixture.Root));
        await using var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".operation.lock"), default);
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => auth.Logout(Teacher, default));
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => auth.Resolve(fixture.Config, Scopes, Teacher, fixture.Services, default));
    }

    [Fact]
    public async Task AtomicWritesNeverExposePartialTokensAndCancellationPreservesPrevious()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "token.json");
        var store = new AtomicTokenStore(path);
        await store.Write(new TokenResponse { AccessToken = "old" }, default);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Write(new TokenResponse { AccessToken = "cancelled" }, cancelled.Token));
        Assert.Equal("old", (await store.GetAsync<TokenResponse>(Teacher)).AccessToken);
        var writes = Enumerable.Range(0, 12).Select(i => store.Write(new TokenResponse { AccessToken = i.ToString() }, default));
        await Task.WhenAll(writes);
        Assert.NotNull(await store.GetAsync<TokenResponse>(Teacher));
        Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
    }

    [Theory]
    [InlineData("auth", "login", "google", "--output", "bad", "--json")]
    [InlineData("auth", "status", "google", "--no-cache", "--json")]
    [InlineData("auth", "logout", "unknown", "--json")]
    [InlineData("auth", "status", "--json")]
    public async Task InvalidProviderAndIrrelevantOptionsFailBeforeWork(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(2, await CliHost.Run(args));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, result.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData("status")]
    [InlineData("logout")]
    public async Task StatusAndLogoutRouteWithoutProfileOrScheduleInitialization(string verb)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(3, await CliHost.Run(["auth", verb, "google", "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Contains("--profile", result.RootElement.GetProperty("errors")[0].GetString());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task LowercaseProviderRoutesStatusWithRealProfileAndNoScheduleInput()
    {
        using var fixture = new Fixture();
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await CliHost.Run(["auth", "status", "google", "--profile", Teacher, "--project", fixture.Root, "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal("auth status google", result.RootElement.GetProperty("command").GetString());
            Assert.Equal(Teacher, result.RootElement.GetProperty("data").GetProperty("profile").GetString());
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Root));
            Assert.DoesNotContain("secret", output.ToString());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task ConfiguredClientSourceAndRuntimeResolverUseSelectedTypedProfile()
    {
        using var fixture = new Fixture();
        using var settings = await CliSettings.Load(new() { Profile = Teacher }, fixture.Root, Path.Combine(fixture.Root, "missing-settings.json"));
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        settings.ConfigureServices(services);
        services.AddScoped<CurrentUserNameProvider>();
        services.AddScoped<GoogleCredentialResolver>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Google:ClientId"] = "configured-client", ["Google:ClientSecret"] = "configured-client-secret",
        }).Build());
        services.AddSingleton<IGoogleCredentialAuthorization>(_ => fixture.Auth);
        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var config = settings.Get(GoogleCalendarConfig.Key)!.Credentials!.Build();
        var resolver = scope.ServiceProvider.GetRequiredService<GoogleCredentialResolver>();
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => resolver.Resolve(config, Scopes, default));
        var authorization = (GoogleAuthentication)scope.ServiceProvider.GetRequiredService<IGoogleCredentialAuthorization>();
        await authorization.Login(config, Teacher, scope.ServiceProvider, default);
        Assert.Equal("access-secret", (await resolver.Resolve(config, Scopes, default)).Token.AccessToken);
        Assert.Equal(1, fixture.Tokens.Consents);
        Assert.Equal(0, fixture.Tokens.Refreshes);
    }

    [Theory]
    [InlineData(AuthProvider.google)]
    [InlineData(AuthProvider.microsoft)]
    public async Task CancelledStatusReturnsOneCancelledJsonResult(AuthProvider provider)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(130, await new AuthCommands().Status(new() { Provider = provider },
                new() { Profile = Teacher }, new() { Json = true }, new CancellationToken(true)));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal("cancelled", json.RootElement.GetProperty("status").GetString());
            Assert.Equal(130, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("data").ValueKind);
        }
        finally { Console.SetOut(original); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "schedulelib-auth-" + Guid.NewGuid().ToString("N"));
        public FakeTokens Tokens { get; } = new();
        public GoogleAuthentication Auth => new(Root, Tokens);
        public ServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();
        public BuiltGoogleCredentialsConfig Config { get; } = new() { CredentialsPath = "ignored-project-token-path", ApiKeysSource = new ManualGoogleApiKeysSource { Value = new() { ClientId = "client", ClientSecret = "client-secret" } } };
        public Fixture() => Directory.CreateDirectory(Root);
        public void Dispose() { Services.Dispose(); Directory.Delete(Root, true); }
    }

    private sealed class FakeTokens : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];
        public int Consents { get; private set; }
        public int Refreshes { get; private set; }
        public bool FailRefresh { get; set; }
        public bool CancelConsent { get; set; }
        public Task<TokenResponse> Consent(ClientSecrets secrets, string teacher, string[] scopes, CancellationToken token)
        {
            Consents++;
            if (CancelConsent) throw new OperationCanceledException();
            return Task.FromResult(new TokenResponse { AccessToken = "access-secret", RefreshToken = "refresh-secret", Scope = string.Join(' ', scopes), IssuedUtc = DateTime.UtcNow, ExpiresInSeconds = 3600 });
        }
        public Task<TokenResponse?> Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token)
        {
            Refreshes++;
            if (FailRefresh) throw new Exception("provider leaked access-secret client-secret");
            return Task.FromResult<TokenResponse?>(new() { AccessToken = "refreshed-secret", IssuedUtc = DateTime.UtcNow, ExpiresInSeconds = 3600 });
        }
        public UserCredential CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(new() { ClientSecrets = secrets, Scopes = scopes, DataStore = store });
            _flows.Add(flow);
            return new(flow, teacher, authorization);
        }
        public void Dispose() { foreach (var flow in _flows) flow.Dispose(); _flows.Clear(); }
    }
}
