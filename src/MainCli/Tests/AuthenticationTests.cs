using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

public sealed class AuthenticationTests
{
    private const string Teacher = "Curmanschii Anton";
    private static readonly string[] Scopes = GoogleAuthentication.LoginScopes;

    [Fact]
    public void LoginBuilderRequiresCredentialsBeforeExecution()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        var builder = auth.Login(Teacher, fixture.Services);
        Assert.Throws<InvalidOperationException>(() => { _ = builder.Execute(default); });
        Assert.Equal(expected: 0, actual: fixture.Tokens.Consents);
    }

    [Fact]
    public async Task OrdinaryResolutionNeverConsentsAndReturnsExactLoginCommand()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        var missing = await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            auth.Resolve(
                new GoogleCredentialRequest(
                    fixture.Config,
                    Scopes,
                    Teacher,
                    fixture.Services),
                default));
        Assert.Equal(
            expected: "schedulelib auth login google --profile \"Curmanschii Anton\"",
            actual: missing.LoginCommand);
        Assert.Equal(expected: 0, actual: fixture.Tokens.Consents);
        Assert.Equal(expected: 0, actual: fixture.Tokens.Refreshes);
        Assert.DoesNotContain(expectedSubstring: "client-secret", actualString: missing.Message);
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
    }

    [Fact]
    public async Task ExplicitLoginPersistsAndResolutionRefreshesWithoutConsent()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(Teacher, fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default);
        Assert.Equal(expected: 1, actual: fixture.Tokens.Consents);
        var credential = await auth.Resolve(
            new GoogleCredentialRequest(
                fixture.Config,
                Scopes,
                Teacher,
                fixture.Services),
            default);
        Assert.Equal(expected: "access-secret", actual: credential.Token.AccessToken);
        Assert.Equal(expected: 0, actual: fixture.Tokens.Refreshes);

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetFiles(
                path: fixture.Root,
                searchPattern: "*.json",
                SearchOption.AllDirectories);
            return collection;
        }

        var file = Assert.Single(
            CalculateCollection());
        var stored = await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher);
        stored.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await new AtomicTokenStore(file).Write(stored, default);
        Assert.Equal(
            expected: "refreshable",
            actual: Assert.Single((await auth.Status(Teacher, default)).Accounts).State);
        var refreshed = await auth.Resolve(
            new GoogleCredentialRequest(
                fixture.Config,
                Scopes,
                Teacher,
                fixture.Services),
            default);
        Assert.Equal(expected: "refreshed-secret", actual: refreshed.Token.AccessToken);
        Assert.Equal(expected: "refresh-secret", actual: refreshed.Token.RefreshToken);
        Assert.Equal(expected: 1, actual: fixture.Tokens.Refreshes);
        Assert.Equal(expected: 1, actual: fixture.Tokens.Consents);
        Assert.Equal(
            expected: "authorized",
            actual: Assert.Single((await auth.Status(Teacher, default)).Accounts).State);
        Assert.Equal(
            expected: "refreshed-secret",
            actual: (await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher)).AccessToken);
        if (!OperatingSystem.IsWindows())
        {
            var expected2 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            var actual2 = File.GetUnixFileMode(file);
            Assert.Equal(expected: expected2, actual: actual2);
        }
    }

    [Fact]
    public async Task NonAuthenticationRefreshFailureIsNotReportedAsLoginRequired()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(Teacher, fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default);
        var file = Assert.Single(Directory.GetFiles(
            path: fixture.Root,
            searchPattern: "*.json",
            SearchOption.AllDirectories));
        var stored = await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher);
        stored.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await new AtomicTokenStore(file).Write(stored, default);
        var failure = new HttpRequestException("Google token endpoint unreachable");
        fixture.Tokens.RefreshError = failure;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            auth.Resolve(
                new GoogleCredentialRequest(
                    fixture.Config,
                    Scopes,
                    Teacher,
                    fixture.Services),
                default));
        Assert.Same(expected: failure, actual: error);
    }

    [Fact]
    public async Task MissingScopesAndFailedRefreshRequireLoginAndRedactProviderErrors()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(Teacher, fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default);
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            auth.Resolve(
                new GoogleCredentialRequest(
                    fixture.Config,
                    ["ungranted"],
                    Teacher,
                    fixture.Services),
                default));

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetFiles(
                path: fixture.Root,
                searchPattern: "*.json",
                SearchOption.AllDirectories);
            return collection;
        }

        var file = Assert.Single(
            CalculateCollection());
        var stored = await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher);
        stored.IssuedUtc = DateTime.UtcNow.AddHours(-2);
        await new AtomicTokenStore(file).Write(stored, default);
        fixture.Tokens.FailRefresh = true;
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            auth.Resolve(
                new GoogleCredentialRequest(
                    fixture.Config,
                    Scopes,
                    Teacher,
                    fixture.Services),
                default));
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: error.Message);
        Assert.DoesNotContain(expectedSubstring: "client-secret", actualString: error.Message);
        Assert.IsType<TokenResponseException>(error.InnerException);
        Assert.Contains(
            expectedSubstring: "AuthenticationRequiredException translated from TokenResponseException (code invalid_grant)",
            actualString: fixture.Log.All);
        Assert.DoesNotContain(expectedSubstring: "secret", actualString: fixture.Log.All);
        Assert.Equal(expected: 1, actual: fixture.Tokens.Consents);
        Assert.Equal(
            expected: "access-secret",
            actual: (await new AtomicTokenStore(file).GetAsync<TokenResponse>(Teacher)).AccessToken);
    }

    [Fact]
    public async Task TeacherAndClientPartitionsLogoutLocallyWithoutSecrets()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        var second = fixture.Config with
        {
            ApiKeysSource = new ManualGoogleApiKeysSource
            {
                Value = new()
                {
                    ClientId = "other-client",
                    ClientSecret = "other-secret",
                },
            },
        };
        await auth.Login(Teacher, fixture.Services)
            .WithCredentials(new[]
            {
                fixture.Config,
                fixture.Config,
                second,
            })
            .Execute(default);
        Assert.Equal(expected: 2, actual: fixture.Tokens.Consents);
        await auth.Login("Nartea Nichita", fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default);
        Assert.Equal(expected: 2, actual: (await auth.Status(Teacher, default)).Accounts.Count);
        Assert.Single((await auth.Status("Nartea Nichita", default)).Accounts);
        var first = await auth.Resolve(
            new GoogleCredentialRequest(
                fixture.Config,
                Scopes,
                Teacher,
                fixture.Services),
            default);
        await auth.Logout(Teacher, default);
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
        Assert.Single((await auth.Status("Nartea Nichita", default)).Accounts);
        await Assert.ThrowsAsync<IOException>(() => first.Flow.DataStore.StoreAsync(Teacher, first.Token));
        Assert.Empty((await auth.Status(Teacher, default)).Accounts);
        await auth.Logout(Teacher, default);
        Assert.Equal(expected: 0, actual: fixture.Tokens.Refreshes);
    }

    [Fact]
    public async Task CancelledLoginPreservesAuthorizationAndHeldLockReportsBusy()
    {
        using var fixture = new Fixture();
        using var auth = fixture.Auth;
        await auth.Login(Teacher, fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default);
        fixture.Tokens.CancelConsent = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => auth.Login(Teacher, fixture.Services)
            .WithCredentials(fixture.Config)
            .Execute(default));
        Assert.Single((await auth.Status(Teacher, default)).Accounts);

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetDirectories(fixture.Root);
            return collection;
        }

        var directory = Assert.Single(CalculateCollection());
        await using var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".operation.lock"), default);
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => auth.Logout(Teacher, default));
        await Assert.ThrowsAsync<LocalOperationBusyException>(() =>
            auth.Resolve(
                new GoogleCredentialRequest(
                    fixture.Config,
                    Scopes,
                    Teacher,
                    fixture.Services),
                default));
    }

    [Fact]
    public async Task AtomicWritesNeverExposePartialTokensAndCancellationPreservesPrevious()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "token.json");
        var store = new AtomicTokenStore(path);
        await store.Write(
            new TokenResponse
            {
                AccessToken = "old",
            },
            default);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Write(
            new TokenResponse
            {
                AccessToken = "cancelled",
            },
            cancelled.Token));
        Assert.Equal(expected: "old", actual: (await store.GetAsync<TokenResponse>(Teacher)).AccessToken);
        var writes = Enumerable.Range(start: 0, count: 12)
            .Select(i => store.Write(
                new TokenResponse
                {
                    AccessToken = i.ToString(),
                },
                default));
        await Task.WhenAll(writes);
        Assert.NotNull(await store.GetAsync<TokenResponse>(Teacher));

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetFiles(path: fixture.Root, searchPattern: "*.tmp");
            return collection;
        }

        Assert.Empty(CalculateCollection());
    }

    [Theory]
    [InlineData(
        "auth",
        "login",
        "google",
        "--json",
        "--output",
        "bad")]
    [InlineData(
        "auth",
        "status",
        "google",
        "--json",
        "--no-cache")]
    [InlineData(
        "auth",
        "logout",
        "--json",
        "unknown")]
    [InlineData(
        "auth",
        "status",
        "--json")]
    public async Task InvalidProviderAndIrrelevantOptionsFailBeforeWork(params string[] args)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(expected: 2, actual: await CliHost.Run(args));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: 2,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
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
            Assert.Equal(expected: 3, actual: await CliHost.Run(["auth", verb, "google", "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                expectedSubstring: "--profile",
                actualString: result.RootElement.GetProperty("errors")[0]
                    .GetString());
        }
        finally
        {
            Console.SetOut(original);
        }
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
            Assert.Equal(
                expected: 0,
                actual: await CliHost.Run(
                    ["auth", "status", "google", "--profile", Teacher, "--project", fixture.Root, "--json"]));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: "auth status google",
                actual: result.RootElement.GetProperty("command")
                    .GetString());
            Assert.Equal(
                expected: Teacher,
                actual: result.RootElement.GetProperty("data")
                    .GetProperty("profile")
                    .GetString());

            string[] CalculateCollection()
            {
                string[] collection = Directory.GetFileSystemEntries(fixture.Root);
                return collection;
            }

            Assert.Empty(CalculateCollection());
            Assert.DoesNotContain(expectedSubstring: "secret", actualString: output.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task ConfiguredClientSourceAndRuntimeResolverUseSelectedTypedProfile()
    {
        using var fixture = new Fixture();

        string CalculateUserFile()
        {
            string userFile2 = Path.Combine(fixture.Root, "missing-settings.json");
            return userFile2;
        }

        using var settings = await CliSettings.Load(
            new()
            {
                Profile = Teacher,
            },
            invocationDirectory: fixture.Root,
            userFile: CalculateUserFile());
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        settings.ConfigureServices(services);
        services.AddScoped<CurrentUserNameProvider>();
        services.AddScoped<GoogleCredentialResolver>();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Google:ClientId"] = "configured-client",
            ["Google:ClientSecret"] = "configured-client-secret",
        })
            .Build());
        services.AddSingleton<IGoogleCredentialAuthorization>(_ => fixture.Auth);
        await using var container = services.BuildServiceProvider();
        await using var scope = container.CreateAsyncScope();
        var config = settings.Get(GoogleCalendarConfig.Key)!.Credentials!
            .Build();
        var resolver = scope.ServiceProvider.GetRequiredService<GoogleCredentialResolver>();
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => resolver.Resolve(
            config,
            Scopes,
            default));
        var authorization =
            (GoogleAuthentication) scope.ServiceProvider.GetRequiredService<IGoogleCredentialAuthorization>();
        await authorization.Login(Teacher, scope.ServiceProvider)
            .WithCredentials(config)
            .Execute(default);
        Assert.Equal(
            expected: "access-secret",
            actual: (await resolver.Resolve(
                config,
                Scopes,
                default)).Token.AccessToken);
        Assert.Equal(expected: 1, actual: fixture.Tokens.Consents);
        Assert.Equal(expected: 0, actual: fixture.Tokens.Refreshes);
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

            CancellationToken CalculateCancellationToken()
            {
                var cancellationToken = new CancellationToken(true);
                return cancellationToken;
            }

            Assert.Equal(
                expected: 130,
                actual: await new AuthCommands().Status(
                    new()
                    {
                        Provider = provider,
                    },
                    new()
                    {
                        Profile = Teacher,
                    },
                    new()
                    {
                        Json = true,
                    },
                    CalculateCancellationToken()));
            using var json = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: "cancelled",
                actual: json.RootElement.GetProperty("status")
                    .GetString());
            Assert.Equal(
                expected: 130,
                actual: json.RootElement.GetProperty("exitCode")
                    .GetInt32());
            Assert.Equal(expected: JsonValueKind.Null, actual: json.RootElement.GetProperty("data").ValueKind);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TestTempDirectory _temp = new("schedulelib-tests");

        public string Root => _temp.Path;

        public FakeTokens Tokens { get; } = new();

        public DebugLogCapture Log { get; } = new();

        public GoogleAuthentication Auth => new(Root, Tokens, Log);

        public ServiceProvider Services { get; } = new ServiceCollection().BuildServiceProvider();

        public BuiltGoogleCredentialsConfig Config { get; } = new()
        {
            CredentialsPath = "ignored-project-token-path",
            ApiKeysSource = new ManualGoogleApiKeysSource
            {
                Value = new()
                {
                    ClientId = "client",
                    ClientSecret = "client-secret",
                },
            },
        };

        public Fixture() => Directory.CreateDirectory(Root);

        public void Dispose()
        {
            Services.Dispose();
            _temp.Dispose();
        }

    }

    private sealed class FakeTokens : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];

        public int Consents { get; private set; }

        public int Refreshes { get; private set; }

        public bool FailRefresh { get; set; }

        public Exception? RefreshError { get; set; }

        public bool CancelConsent { get; set; }

        public Task<TokenResponse> Consent(
            GoogleTokenContext context,
            CancellationToken token)
        {
            Consents++;
            if (CancelConsent)
            {
                throw new OperationCanceledException();
            }

            return Task.FromResult(new TokenResponse
            {
                AccessToken = "access-secret",
                RefreshToken = "refresh-secret",
                Scope = string.Join(' ', context.Scopes),
                IssuedUtc = DateTime.UtcNow,
                ExpiresInSeconds = 3600,
            });
        }

        public Task<TokenResponse?> Refresh(
            GoogleTokenContext context,
            TokenResponse authorization,
            CancellationToken token)
        {
            Refreshes++;
            if (RefreshError is { } refreshError)
            {
                throw refreshError;
            }
            if (FailRefresh)
            {
                throw new TokenResponseException(new TokenErrorResponse
                {
                    Error = "invalid_grant",
                    ErrorDescription = "provider leaked access-secret client-secret",
                });
            }

            return Task.FromResult<TokenResponse?>(new()
            {
                AccessToken = "refreshed-secret",
                IssuedUtc = DateTime.UtcNow,
                ExpiresInSeconds = 3600,
            });
        }

        public UserCredential CreateCredential(
            GoogleTokenContext context,
            TokenResponse authorization,
            IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(new()
            {
                ClientSecrets = context.Secrets,
                Scopes = context.Scopes,
                DataStore = store,
            });
            _flows.Add(flow);
            return new(
                flow,
                context.Teacher,
                authorization);
        }

        public void Dispose()
        {
            foreach (var flow in _flows)
            {
                flow.Dispose();
            }

            _flows.Clear();
        }
    }
}
