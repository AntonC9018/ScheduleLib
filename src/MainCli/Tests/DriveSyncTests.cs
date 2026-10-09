using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Xunit;

public sealed class DriveSyncTests
{
    [Fact]
    public async Task PreviewAndApplyPreserveCaseInsensitiveMatchingDuplicatesAndDeletion()
    {
        using var temp = new Temp();
        var files = await temp.Artifacts("schedule.pdf", "new.ics", "schedulelib-manifest.json", ".schedulelib-output.lock", "notes.txt");
        using var fake = new Fake { Remote = [new("old", "obsolete.xlsx"), new("one", "SCHEDULE.PDF"), new("two", "schedule.pdf")] };
        var preview = await DriveSync.Run(fake, "Configured", files, false, default);
        Assert.Equal("actual-account", preview.Account);
        Assert.Equal("actual@example.test", preview.AccountEmail);
        Assert.Equal("actual-folder", preview.Folder.Id);
        Assert.Equal(new[] { "delete", "create", "update", "update" }, preview.Actions.Select(x => x.Action));
        Assert.Empty(fake.Writes);
        Assert.Empty(preview.Outcomes);
        fake.Remote = [new("changed", "obsolete.xlsx"), new("one", "SCHEDULE.PDF")];
        var applied = await DriveSync.Run(fake, "Configured", files, true, default, temp.Path);
        Assert.Equal(0, applied.ExitCode);
        Assert.Equal(new[] { "delete changed", "create new.ics", "update one" }, fake.Writes);
        Assert.All(applied.Outcomes, x => Assert.Equal("completed", x.State));
        Assert.Equal(2, fake.ListCalls);
    }

    [Theory]
    [InlineData("lost", 6, "uncertain")]
    [InlineData("cancel", 6, "uncertain")]
    [InlineData("reject", 5, "failed")]
    public async Task CreateFailureIsNeverRetriedAndRemainingActionsAreReported(string fail, int exit, string state)
    {
        using var temp = new Temp();
        var files = await temp.Artifacts("first.pdf", "second.ics");
        using var fake = new Fake { Fail = fail };
        var result = await DriveSync.Run(fake, "Configured", files, true, default, temp.Path);
        Assert.Equal(exit, result.ExitCode);
        Assert.Equal(state, result.Outcomes[0].State);
        Assert.Equal("not-attempted", result.Outcomes[1].State);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task RejectedDeleteAndUnauthorizedCreateCannotBecomeSuccess()
    {
        using var temp = new Temp();
        using var rejectedDelete = new Fake { Remote = [new("old", "old.txt")], FailDelete = true };
        var deletion = await DriveSync.Run(rejectedDelete, "Configured", await temp.Artifacts("a.pdf"), true, default, temp.Path);
        Assert.Equal(5, deletion.ExitCode);
        Assert.Equal("failed", deletion.Outcomes[0].State);
        Assert.Equal("not-attempted", deletion.Outcomes[1].State);
        Assert.Single(rejectedDelete.Writes);
        using var unauthorized = new Fake { Fail = "unauthorized" };
        var creation = await DriveSync.Run(unauthorized, "Configured", await temp.Artifacts("a.pdf"), true, default, temp.Path);
        Assert.Equal(4, creation.ExitCode);
        Assert.Equal("failed", Assert.Single(creation.Outcomes).State);
        Assert.Single(unauthorized.Writes);
    }

    [Fact]
    public async Task PartialDeletionThenLostCreatePreservesCompletedIdentity()
    {
        using var temp = new Temp();
        using var fake = new Fake { Remote = [new("removed", "old.txt")], Fail = "lost" };
        var result = await DriveSync.Run(fake, "Configured", await temp.Artifacts("a.pdf", "b.pdf"), true, default, temp.Path);
        Assert.Equal(6, result.ExitCode);
        Assert.Equal("removed", result.Outcomes[0].ResultFileId);
        Assert.Equal("completed", result.Outcomes[0].State);
        Assert.Equal("uncertain", result.Outcomes[1].State);
        Assert.Equal("not-attempted", result.Outcomes[2].State);
        Assert.Equal(2, fake.Writes.Count);
    }

    [Fact]
    public async Task CancellationBetweenActionsDoesNotStartNextMutation()
    {
        using var temp = new Temp();
        using var cancellation = new CancellationTokenSource();
        using var fake = new Fake { AfterWrite = cancellation.Cancel };
        var result = await DriveSync.Run(fake, "Configured", await temp.Artifacts("a.pdf", "b.pdf"), true, cancellation.Token, temp.Path);
        Assert.Equal(130, result.ExitCode);
        Assert.Equal("completed", result.Outcomes[0].State);
        Assert.Equal("not-attempted", result.Outcomes[1].State);
        Assert.Single(fake.Writes);
    }

    [Fact]
    public async Task AliasesLockActualAccountFolderBeforeListingAndReleaseOnFailure()
    {
        using var temp = new Temp();
        using var fake = new Fake();
        await using (var lease = await LocalFileLock.Acquire(DriveSync.LockPath(temp.Path, "actual-account", "actual-folder"), default))
        {
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => DriveSync.Run(fake, "Alias", [], true, default, temp.Path));
            Assert.Equal(0, fake.ListCalls);
            Assert.Empty(fake.Writes);
            Assert.Equal(0, (await DriveSync.Run(fake, "Alias", [], false, default, temp.Path)).ExitCode);
        }
        Assert.Equal(0, (await DriveSync.Run(fake, "Alias", [], true, default, temp.Path)).ExitCode);
    }

