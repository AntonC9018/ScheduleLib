using System.Net;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Graph;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Curriculum.Download;
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
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() =>
            authentication.Resolve(
                Config,
                Teacher,
                default));
        Assert.Equal(expected: "schedulelib auth login microsoft --profile \"Curmanschii Anton\"", actual: error.LoginCommand);
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
        tokens.FailSilent = true;
        var failed =
            await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(
                Config,
                Teacher,
                default));
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: failed.ToString());
        Assert.Equal(expected: 1, actual: tokens.Consents);
        Assert.Single((await authentication.Status(Teacher, default)).Accounts);
    }

    [Fact]
    public async Task AccountPartitionsCancellationAndLogoutPreserveOtherTeachers()
    {
        using var temp = new TempDirectory();
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
                Assert.Equal(expected: UnixFileMode.UserRead | UnixFileMode.UserWrite, actual: File.GetUnixFileMode(file));
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
        using var temp = new TempDirectory();
        var authentication = new MicrosoftAuthentication(temp.Path, new FakeTokens());
        await authentication.Login(
            Config,
            Teacher,
            default);
        var directory = Assert.Single(Directory.GetDirectories(temp.Path));
        await using var lease = await LocalFileLock.Acquire(
            System.IO.Path.Combine(directory, ".operation.lock"),
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
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var commands = new FakeAuth(new MicrosoftAuthentication(temp.Path, tokens));
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await commands.Login(
                new() { Provider = AuthProvider.microsoft },
                new() { Profile = Teacher },
                new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(expected: "auth login microsoft", actual: result.GetProperty("command").GetString());
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
            "curricula",
            "download",
            "--no-cache",
        },
        2)]
    [InlineData(
        new[]
        {
            "curricula",
            "download",
            "--apply",
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
    [InlineData(
        new[]
        {
            "curricula",
            "download",
            "--help",
        },
        0)]
    public async Task HelpAndArgumentModelsStayIndependentOfSchedule(string[] args, int exit) =>
        Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Fact]
    public async Task GraphDocxDownloadUsesOwnedOutputsAndDisclosesCodedSource()
    {
        using var temp = new TempDirectory();
        var unrelated = System.IO.Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(path: unrelated, contents: "caller data");
        using var capture = new Capture();
        using var commands = new FakeGraphCommands();
        Assert.Equal(
            expected: 0,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(
            expected: CurriculaDownloadTasks.ConfiguredSource.Owner,
            actual: result.GetProperty("data").GetProperty("source").GetProperty("owner").GetString());
        Assert.Equal(expected: 1, actual: result.GetProperty("data").GetProperty("documentCount").GetInt32());
        var documentPath = Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        using var document = WordprocessingDocument.Open(documentPath, false);
        Assert.Equal(expected: "fixture", actual: document.MainDocumentPart!.Document.Body!.InnerText);
        Assert.Equal(expected: "caller data", actual: await File.ReadAllTextAsync(unrelated));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal(expected: 1, actual: commands.Handler.ContentRequests);
    }

    [Fact]
    public async Task LinuxLegacyPrerequisiteDoesNotDownloadOrModifyOriginals()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var temp = new TempDirectory();
        var original = System.IO.Path.Combine(temp.Path, "caller.doc");
        await File.WriteAllTextAsync(path: original, contents: "original");
        using var commands = new FakeGraphCommands("legacy.doc");
        Assert.Equal(
            expected: 8,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Equal(expected: 0, actual: commands.Handler.ContentRequests);
        Assert.Equal(expected: "original", actual: await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
    }

    [Fact]
    public async Task LegacyConverterReceivesStagedCopyAndOriginalSurvivesItsDeletion()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeConversionCommands();
        Assert.Equal(
            expected: 0,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Equal(
            expected: "original-doc",
            actual: await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.doc"))));
        Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        Assert.Empty(Directory.GetDirectories(path: temp.Path, searchPattern: ".conversion-*"));
    }

    [Fact]
    public async Task MissingOrStaleSourceAndMissingAuthenticationHaveDistinctPrerequisites()
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        using var stale = new FakeGraphCommands(missingSource: true);
        Assert.Equal(
            expected: 3,
            actual: await stale.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true }));
        Assert.Contains(expectedSubstring: "2024-2025", actualString: capture.Result().GetProperty("errors")[0].GetString());
        var missing = new MissingAuthenticationCommands();
        Assert.Equal(
            expected: 4,
            actual: await missing.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
    }

    [Fact]
    public async Task CancelledDownloadDoesNotPublishPartialDocument()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        using var commands = new FakeGraphCommands(cancelDownload: true);
        commands.Handler.Cancellation = cancellation;
        Assert.Equal(
            expected: 130,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new(),
                cancellation.Token));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.tmp"));
    }

    [Fact]
    public async Task ProviderTimeoutIsAnOperationFailureWithoutCommandCancellation()
    {
        using var temp = new TempDirectory();
        using var timeout = new FakeGraphCommands(cancelDownload: true);
        Assert.Equal(
            expected: 5,
            actual: await timeout.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
    }

    [Fact]
    public async Task StatusAndLogoutNeverResolveMicrosoftConfigurationOrConsent()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var commands = new FakeAuth(new MicrosoftAuthentication(temp.Path, tokens), forbidConfig: true);
        Assert.Equal(
            expected: 0,
            actual: await commands.Status(
                new() { Provider = AuthProvider.microsoft },
                new() { Profile = Teacher },
                new()));
        Assert.Equal(
            expected: 0,
            actual: await commands.Logout(
                new() { Provider = AuthProvider.microsoft },
                new() { Profile = Teacher },
                new()));
        Assert.Equal(expected: 0, actual: tokens.Consents);
        Assert.Equal(expected: 0, actual: tokens.SilentCalls);
    }

    [Fact]
    public async Task DifferentClientCannotUseProvisionedAccount()
    {
        using var temp = new TempDirectory();
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

    [Fact]
    public async Task GraphPaginationIncludesAllProgramsAndDocumentsAndOnlyReads()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(paginate: true);
        Assert.Equal(
            expected: 0,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Equal(expected: 3, actual: commands.Handler.ContentRequests);
        Assert.Equal(expected: 3, actual: Directory.GetFiles(path: temp.Path, searchPattern: "*.docx").Length);
        Assert.Equal(expected: 2, actual: commands.Handler.PageRequests);
    }

    [Fact]
    public async Task UnrelatedCollisionPreservesCallerDocument()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "group - course.docx");
        await File.WriteAllTextAsync(path: path, contents: "caller document");
        using var commands = new FakeGraphCommands();
        Assert.Equal(
            expected: 5,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Equal(expected: "caller document", actual: await File.ReadAllTextAsync(path));
        Assert.Equal(expected: 0, actual: commands.Handler.ContentRequests);
    }

    [Fact]
    public async Task CorruptDocxIsRejectedBeforePublication()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(invalidDocx: true);
        Assert.Equal(
            expected: 5,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionFailureKeepsOriginalAndReportsPartialManifest(bool missingWord)
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        using var commands = new FailedConversionCommands(missingWord);
        Assert.Equal(
            expected: 6,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(expected: "partial", actual: result.GetProperty("status").GetString());
        Assert.Equal(
            expected: "original-doc",
            actual: await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.doc"))));
        Assert.Empty(Directory.GetDirectories(path: temp.Path, searchPattern: ".conversion-*"));
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal(expected: "partial", actual: manifest.RootElement.GetProperty("Status").GetString());
        Assert.Single(manifest.RootElement.GetProperty("Artifacts").EnumerateArray());
    }

    [Fact]
    public async Task CancellationAfterFirstDownloadRetainsCompletedArtifactAndManifest()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        using var capture = new Capture();
        using var commands = new FakeGraphCommands(paginate: true, cancelDownload: true);
        commands.Handler.Cancellation = cancellation;
        Assert.Equal(
            expected: 130,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true },
                cancellation.Token));
        Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.tmp"));
        var result = capture.Result();
        Assert.Equal(expected: "cancelled", actual: result.GetProperty("status").GetString());
        Assert.Equal(expected: 2, actual: result.GetProperty("outputs").GetArrayLength());
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal(expected: "partial", actual: manifest.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task LaterCorruptDocumentReportsPartialOutputInsteadOfSuccess()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(paginate: true, invalidDocx: true);
        Assert.Equal(
            expected: 6,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new()));
        Assert.Single(Directory.GetFiles(path: temp.Path, searchPattern: "*.docx"));
        Assert.Empty(Directory.GetFiles(path: temp.Path, searchPattern: "*.tmp"));
    }

    [Theory]
    [InlineData(
        false,
        false,
        false)]
    [InlineData(
        false,
        false,
        true)]
    [InlineData(
        false,
        true,
        false)]
    [InlineData(
        false,
        true,
        true)]
    [InlineData(
        true,
        false,
        false)]
    [InlineData(
        true,
        false,
        true)]
    [InlineData(
        true,
        true,
        false)]
    [InlineData(
        true,
        true,
        true)]
    public async Task ProductionMiddlewarePreservesKnownCallbackFailures(
        bool busy,
        bool partial,
        bool nested)
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        Exception failure = busy
            ? new LocalOperationBusyException("Microsoft token state is busy.", new IOException("cache-secret"))
            : new AuthenticationRequiredException(provider: "microsoft", teacher: Teacher);
        if (nested)
        {
            failure = new IOException("access-secret", failure);
        }

        using var commands = new MiddlewareFailureCommands(failure, partial);
        var expected = partial ? 6 : busy ? 7 : 4;
        Assert.Equal(
            expected: expected,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(expected: expected, actual: result.GetProperty("exitCode").GetInt32());
        Assert.Equal(expected: partial ? "partial" : "failed", actual: result.GetProperty("status").GetString());
        var error = result.GetProperty("errors")[0].GetString();
        Assert.Equal(
            expected: busy ? "Microsoft token state is busy." : new AuthenticationRequiredException(provider: "microsoft", teacher: Teacher).Message,
            actual: error);
        Assert.Contains(error!, capture.Errors);
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: (string) $"{result.GetRawText()}{capture.Errors}");
        Assert.DoesNotContain(expectedSubstring: "cache-secret", actualString: (string) $"{result.GetRawText()}{capture.Errors}");
        Assert.Equal(expected: partial ? 1 : 0, actual: commands.Handler.ContentRequests);
        Assert.Equal(expected: partial ? 1 : 0, actual: Directory.GetFiles(path: temp.Path, searchPattern: "*.docx").Length);
        if (partial)
        {
            using var manifest = JsonDocument.Parse(
                await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
            Assert.Equal(expected: "partial", actual: manifest.RootElement.GetProperty("Status").GetString());
            Assert.Single(manifest.RootElement.GetProperty("Artifacts").EnumerateArray());
        }
        else
        {
            Assert.Empty(Directory.GetFiles(temp.Path));
        }
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 6)]
    public async Task ProductionMiddlewareKeepsUnknownCallbackFailuresRedacted(bool partial, int expected)
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        using var commands = new MiddlewareFailureCommands(new IOException("access-secret cache-secret"), partial);
        Assert.Equal(
            expected: expected,
            actual: await commands.Download(
                new() { Profile = Teacher },
                new() { Directory = temp.Path },
                new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(expected: "Curricula download or local publication failed.", actual: result.GetProperty("errors")[0].GetString());
        Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: (string) $"{result.GetRawText()}{capture.Errors}");
        Assert.DoesNotContain(expectedSubstring: "cache-secret", actualString: (string) $"{result.GetRawText()}{capture.Errors}");
    }

    private sealed class MiddlewareFailureCommands(Exception failure, bool partial) : CurriculaCommands, IDisposable
    {
        public FakeGraphHandler Handler { get; } = new(
            "course.docx",
            missingSource: false,
            cancelDownload: false,
            paginate: partial,
            invalidDocx: false);

        public void Dispose() => Handler.Dispose();

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            category: "Reliability",
            checkId: "CA2000:Dispose objects before losing scope",
            Justification = "The Graph adapter owns the HttpProvider and its middleware pipeline.")]
        protected override async Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token)
        {
            var calls = 0;
            var authentication = new DelegateAuthenticationProvider(request =>
            {
                // Preflight succeeds; fail during middleware authentication, before the terminal fixture handler.
                if (++calls > 1
                    && (!partial
                        || request.RequestUri!.AbsolutePath.EndsWith("/content", StringComparison.Ordinal)
                        && Handler.ContentRequests == 1))
                {
                    Assert.Contains(
                        expectedSubstring: "Microsoft.Graph.AuthenticationHandler",
                        actualString: new System.Diagnostics.StackTrace().ToString());
                    throw failure;
                }
                return Task.CompletedTask;
            });
            using (var preflight = new HttpRequestMessage())
            {
                await authentication.AuthenticateRequestAsync(preflight);
            }

            var http = new HttpProvider();
            // Test-only replacement of the default client's transport: use the same SDK factory and
            // middleware flags as HttpProvider's production constructor, with a local terminal handler.
            var clientField = typeof(HttpProvider).GetField(
                "httpClient",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            ((HttpClient) clientField.GetValue(http)!).Dispose();
            clientField.SetValue(obj: http, value: GraphClientFactory.Create(authenticationProvider: null, finalHandler: Handler));
            return new GraphCurriculaProvider(new GraphServiceClient(authentication, http), http);
        }
    }

    private sealed class FakeTokens : IMicrosoftTokenProvider
    {
        public int Consents;
        public int SilentCalls;
        public bool FailSilent;
        public bool CancelConsent;
        public string AccountId = "first-account";

        private MicrosoftToken Value()
        {
            return new(
                AccountId: AccountId,
                AccessToken: "access-secret",
                DateTimeOffset.UtcNow.AddHours(1),
                CurriculaDownloadTasks.ApiRequiredScopes,
                Encoding.UTF8.GetBytes("cache-secret"));
        }

        public Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token)
        {
            Consents++;
            if (CancelConsent)
            {
                throw new OperationCanceledException();
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
            if (FailSilent)
            {
                throw new Exception("provider exposed access-secret");
            }

            Assert.Equal(expected: "cache-secret", actual: Encoding.UTF8.GetString(cache));
            return Task.FromResult(Value());
        }
    }

    private sealed class FakeAuth(MicrosoftAuthentication authentication, bool forbidConfig = false) : AuthCommands
    {
        protected override MicrosoftAuthentication CreateMicrosoftAuthentication() => authentication;

        protected override MicrosoftAuthConfig LoadMicrosoftConfig()
        {
            Assert.False(forbidConfig);
            return Config;
        }
    }

    private class FakeGraphCommands(
        string fileName = "course.docx",
        bool missingSource = false,
        bool cancelDownload = false,
        bool paginate = false,
        bool invalidDocx = false) : CurriculaCommands, IDisposable
    {
        public void Dispose() => Handler.Dispose();

        public FakeGraphHandler Handler = new(
            fileName,
            missingSource: missingSource,
            cancelDownload: cancelDownload,
            paginate: paginate,
            invalidDocx: invalidDocx);

        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            category: "Reliability",
            checkId: "CA2000:Dispose objects before losing scope",
            Justification = "The returned fixture adapter owns the HttpProvider.")]
        protected override Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token)
        {
            var http = new HttpProvider(
                Handler,
                disposeHandler: true,
                serializer: null);
            return Task.FromResult<ICurriculaProvider>(new GraphCurriculaProvider(
                    new GraphServiceClient(new DelegateAuthenticationProvider(_ => Task.CompletedTask), http),
                    http));
        }
    }

    private sealed class MissingAuthenticationCommands : CurriculaCommands
    {
        protected override Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token) =>
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
    }

    private sealed class FakeConversionCommands() : FakeGraphCommands("legacy.doc")
    {
        protected override void EnsureLegacyCapability()
        {
        }

        protected override async Task<bool> ConvertLegacy(
            string input,
            string output,
            CancellationToken token)
        {
            Assert.Equal(expected: "original-doc", actual: await File.ReadAllTextAsync(input, token));
            File.Delete(input); // Reproduce the legacy converter's destructive behavior on the staged copy.
            await File.WriteAllBytesAsync(
                output,
                Docx(),
                token);
            return true;
        }
    }

    private sealed class FailedConversionCommands(bool missingWord) : FakeGraphCommands("legacy.doc")
    {
        protected override void EnsureLegacyCapability()
        {
        }

        protected override Task<bool> ConvertLegacy(
            string input,
            string output,
            CancellationToken token)
        {
            File.Delete(input);
            if (missingWord)
            {
                throw new PlatformNotSupportedException("Microsoft Word is required.");
            }

            return Task.FromResult(false);
        }
    }

    private sealed class FakeGraphHandler(
        string fileName,
        bool missingSource,
        bool cancelDownload,
        bool paginate,
        bool invalidDocx) : HttpMessageHandler
    {
        public int ContentRequests;
        public int PageRequests;
        public CancellationTokenSource? Cancellation;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path.EndsWith("/content", StringComparison.Ordinal))
            {
                ContentRequests++;
                bool CheckCondition()
                {
                    bool conditionResult;
                    if (cancelDownload)
                    {
                        if (!paginate)
                        {
                            conditionResult = true;
                        }
                        else
                        {
                            conditionResult = ContentRequests == 2;
                        }
                    }
                    else
                    {
                        conditionResult = false;
                    }

                    if (conditionResult)
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition())
                {
                    Cancellation?.Cancel();
                    throw new OperationCanceledException(cancellationToken);
                }
                bool CheckCondition2()
                {
                    bool conditionResult2;
                    if (invalidDocx)
                    {
                        if (!paginate)
                        {
                            conditionResult2 = true;
                        }
                        else
                        {
                            conditionResult2 = ContentRequests == 2;
                        }
                    }
                    else
                    {
                        conditionResult2 = false;
                    }

                    if (conditionResult2)
                    {
                        return true;
                    }

                    return false;
                }

                response = new(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(
                                        fileName.EndsWith(".doc", StringComparison.Ordinal)
                                            ? Encoding.UTF8.GetBytes("original-doc")
                                            : CheckCondition2()
                                                ? Encoding.UTF8.GetBytes("not-docx")
                                                : Docx()),
                };
            }
            else
            {
                string json;
                if (path.EndsWith("/programs-page2", StringComparison.Ordinal))
                {
                    PageRequests++;
                    json = "{\"value\":[{\"id\":\"program2\",\"name\":\"group2\",\"folder\":{}}]}";
                }
                else if (path.EndsWith("/documents-page2", StringComparison.Ordinal))
                {
                    PageRequests++;
                    json = "{\"value\":[{\"id\":\"file2\",\"name\":\"course2.docx\",\"file\":{}}]}";
                }
                else if (path.Contains("/items/root/children", StringComparison.Ordinal))
                {
                    json = (string) $"{{\"value\":[{{\"id\":\"program\",\"name\":\"group\",\"folder\":{{}}}}]{(paginate
                            ? ",\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/programs-page2\"}"
                            : "}")}";
                }
                else if (path.Contains("/items/program", StringComparison.Ordinal))
                {
                    json = JsonSerializer.Serialize(
                        new
                        {
                            value = new[]
                            {
                                new
                                {
                                    id = "file",
                                    name = fileName,
                                    file = new { mimeType = "application/octet-stream" },
                                },
                            },
                        });
                    bool CheckCondition3()
                    {
                        if (!(paginate))
                        {
                            return false;
                        }

                        if (!(path.Contains("/items/program/", StringComparison.Ordinal)))
                        {
                            return false;
                        }

                        return true;
                    }

                    if (CheckCondition3())
                    {
                        json = (string) $"{json[..^1]},\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/documents-page2\"}}";
                    }
                }
                else
                {
                    json = "{\"id\":\"root\",\"folder\":{},\"parentReference\":{\"driveId\":\"drive\"}}";
                }

                response = new(missingSource ? HttpStatusCode.NotFound : HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"),
                };
            }
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private static byte[] Docx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.AddMainDocumentPart().Document =
                new Document(new Body(new Paragraph(new Run(new Text("fixture")))));
        }
        return stream.ToArray();
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

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            (string) $"schedulelib-microsoft-{Guid.NewGuid().ToString("N")}");

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
