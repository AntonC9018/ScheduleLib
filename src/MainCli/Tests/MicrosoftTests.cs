using System.Net;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Graph;
using File = System.IO.File;
using Directory = System.IO.Directory;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Curriculum.Download;
using Xunit;

public sealed class MicrosoftTests
{
    private const string Teacher = "Curmanschii Anton";
    private static MicrosoftAuthConfig Config => new() { TenantId = "tenant", ClientId = "client" };

    [Fact]
    public async Task OrdinaryAuthenticationIsSilentAndHasExactLoginGuidance()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        var error = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(Config, Teacher, default));
        Assert.Equal("schedulelib auth login microsoft --profile \"Curmanschii Anton\"", error.LoginCommand);
        Assert.Equal(0, tokens.Consents);
        Assert.Equal(0, tokens.SilentCalls);
        await authentication.Login(Config, Teacher, default);
        Assert.Equal("access-secret", await authentication.Resolve(Config, Teacher, default));
        Assert.Equal(1, tokens.Consents);
        Assert.Equal(1, tokens.SilentCalls);
        tokens.FailSilent = true;
        var failed = await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(Config, Teacher, default));
        Assert.DoesNotContain("access-secret", failed.ToString());
        Assert.Equal(1, tokens.Consents);
        Assert.Single((await authentication.Status(Teacher, default)).Accounts);
    }

    [Fact]
    public async Task AccountPartitionsCancellationAndLogoutPreserveOtherTeachers()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        await authentication.Login(Config, Teacher, default);
        tokens.AccountId = "second-account";
        await authentication.Login(Config, Teacher, default);
        await authentication.Login(Config, "Nartea Nichita", default);
        tokens.CancelConsent = true;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => authentication.Login(Config, Teacher, default));
        Assert.Equal("second-account", Assert.Single((await authentication.Status(Teacher, default)).Accounts).AccountId);
        var files = Directory.GetFiles(temp.Path, "*.json", SearchOption.AllDirectories);
        Assert.True(files.Length >= 5);
        if (!OperatingSystem.IsWindows())
            foreach (var file in files) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
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
        await authentication.Login(Config, Teacher, default);
        var directory = Assert.Single(Directory.GetDirectories(temp.Path));
        await using var lease = await LocalFileLock.Acquire(System.IO.Path.Combine(directory, ".operation.lock"), default);
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => authentication.Resolve(Config, Teacher, default));
        await Assert.ThrowsAsync<LocalOperationBusyException>(() => authentication.Logout(Teacher, default));
    }

    [Fact]
    public async Task MicrosoftCommandRouteUsesProviderAndEmitsRedactedStatus()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var commands = new FakeAuth(new MicrosoftAuthentication(temp.Path, tokens));
        using var capture = new Capture();
        Assert.Equal(0, await commands.Login(new() { Provider = AuthProvider.microsoft }, new() { Profile = Teacher }, new() { Json = true }));
        var result = capture.Result();
        Assert.Equal("auth login microsoft", result.GetProperty("command").GetString());
        Assert.DoesNotContain("access-secret", result.GetRawText());
        Assert.DoesNotContain("cache-secret", result.GetRawText());
        Assert.Equal(1, tokens.Consents);
    }

    [Theory]
    [InlineData(new[] { "auth", "status", "microsoft", "--cache-dir", "bad" }, 2)]
    [InlineData(new[] { "auth", "login", "microsoft", "--output", "bad" }, 2)]
    [InlineData(new[] { "curricula", "download", "--no-cache" }, 2)]
    [InlineData(new[] { "curricula", "download", "--apply" }, 2)]
    [InlineData(new[] { "auth", "login", "microsoft", "--help" }, 0)]
    [InlineData(new[] { "curricula", "download", "--help" }, 0)]
    public async Task HelpAndArgumentModelsStayIndependentOfSchedule(string[] args, int exit) => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task GraphDocxDownloadUsesOwnedOutputsAndDisclosesCodedSource()
    {
        using var temp = new TempDirectory();
        var unrelated = System.IO.Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(unrelated, "caller data");
        using var capture = new Capture();
        using var commands = new FakeGraphCommands();
        Assert.Equal(0, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new() { Json = true }));
        var result = capture.Result();
        Assert.Equal(CurriculaDownloadTasks.ConfiguredSource.Owner, result.GetProperty("data").GetProperty("source").GetProperty("owner").GetString());
        Assert.Equal(1, result.GetProperty("data").GetProperty("documentCount").GetInt32());
        var documentPath = Assert.Single(Directory.GetFiles(temp.Path, "*.docx"));
        using var document = WordprocessingDocument.Open(documentPath, false);
        Assert.Equal("fixture", document.MainDocumentPart!.Document.Body!.InnerText);
        Assert.Equal("caller data", await File.ReadAllTextAsync(unrelated));
        Assert.True(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal(1, commands.Handler.ContentRequests);
    }

    [Fact]
    public async Task LinuxLegacyPrerequisiteDoesNotDownloadOrModifyOriginals()
    {
        if (OperatingSystem.IsWindows()) return;
        using var temp = new TempDirectory();
        var original = System.IO.Path.Combine(temp.Path, "caller.doc");
        await File.WriteAllTextAsync(original, "original");
        using var commands = new FakeGraphCommands("legacy.doc");
        Assert.Equal(8, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Equal(0, commands.Handler.ContentRequests);
        Assert.Equal("original", await File.ReadAllTextAsync(original));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
    }

    [Fact]
    public async Task LegacyConverterReceivesStagedCopyAndOriginalSurvivesItsDeletion()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeConversionCommands();
        Assert.Equal(0, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Equal("original-doc", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(temp.Path, "*.doc"))));
        Assert.Single(Directory.GetFiles(temp.Path, "*.docx"));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".conversion-*"));
    }

    [Fact]
    public async Task MissingOrStaleSourceAndMissingAuthenticationHaveDistinctPrerequisites()
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        using var stale = new FakeGraphCommands(missingSource: true);
        Assert.Equal(3, await stale.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new() { Json = true }));
        Assert.Contains("2024-2025", capture.Result().GetProperty("errors")[0].GetString());
        var missing = new MissingAuthenticationCommands();
        Assert.Equal(4, await missing.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
    }

    [Fact]
    public async Task CancelledDownloadDoesNotPublishPartialDocument()
    {
        using var temp = new TempDirectory();
        using var cancellation = new CancellationTokenSource();
        using var commands = new FakeGraphCommands(cancelDownload: true);
        commands.Handler.Cancellation = cancellation;
        Assert.Equal(130, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new(), cancellation.Token));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.docx"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Fact]
    public async Task ProviderTimeoutIsAnOperationFailureWithoutCommandCancellation()
    {
        using var temp = new TempDirectory();
        using var timeout = new FakeGraphCommands(cancelDownload: true);
        Assert.Equal(5, await timeout.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
    }

    [Fact]
    public async Task StatusAndLogoutNeverResolveMicrosoftConfigurationOrConsent()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var commands = new FakeAuth(new MicrosoftAuthentication(temp.Path, tokens), forbidConfig: true);
        Assert.Equal(0, await commands.Status(new() { Provider = AuthProvider.microsoft }, new() { Profile = Teacher }, new()));
        Assert.Equal(0, await commands.Logout(new() { Provider = AuthProvider.microsoft }, new() { Profile = Teacher }, new()));
        Assert.Equal(0, tokens.Consents);
        Assert.Equal(0, tokens.SilentCalls);
    }

    [Fact]
    public async Task DifferentClientCannotUseProvisionedAccount()
    {
        using var temp = new TempDirectory();
        var tokens = new FakeTokens();
        var authentication = new MicrosoftAuthentication(temp.Path, tokens);
        await authentication.Login(Config, Teacher, default);
        await Assert.ThrowsAsync<AuthenticationRequiredException>(() => authentication.Resolve(
            new() { TenantId = Config.TenantId, ClientId = "other-client" }, Teacher, default));
        Assert.Equal(0, tokens.SilentCalls);
    }

    [Fact]
    public async Task GraphPaginationIncludesAllProgramsAndDocumentsAndOnlyReads()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(paginate: true);
        Assert.Equal(0, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Equal(3, commands.Handler.ContentRequests);
        Assert.Equal(3, Directory.GetFiles(temp.Path, "*.docx").Length);
        Assert.Equal(2, commands.Handler.PageRequests);
    }

    [Fact]
    public async Task UnrelatedCollisionPreservesCallerDocument()
    {
        using var temp = new TempDirectory();
        var path = System.IO.Path.Combine(temp.Path, "group - course.docx");
        await File.WriteAllTextAsync(path, "caller document");
        using var commands = new FakeGraphCommands();
        Assert.Equal(5, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Equal("caller document", await File.ReadAllTextAsync(path));
        Assert.Equal(0, commands.Handler.ContentRequests);
    }

    [Fact]
    public async Task CorruptDocxIsRejectedBeforePublication()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(invalidDocx: true);
        Assert.Equal(5, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.docx"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversionFailureKeepsOriginalAndReportsPartialManifest(bool missingWord)
    {
        using var temp = new TempDirectory();
        using var capture = new Capture();
        using var commands = new FailedConversionCommands(missingWord);
        Assert.Equal(6, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new() { Json = true }));
        var result = capture.Result();
        Assert.Equal("partial", result.GetProperty("status").GetString());
        Assert.Equal("original-doc", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(temp.Path, "*.doc"))));
        Assert.Empty(Directory.GetDirectories(temp.Path, ".conversion-*"));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("Status").GetString());
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
        Assert.Equal(130, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new() { Json = true }, cancellation.Token));
        Assert.Single(Directory.GetFiles(temp.Path, "*.docx"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
        var result = capture.Result();
        Assert.Equal("cancelled", result.GetProperty("status").GetString());
        Assert.Equal(2, result.GetProperty("outputs").GetArrayLength());
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("Status").GetString());
    }

    [Fact]
    public async Task LaterCorruptDocumentReportsPartialOutputInsteadOfSuccess()
    {
        using var temp = new TempDirectory();
        using var commands = new FakeGraphCommands(paginate: true, invalidDocx: true);
        Assert.Equal(6, await commands.Download(new() { Profile = Teacher }, new() { Directory = temp.Path }, new()));
        Assert.Single(Directory.GetFiles(temp.Path, "*.docx"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.tmp"));
    }

    private sealed class FakeTokens : IMicrosoftTokenProvider
    {
        public int Consents;
        public int SilentCalls;
        public bool FailSilent;
        public bool CancelConsent;
        public string AccountId = "first-account";
        private MicrosoftToken Value() => new(AccountId, "access-secret", DateTimeOffset.UtcNow.AddHours(1), CurriculaDownloadTasks.ApiRequiredScopes, Encoding.UTF8.GetBytes("cache-secret"));
        public Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token)
        {
            Consents++;
            if (CancelConsent) throw new OperationCanceledException();
            return Task.FromResult(Value());
        }
        public Task<MicrosoftToken> Silent(MicrosoftAuthConfig config, string accountId, byte[] cache, CancellationToken token)
        {
            SilentCalls++;
            if (FailSilent) throw new Exception("provider exposed access-secret");
            Assert.Equal("cache-secret", Encoding.UTF8.GetString(cache));
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
    private class FakeGraphCommands(string fileName = "course.docx", bool missingSource = false, bool cancelDownload = false, bool paginate = false, bool invalidDocx = false) : CurriculaCommands, IDisposable
    {
        public void Dispose() => Handler.Dispose();
        public FakeGraphHandler Handler = new(fileName, missingSource, cancelDownload, paginate, invalidDocx);
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned fixture adapter owns the HttpProvider.")]
        protected override Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token)
        {
            var http = new HttpProvider(Handler, disposeHandler: true, serializer: null);
            return Task.FromResult<ICurriculaProvider>(new GraphCurriculaProvider(new GraphServiceClient(new DelegateAuthenticationProvider(_ => Task.CompletedTask), http), http));
        }
    }
    private sealed class MissingAuthenticationCommands : CurriculaCommands
    {
        protected override Task<ICurriculaProvider> CreateProvider(string profile, CancellationToken token) => throw new AuthenticationRequiredException("microsoft", profile);
    }
    private sealed class FakeConversionCommands() : FakeGraphCommands("legacy.doc")
    {
        protected override void EnsureLegacyCapability() { }
        protected override async Task<bool> ConvertLegacy(string input, string output, CancellationToken token)
        {
            Assert.Equal("original-doc", await File.ReadAllTextAsync(input, token));
            File.Delete(input); // Reproduce the legacy converter's destructive behavior on the staged copy.
            await File.WriteAllBytesAsync(output, Docx(), token);
            return true;
        }
    }
    private sealed class FailedConversionCommands(bool missingWord) : FakeGraphCommands("legacy.doc")
    {
        protected override void EnsureLegacyCapability() { }
        protected override Task<bool> ConvertLegacy(string input, string output, CancellationToken token)
        {
            File.Delete(input);
            if (missingWord) throw new PlatformNotSupportedException("Microsoft Word is required.");
            return Task.FromResult(false);
        }
    }
    private sealed class FakeGraphHandler(string fileName, bool missingSource, bool cancelDownload, bool paginate, bool invalidDocx) : HttpMessageHandler
    {
        public int ContentRequests;
        public int PageRequests;
        public CancellationTokenSource? Cancellation;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response;
            if (path.EndsWith("/content", StringComparison.Ordinal))
            {
                ContentRequests++;
                if (cancelDownload && (!paginate || ContentRequests == 2)) { Cancellation?.Cancel(); throw new OperationCanceledException(cancellationToken); }
                response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(fileName.EndsWith(".doc", StringComparison.Ordinal) ? Encoding.UTF8.GetBytes("original-doc") : invalidDocx && (!paginate || ContentRequests == 2) ? Encoding.UTF8.GetBytes("not-docx") : Docx()) };
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
                    json = "{\"value\":[{\"id\":\"program\",\"name\":\"group\",\"folder\":{}}]"
                        + (paginate ? ",\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/programs-page2\"}" : "}");
                else if (path.Contains("/items/program", StringComparison.Ordinal))
                {
                    json = JsonSerializer.Serialize(new { value = new[] { new { id = "file", name = fileName, file = new { mimeType = "application/octet-stream" } } } });
                    if (paginate && path.Contains("/items/program/", StringComparison.Ordinal))
                        json = json[..^1] + ",\"@odata.nextLink\":\"https://graph.microsoft.com/v1.0/documents-page2\"}";
                }
                else json = "{\"id\":\"root\",\"folder\":{},\"parentReference\":{\"driveId\":\"drive\"}}";
                response = new(missingSource ? HttpStatusCode.NotFound : HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
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
            document.AddMainDocumentPart().Document = new Document(new Body(new Paragraph(new Run(new Text("fixture")))));
        }
        return stream.ToArray();
    }
    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _text = new();
        public Capture() => Console.SetOut(_text);
        public JsonElement Result() { using var doc = JsonDocument.Parse(_text.ToString()); return doc.RootElement.Clone(); }
        public void Dispose() { Console.SetOut(_original); _text.Dispose(); }
    }
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "schedulelib-microsoft-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