    [Fact]
    public async Task DestinationChangeUnderLockRefusesAllWrites()
    {
        using var temp = new Temp();
        using var fake = new Fake { ChangeFolder = true };
        await Assert.ThrowsAsync<IOException>(() => DriveSync.Run(fake, "Configured", [], true, default, temp.Path));
        Assert.Empty(fake.Writes);
        Assert.Equal(0, fake.ListCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommandBuildsRealBundleAndExcludesManifestsAndCallerFiles(bool apply)
    {
        using var temp = new Temp();
        await File.WriteAllTextAsync(System.IO.Path.Combine(temp.Path, "caller.pdf"), "untouched");
        using var fake = new Fake();
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(0, await new FixtureDrive(fake).Publish(new() { NoCache = true, DataDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Profile = "Curmanschii Anton" }, new() { Directory = temp.Path }, new() { Json = true }, new() { Apply = apply }));
            using var json = System.Text.Json.JsonDocument.Parse(text.ToString());
            Assert.Equal(apply ? "applied" : "preview", json.RootElement.GetProperty("status").GetString());
            var actions = json.RootElement.GetProperty("data").GetProperty("actions").EnumerateArray().ToArray();
            var names = actions.Select(x => x.GetProperty("name").GetString()).ToArray();
            Assert.Contains("all-teachers.xlsx", names);
            Assert.Contains("free-rooms.xlsx", names);
            Assert.Contains(names, x => x!.EndsWith(".pdf", StringComparison.Ordinal));
            Assert.Contains(names, x => x!.EndsWith(".ics", StringComparison.Ordinal));
            Assert.DoesNotContain("caller.pdf", names);
            Assert.DoesNotContain("schedulelib-manifest.json", names);
            Assert.DoesNotContain(".schedulelib-output.lock", names);
            foreach (var path in Directory.GetFiles(temp.Path, "*.xlsx"))
            {
                using var workbook = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false);
                Assert.NotEmpty(workbook.WorkbookPart!.WorksheetParts);
            }
            Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(Directory.GetFiles(temp.Path, "*.pdf").First(x => !x.EndsWith("caller.pdf", StringComparison.Ordinal)))));
            Assert.Contains("BEGIN:VEVENT", await File.ReadAllTextAsync(Directory.GetFiles(temp.Path, "*.ics")[0]));
            if (apply) Assert.Equal(actions.Length, fake.Writes.Count);
            else Assert.Empty(fake.Writes);
            Assert.Equal("untouched", await File.ReadAllTextAsync(System.IO.Path.Combine(temp.Path, "caller.pdf")));
        }
        finally { Console.SetOut(original); }
    }

    [Theory]
    [InlineData(new[] { "drive", "publish", "--help" }, 0)]
    [InlineData(new[] { "drive", "publish", "--unknown" }, 2)]
    public async Task HelpAndInvalidOptionsNeedNoInitialization(string[] args, int exit) => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task AuthenticationFailureIsExitFourWithOneJsonResult()
    {
        var original = Console.Out;
        using var text = new StringWriter();
        try
        {
            Console.SetOut(text);
            Assert.Equal(4, await new FixtureDrive(null).Publish(new() { NoCache = true, DataDirectory = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Profile = "Curmanschii Anton" }, new(), new() { Json = true }, new()));
            using var json = System.Text.Json.JsonDocument.Parse(text.ToString());
            Assert.Equal(4, json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Empty(json.RootElement.GetProperty("outputs").EnumerateArray());
        }
        finally { Console.SetOut(original); }
    }

    internal sealed class FixtureDrive(IDriveSyncProvider? fake) : DriveCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
        protected override Task<IDriveSyncProvider> Connect(IServiceProvider services, BuiltGoogleDriveConfig config, CancellationToken token)
            => fake is null ? throw new AuthenticationRequiredException("google", "Curmanschii Anton") : Task.FromResult<IDriveSyncProvider>(fake);
    }

    private sealed class Fake : IDriveSyncProvider
    {
        public IReadOnlyList<DriveRemoteFile> Remote { get; set; } = [];
        public List<string> Writes { get; } = [];
        public string? Fail { get; init; }
        public bool FailDelete { get; init; }
        public Action? AfterWrite { get; init; }
        public bool ChangeFolder { get; init; }
        public int ListCalls { get; private set; }
        private int _folderCalls;
        public Task<DriveAccount> GetAccount(CancellationToken token) => Task.FromResult(new DriveAccount("actual-account", "actual@example.test"));
        public Task<DriveDestination> FindFolder(string name, CancellationToken token)
            => Task.FromResult(new DriveDestination(ChangeFolder && ++_folderCalls > 1 ? "changed-folder" : "actual-folder", "Actual folder"));
        public Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string id, CancellationToken token) { ListCalls++; return Task.FromResult(Remote); }
        public Task Delete(string id, CancellationToken token)
        {
            Writes.Add("delete " + id);
            if (FailDelete) throw new Google.GoogleApiException("drive", "Rejected") { HttpStatusCode = System.Net.HttpStatusCode.Forbidden };
            AfterWrite?.Invoke();
            return Task.CompletedTask;
        }
        public Task<string> Create(string folder, string name, Stream input, CancellationToken token)
        {
            Assert.True(input.Length > 0);
            Writes.Add("create " + name);
            if (Fail == "lost") throw new IOException("Lost response");
            if (Fail == "cancel") throw new OperationCanceledException();
            if (Fail == "unauthorized") throw new Google.GoogleApiException("drive", "Unauthorized") { HttpStatusCode = System.Net.HttpStatusCode.Unauthorized };
            if (Fail == "reject") throw new Google.GoogleApiException("drive", "Rejected") { HttpStatusCode = System.Net.HttpStatusCode.BadRequest };
            AfterWrite?.Invoke();
            return Task.FromResult("created");
        }
        public Task<string> Update(string id, Stream input, CancellationToken token) { Assert.True(input.Length > 0); Writes.Add("update " + id); return Task.FromResult(id); }
        public void Dispose() { }
    }

    private sealed class Temp : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public Temp() => Directory.CreateDirectory(Path);
        public async Task<DriveArtifact[]> Artifacts(params string[] names)
        {
            foreach (var name in names) await File.WriteAllTextAsync(System.IO.Path.Combine(Path, name), "fixture bytes");
            return names.Select(x => new DriveArtifact(x, System.IO.Path.Combine(Path, x))).ToArray();
        }
        public void Dispose() => Directory.Delete(Path, true);
    }
}
