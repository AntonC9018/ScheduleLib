using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using Directory = System.IO.Directory;
using File = System.IO.File;

public sealed class MicrosoftTests
{
    private const string Teacher = "Curmanschii Anton";

    private static MicrosoftAuthConfig Config
    {
        get
        {
            return new()
            {
                TenantId = "tenant",
                ClientId = "client",
            };
        }
    }

    [Fact]
    public async Task OrdinaryAuthenticationIsSilentAndHasExactLoginGuidance()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();
        using var log = new DebugLogCapture();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens, log);
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            authentication.Resolve(
                Config,
                Teacher,
                default));
        Assert.Equal(
            expected: "schedulelib auth login microsoft --profile \"Curmanschii Anton\"",
            actual: error.LoginCommand);
        Assert.Equal(expected: 0, actual: tokens.Consents);
        Assert.Equal(expected: 0, actual: tokens.SilentCalls);
        await authentication.Login(
            Config,
            Teacher,
            default);
        Assert.Equal(
            expected: "access-secret",
            actual: await authentication.Resolve(
                Config,
                Teacher,
                default));
        Assert.Equal(expected: 1, actual: tokens.Consents);
        Assert.Equal(expected: 1, actual: tokens.SilentCalls);
        tokens.SilentFailure = new MsalUiRequiredException(
            errorCode: "invalid_grant",
            errorMessage: "provider exposed access-secret");
        var failed =
            await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(
                Config,
                Teacher,
                default));
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: failed.Message);
        Assert.Same(expected: tokens.SilentFailure, actual: failed.InnerException);
        Assert.Contains(
            expectedSubstring: "AuthenticationRequiredException translated from MsalUiRequiredException (code invalid_grant)",
            actualString: log.All);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: log.All);
        Assert.Equal(expected: 1, actual: tokens.Consents);
        Assert.Single((await authentication.Status(Teacher, default)).Accounts);
    }

    public static TheoryData<MsalException> NonAuthenticationConsentFailures => new()
    {
        new MsalServiceException(errorCode: "temporarily_unavailable", errorMessage: "provider exposed access-secret"),
        new MsalServiceException(errorCode: MsalError.RequestTimeout, errorMessage: "provider exposed access-secret"),
        new MsalClientException(errorCode: MsalError.InvalidClient, errorMessage: "provider exposed access-secret"),
    };

    [Theory]
    [MemberData(nameof(NonAuthenticationConsentFailures))]
    public async Task NonAuthenticationConsentFailuresAreNotReportedAsLoginRequired(MsalException failure)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var log = new DebugLogCapture();
        var tokens = new FakeTokens
        {
            ConsentFailure = failure,
        };
        var authentication = new MicrosoftAuthentication(temp.Path, tokens, log);
        var error = await Assert.ThrowsAnyAsync<MsalException>(() => authentication.Login(
            Config,
            Teacher,
            default));
        Assert.Same(expected: failure, actual: error);
        var classified = CliAuthenticationExecution.ClassifyMicrosoft(error, cancellationRequested: false);
        Assert.Equal(expected: CliExit.Unexpected, actual: classified.ExitCode);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: string.Join('\n', classified.Errors));
        Assert.DoesNotContain(expectedSubstring: "translated", actualString: log.All);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: log.All);
    }

    public static TheoryData<MsalException> RejectedConsentFailures => new()
    {
        new MsalClientException(errorCode: MsalError.AuthenticationCanceledError, errorMessage: "provider exposed access-secret"),
        new MsalServiceException(errorCode: MsalError.AccessDenied, errorMessage: "provider exposed access-secret"),
        new MsalUiRequiredException(errorCode: MsalError.InvalidGrantError, errorMessage: "provider exposed access-secret"),
    };

    [Theory]
    [MemberData(nameof(RejectedConsentFailures))]
    public async Task RejectedConsentRequiresLogin(MsalException failure)
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        using var log = new DebugLogCapture();
        var tokens = new FakeTokens
        {
            ConsentFailure = failure,
        };
        var authentication = new MicrosoftAuthentication(temp.Path, tokens, log);
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Login(
            Config,
            Teacher,
            default));
        Assert.Same(expected: failure, actual: error.InnerException);
        Assert.Equal(
            expected: CliExit.AuthenticationRequired,
            actual: CliAuthenticationExecution.ClassifyMicrosoft(error, cancellationRequested: false).ExitCode);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: error.Message);
        Assert.Contains(expectedSubstring: $"(code {failure.ErrorCode})", actualString: log.All);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: log.All);
    }

    [Fact]
    public async Task AccountPartitionsCancellationAndLogoutPreserveOtherTeachers()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        await authentication.Login(
            Config,
            Teacher,
            default);
        tokens.AccountId = "second-account";
        await authentication.Login(
            Config,
            Teacher,
            default);
        await authentication.Login(
            Config,
            "Nartea Nichita",
            default);
        tokens.CancelConsent = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authentication.Login(
            Config,
            Teacher,
            default));
        Assert.Equal(
            expected: "second-account",
            actual: Assert.Single((await authentication.Status(Teacher, default)).Accounts).AccountId);
        var files = Directory.GetFiles(
            path: temp.Path,
            searchPattern: "*.json",
            SearchOption.AllDirectories);
        Assert.True(files.Length >= 5);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var file in files)
            {
                var expected2 = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var actual2 = File.GetUnixFileMode(file);
                Assert.Equal(expected: expected2, actual: actual2);
            }
        }

        await authentication.Logout(Teacher, default);
        Assert.Empty((await authentication.Status(Teacher, default)).Accounts);
        Assert.Single((await authentication.Status("Nartea Nichita", default)).Accounts);
        await authentication.Logout(Teacher, default);
    }

    [Fact]
    public async Task TokenOperationsRejectConflictingStateAccess()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var authentication = new MicrosoftAuthentication(temp.Path, new FakeTokens());
        await authentication.Login(
            Config,
            Teacher,
            default);

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetDirectories(temp.Path);
            return collection;
        }

        var directory = Assert.Single(CalculateCollection());
        await using var lease = await LocalFileLock.Acquire(
            Path.Combine(directory, ".operation.lock"),
            default);
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => authentication.Resolve(
            Config,
            Teacher,
            default));
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => authentication.Logout(Teacher, default));
    }

    [Fact]
    public async Task MicrosoftCommandRouteUsesProviderAndEmitsRedactedStatus()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();

        MicrosoftAuthentication CalculateAuthentication()
        {
            var authentication = new MicrosoftAuthentication(temp.Path, tokens);
            return authentication;
        }

        var commands = new FakeAuth(CalculateAuthentication());
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await commands.Login(
                new()
                {
                    Provider = AuthProvider.microsoft,
                },
                new()
                {
                    Profile = Teacher,
                },
                new()
                {
                    Json = true,
                }));
        var result = capture.Result();
        Assert.Equal(
            expected: "auth login microsoft",
            actual: result.GetProperty("command")
                .GetString());
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: result.GetRawText());
        Assert.DoesNotContain(expectedSubstring: "cache-secret", actualString: result.GetRawText());
        Assert.Equal(expected: 1, actual: tokens.Consents);
    }

    [Theory]
    [InlineData(
        new[]
        {
            "auth",
            "status",
            "microsoft",
            "--cache-dir",
            "bad",
        },
        2)]
    [InlineData(
        new[]
        {
            "auth",
            "login",
            "microsoft",
            "--output",
            "bad",
        },
        2)]
    [InlineData(
        new[]
        {
            "auth",
            "login",
            "microsoft",
            "--help",
        },
        0)]
    public async Task HelpAndArgumentModelsStayIndependentOfSchedule(string[] args, int exit) =>
        Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Fact]
    public async Task StatusAndLogoutNeverResolveMicrosoftConfigurationOrConsent()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();

        MicrosoftAuthentication CalculateAuthentication()
        {
            var authentication = new MicrosoftAuthentication(temp.Path, tokens);
            return authentication;
        }

        var commands = new FakeAuth(CalculateAuthentication(), forbidConfig: true);
        Assert.Equal(
            expected: 0,
            actual: await commands.Status(
                new()
                {
                    Provider = AuthProvider.microsoft,
                },
                new()
                {
                    Profile = Teacher,
                },
                new()));
        Assert.Equal(
            expected: 0,
            actual: await commands.Logout(
                new()
                {
                    Provider = AuthProvider.microsoft,
                },
                new()
                {
                    Profile = Teacher,
                },
                new()));
        Assert.Equal(expected: 0, actual: tokens.Consents);
        Assert.Equal(expected: 0, actual: tokens.SilentCalls);
    }

    [Fact]
    public async Task NonAuthenticationSilentFailureIsNotReportedAsLoginRequired()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        await authentication.Login(
            Config,
            Teacher,
            default);
        tokens.SilentFailure = new IOException("token cache unreadable");
        var error = await Assert.ThrowsAsync<IOException>(() => authentication.Resolve(
            Config,
            Teacher,
            default));
        Assert.Same(expected: tokens.SilentFailure, actual: error);
    }

    [Fact]
    public async Task DifferentClientCannotUseProvisionedAccount()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        await authentication.Login(
            Config,
            Teacher,
            default);
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(
            new()
            {
                TenantId = Config.TenantId,
                ClientId = "other-client",
            },
            Teacher,
            default));
        Assert.Equal(expected: 0, actual: tokens.SilentCalls);
    }

    private sealed class FakeTokens : IMicrosoftTokenProvider
    {
        public int Consents;
        public int SilentCalls;
        public Exception? SilentFailure;
        public bool CancelConsent;
        public Exception? ConsentFailure;
        public string AccountId = "first-account";

        private MicrosoftToken Value()
        {
            DateTimeOffset CalculateExpiresOn()
            {
                var expiresOn = DateTimeOffset.UtcNow.AddHours(1);
                return expiresOn;
            }

            byte[] CalculateCache()
            {
                byte[] cache = Encoding.UTF8.GetBytes("cache-secret");
                return cache;
            }

            return new(
                AccountId: AccountId,
                AccessToken: "access-secret",
                CalculateExpiresOn(),
                MicrosoftScopes.Required,
                CalculateCache());
        }

        public Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token)
        {
            Consents++;
            if (CancelConsent)
            {
                throw new OperationCanceledException();
            }
            if (ConsentFailure is { } failure)
            {
                throw failure;
            }

            return Task.FromResult(Value());
        }

        public Task<MicrosoftToken> Silent(
            MicrosoftAuthConfig config,
            string accountId,
            byte[] cache,
            CancellationToken token)
        {
            SilentCalls++;
            if (SilentFailure is { } failure)
            {
                throw failure;
            }

            string CalculateActual()
            {
                string actual2 = Encoding.UTF8.GetString(cache);
                return actual2;
            }

            Assert.Equal(expected: "cache-secret", actual: CalculateActual());
            return Task.FromResult(Value());
        }
    }

    private sealed class FakeAuth(MicrosoftAuthentication authentication, bool forbidConfig = false) : AuthCommands
    {
        protected override MicrosoftAuthentication CreateMicrosoftAuthentication(CliCommandContext context) =>
            authentication;

        protected override MicrosoftAuthConfig LoadMicrosoftConfig()
        {
            Assert.False(forbidConfig);
            return Config;
        }
    }

    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly TextWriter _originalError = Console.Error;
        private readonly StringWriter _errors = new();
        public string Errors => _errors.ToString();
        private readonly StringWriter _text = new();

        public Capture()
        {
            Console.SetOut(_text);
            Console.SetError(_errors);
        }

        public JsonElement Result()
        {
            using var doc = JsonDocument.Parse(_text.ToString());
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            Console.SetOut(_original);
            Console.SetError(_originalError);
            _text.Dispose();
            _errors.Dispose();
        }
    }


}
