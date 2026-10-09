using System.Diagnostics;
using System.Text.Json;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using Xunit;

public sealed class SettingsWriteTests
{
    [Fact]
    public async Task FreshProcessesSetOnlySelectedScopeAndUnsetRestoresInheritance()
    {
        using var files = new Fixture();
        await files.User("""{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"inherited"}}}""");
        var userBefore = await File.ReadAllTextAsync(files.UserFile);
        await files.Success("set", "GoogleCalendarConfig.calendarName", "\"project\"", "--scope", "project");
        Assert.Equal("project", await files.Get("GoogleCalendarConfig.calendarName"));
        Assert.Equal(userBefore, await File.ReadAllTextAsync(files.UserFile));
        await files.Success("set", "DeadlinesExcelConfig.lessonDelayLimit", "9", "--scope", "user");
        Assert.Equal("9", await files.Get("DeadlinesExcelConfig.lessonDelayLimit"));
        await files.Success("set", "GoogleCalendarConfig.calendarName", "\"teacher\"", "--scope", "project", "--profile", "Curmanschii Anton");
        Assert.Equal("teacher", await files.Get("GoogleCalendarConfig.calendarName", "--profile", "Curmanschii Anton"));
        Assert.Equal("project", await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success("unset", "GoogleCalendarConfig.calendarName", "--scope", "project");
        Assert.Equal("inherited", await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success("unset", "GoogleCalendarConfig.calendarName", "--scope", "project", "--profile", "Curmanschii Anton");
        Assert.NotEqual("teacher", await files.Get("GoogleCalendarConfig.calendarName", "--profile", "Curmanschii Anton"));
        await files.Success("set", "GoogleCalendarConfig.credentials.credentialsPath", "\"tokens\"", "--scope", "project");
        Assert.Equal(Path.Combine(files.ProjectDirectory, "tokens"), await files.Get("GoogleCalendarConfig.credentials.credentialsPath"));
    }

    [Theory]
    [InlineData("GoogleCalendarConfig.calendarName", "123")]
    [InlineData("GoogleCalendarConfig.unknown", "null")]
    [InlineData("StudyYearOptions", "{}")]
    [InlineData("MoodleConfig", "{\"credentials\":{\"$type\":\"ValueCredentialsSource\",\"value\":{\"password\":\"sensitive\"}}}")]
    [InlineData("GoogleCalendarConfig", "{\"calendarName\":\"first\",\"calendarName\":\"second\"}")]
    [InlineData("GoogleCalendarConfig", "{\"credentials\":{\"saveCredentials\":\"wrong\"}}")]
    public async Task RejectedWritesPreserveExactPreviousDocument(string key, string value)
    {
        using var files = new Fixture();
        const string original = """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"valid"}}}""";
        await File.WriteAllTextAsync(files.ProjectFile, original);
        var result = await files.Run("set", key, value, "--scope", "project");
        Assert.Equal(3, result.Exit);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain("sensitive", result.Output + result.Error);
        Assert.Equal(original, await File.ReadAllTextAsync(files.ProjectFile));
        Assert.False(File.Exists(files.ProjectFile + ".lock"));
    }

    [Fact]
    public async Task MissingScopeAndUnknownProfileFailWithoutCreatingSettings()
    {
        using var files = new Fixture();
        Assert.Equal(2, (await files.Run("set", "GoogleCalendarConfig.calendarName", "\"x\"")).Exit);
        Assert.Equal(3, (await files.Run("unset", "GoogleCalendarConfig.calendarName", "--scope", "project", "--profile", "Unknown")).Exit);
        Assert.False(File.Exists(files.ProjectFile));
        await files.Success("unset", "GoogleCalendarConfig.calendarName", "--scope", "project");
        Assert.False(File.Exists(files.ProjectFile));
        Assert.NotEmpty(await files.Get("GoogleCalendarConfig.calendarName"));
    }

    [Fact]
    public async Task TwoProcessesWaitForEditorLeaseAndRetainBothEdits()
    {
        using var files = new Fixture();
        await using var lease = await LocalFileLock.Acquire(files.ProjectFile + ".lock", CancellationToken.None);
        using var first = files.Start("set", "GoogleCalendarConfig.calendarName", "\"concurrent\"", "--scope", "project");
        using var second = files.Start("set", "DeadlinesExcelConfig.lessonDelayLimit", "11", "--scope", "project");
        await Task.Delay(500);
        Assert.False(first.HasExited);
        Assert.False(second.HasExited);
        await lease.DisposeAsync();
        var results = await Task.WhenAll(Fixture.Finish(first), Fixture.Finish(second));
        Assert.All(results, x => Assert.Equal(0, x.Exit));
        Assert.Equal("concurrent", await files.Get("GoogleCalendarConfig.calendarName"));
        Assert.Equal("11", await files.Get("DeadlinesExcelConfig.lessonDelayLimit"));
    }

    [Fact]
    public async Task CancelledWaitingEditorAndFailedPublicationPreserveSettings()
    {
        using var files = new Fixture();
        const string original = """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"valid"}}}""";
        await File.WriteAllTextAsync(files.ProjectFile, original);
        await using (var lease = await LocalFileLock.Acquire(files.ProjectFile + ".lock", CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource();
            var edit = CliSettings.Edit(new() { Project = files.ProjectDirectory }, SettingsScope.Project,
                "GoogleCalendarConfig.calendarName", "\"cancelled\"", false, cancellation.Token, files.ProjectDirectory, files.UserFile);
            await Task.Delay(200);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => edit);
        }
        Assert.Equal(original, await File.ReadAllTextAsync(files.ProjectFile));
        await Assert.ThrowsAsync<IOException>(() => AtomicFile.Publish(files.ProjectFile, async (stream, token) =>
        {
            await stream.WriteAsync(new byte[] { 1, 2, 3 }, token);
            throw new IOException("fixture write failure");
        }, CancellationToken.None));
        Assert.Equal(original, await File.ReadAllTextAsync(files.ProjectFile));
        Assert.Empty(Directory.GetFiles(files.ProjectDirectory, "*.tmp"));
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "schedulelib-writes-" + Guid.NewGuid().ToString("N"));
        public string ProjectDirectory => Path.Combine(Root, "project");
        public string ProjectFile => Path.Combine(ProjectDirectory, CliSettings.FileName);
        public string UserFile => Path.Combine(Root, "user", "ScheduleLib", CliSettings.FileName);
        public Fixture() { Directory.CreateDirectory(ProjectDirectory); Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!); }
        public Task User(string json) => File.WriteAllTextAsync(UserFile, json);
        public Process Start(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = ProjectDirectory, RedirectStandardOutput = true, RedirectStandardError = true };
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(Root, "user");
            start.Environment["APPDATA"] = Path.Combine(Root, "user");
            start.ArgumentList.Add(typeof(CliHost).Assembly.Location);
            start.ArgumentList.Add("config");
            foreach (var arg in args) start.ArgumentList.Add(arg);
            start.ArgumentList.Add("--project"); start.ArgumentList.Add(ProjectDirectory); start.ArgumentList.Add("--json");
            return Process.Start(start)!;
        }
        public async Task<(int Exit, string Output, string Error)> Run(params string[] args)
        { using var process = Start(args); return await Finish(process); }
        public static async Task<(int Exit, string Output, string Error)> Finish(Process process)
        {
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }
        public async Task Success(params string[] args)
        {
            var result = await Run(args); Assert.True(result.Exit == 0, result.Output + result.Error); Assert.Empty(result.Error);
            using var document = JsonDocument.Parse(result.Output); Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        }
        public async Task<string> Get(string key, params string[] args)
        {
            var result = await Run(new[] { "get", key }.Concat(args).ToArray()); Assert.True(result.Exit == 0, result.Output + result.Error);
            using var document = JsonDocument.Parse(result.Output); return document.RootElement.GetProperty("data").GetProperty("value").ToString();
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
