using System.Diagnostics;
using System.Text.Json;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;

public sealed class SettingsWriteTests
{
    [Fact]
    public async Task LabSubGroupSettingWritesGloballyAndRejectsProfileWritesAtomically()
    {
        using var files = new Fixture();
        await files.Success(
            "set",
            "ScheduleDerivationConfig.autoMergeLabSubGroups",
            "true",
            "--scope",
            "user");
        Assert.True(
            bool.Parse(
                await files.Get(
                    "ScheduleDerivationConfig.autoMergeLabSubGroups",
                    "--profile",
                    "Curmanschii Anton")));
        var original = await File.ReadAllTextAsync(files.UserFile);
        File.Delete((string) $"{files.UserFile}.lock");
        var result = await files.Run(
            "set",
            "ScheduleDerivationConfig.autoMergeLabSubGroups",
            "false",
            "--scope",
            "user",
            "--profile",
            "Curmanschii Anton");
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Contains(expectedSubstring: "global", actualString: result.Error);
        Assert.Equal(expected: original, actual: await File.ReadAllTextAsync(files.UserFile));
        Assert.False(File.Exists((string) $"{files.UserFile}.lock"));
    }

    [Fact]
    public async Task RegistryCourseAliasesAreScopedToSelectedTeacher()
    {
        using var files = new Fixture();
        const string aliases = """{"Registry course":"Schedule course"}""";
        await files.Success(
            "set",
            "RegistryConfig.courseNames",
            aliases,
            "--scope",
            "user",
            "--profile",
            "Visnevschi Vlada");
        using var configured = JsonDocument.Parse(
            await files.Get(
                "RegistryConfig.courseNames",
                "--profile",
                "Visnevschi Vlada"));
        Assert.Equal(expected: "Schedule course", actual: configured.RootElement.GetProperty("Registry course").GetString());
        Assert.Empty(
            await files.Get(
                "RegistryConfig.courseNames",
                "--profile",
                "Curmanschii Anton"));
    }

    [Fact]
    public async Task FreshProcessesSetOnlySelectedScopeAndUnsetRestoresInheritance()
    {
        using var files = new Fixture();
        await files.User("""{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"inherited"}}}""");
        var userBefore = await File.ReadAllTextAsync(files.UserFile);
        await files.Success(
            "set",
            "GoogleCalendarConfig.calendarName",
            "\"project\"",
            "--scope",
            "project");
        Assert.Equal(expected: "project", actual: await files.Get("GoogleCalendarConfig.calendarName"));
        Assert.Equal(expected: userBefore, actual: await File.ReadAllTextAsync(files.UserFile));
        await files.Success(
            "set",
            "DeadlinesExcelConfig.lessonDelayLimit",
            "9",
            "--scope",
            "user");
        Assert.Equal(expected: "9", actual: await files.Get("DeadlinesExcelConfig.lessonDelayLimit"));
        await files.Success(
            "set",
            "GoogleCalendarConfig.calendarName",
            "\"teacher\"",
            "--scope",
            "project",
            "--profile",
            "Curmanschii Anton");
        Assert.Equal(
            expected: "teacher",
            actual: await files.Get(
                "GoogleCalendarConfig.calendarName",
                "--profile",
                "Curmanschii Anton"));
        Assert.Equal(expected: "project", actual: await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success(
            "unset",
            "GoogleCalendarConfig.calendarName",
            "--scope",
            "project");
        Assert.Equal(expected: "inherited", actual: await files.Get("GoogleCalendarConfig.calendarName"));
        await files.Success(
            "unset",
            "GoogleCalendarConfig.calendarName",
            "--scope",
            "project",
            "--profile",
            "Curmanschii Anton");
        Assert.NotEqual(
            expected: "teacher",
            actual: await files.Get(
                "GoogleCalendarConfig.calendarName",
                "--profile",
                "Curmanschii Anton"));
        await files.Success(
            "set",
            "GoogleCalendarConfig.credentials.credentialsPath",
            "\"tokens\"",
            "--scope",
            "project");
        Assert.Equal(
            expected: Path.Combine(files.ProjectDirectory, "tokens"),
            actual: await files.Get("GoogleCalendarConfig.credentials.credentialsPath"));
    }

    [Fact]
    public async Task AttendanceSourceLayoutSettingsRoundTripThroughConfiguration()
    {
        using var files = new Fixture();
        const string sources =
            """[{"filePath":"attendance.xlsx","studentNameColumn":2,"headerFormat":"Date","dateMode":"ValidateAndBind","worksheetNames":{"group shorthand":"Synthetic course IA2601 (lab)"}}]""";
        await files.Success(
            "set",
            "LessonAttendanceConfig.sources",
            sources,
            "--scope",
            "project");
        var result = await files.Get("LessonAttendanceConfig.sources");
        using var document = JsonDocument.Parse(result);
        var source = document.RootElement[0];
        Assert.Equal(expected: 2, actual: source.GetProperty("studentNameColumn").GetInt32());
        Assert.Equal(expected: "Date", actual: source.GetProperty("headerFormat").GetString());
        Assert.Equal(expected: "ValidateAndBind", actual: source.GetProperty("dateMode").GetString());
        Assert.Equal(
            expected: "Synthetic course IA2601 (lab)",
            actual: source.GetProperty("worksheetNames").GetProperty("group shorthand").GetString());
    }

    [Fact]
    public async Task RegistryHttpDelaySupportsFractionalSecondsInSettings()
    {
        using var files = new Fixture();
        await files.Success(
            "set",
            "RegistryConfig.httpCallDelay",
            "\"00:00:00.5000000\"",
            "--scope",
            "project");
        Assert.Equal(expected: "00:00:00.5000000", actual: await files.Get("RegistryConfig.httpCallDelay"));
    }

    [Theory]
    [InlineData("GoogleCalendarConfig.calendarName", "123")]
    [InlineData("GoogleCalendarConfig.unknown", "null")]
    [InlineData("StudyYearOptions", "{}")]
    [InlineData(
        "MoodleConfig",
        "{\"credentials\":{\"$type\":\"ValueCredentialsSource\",\"value\":{\"password\":\"sensitive\"}}}")]
    [InlineData("GoogleCalendarConfig", "{\"calendarName\":\"first\",\"calendarName\":\"second\"}")]
    [InlineData("GoogleCalendarConfig", "{\"credentials\":{\"saveCredentials\":\"wrong\"}}")]
    public async Task RejectedWritesPreserveExactPreviousDocument(string key, string value)
    {
        using var files = new Fixture();
        const string original = """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"valid"}}}""";
        await File.WriteAllTextAsync(path: files.ProjectFile, contents: original);
        var result = await files.Run(
            "set",
            key,
            value,
            "--scope",
            "project");
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.NotEmpty(result.Error);
        Assert.DoesNotContain(expectedSubstring: "sensitive", actualString: (string) $"{result.Output}{result.Error}");
        Assert.Equal(expected: original, actual: await File.ReadAllTextAsync(files.ProjectFile));
        Assert.False(File.Exists((string) $"{files.ProjectFile}.lock"));
    }

    [Fact]
    public async Task MissingScopeAndUnknownProfileFailWithoutCreatingSettings()
    {
        using var files = new Fixture();
        Assert.Equal(
            expected: 2,
            actual: (await files.Run(
                "set",
                "GoogleCalendarConfig.calendarName",
                "\"x\"")).Exit);
        Assert.Equal(
            expected: 3,
            actual: (await files.Run(
                "unset",
                "GoogleCalendarConfig.calendarName",
                "--scope",
                "project",
                "--profile",
                "Unknown")).Exit);
        Assert.False(File.Exists(files.ProjectFile));
        await files.Success(
            "unset",
            "GoogleCalendarConfig.calendarName",
            "--scope",
            "project");
        Assert.False(File.Exists(files.ProjectFile));
        Assert.NotEmpty(await files.Get("GoogleCalendarConfig.calendarName"));
    }

    [Fact]
    public async Task TwoProcessesWaitForEditorLeaseAndRetainBothEdits()
    {
        using var files = new Fixture();
        await using var lease = await LocalFileLock.Acquire((string) $"{files.ProjectFile}.lock", CancellationToken.None);
        using var first = files.Start(
            "set",
            "GoogleCalendarConfig.calendarName",
            "\"concurrent\"",
            "--scope",
            "project");
        using var second = files.Start(
            "set",
            "DeadlinesExcelConfig.lessonDelayLimit",
            "11",
            "--scope",
            "project");
        await Task.Delay(500);
        Assert.False(first.HasExited);
        Assert.False(second.HasExited);
        await lease.DisposeAsync();
        var results = await Task.WhenAll(Fixture.Finish(first), Fixture.Finish(second));
        Assert.All(results, x => Assert.Equal(expected: 0, actual: x.Exit));
        Assert.Equal(expected: "concurrent", actual: await files.Get("GoogleCalendarConfig.calendarName"));
        Assert.Equal(expected: "11", actual: await files.Get("DeadlinesExcelConfig.lessonDelayLimit"));
    }

    [Fact]
    public async Task CancelledWaitingEditorAndFailedPublicationPreserveSettings()
    {
        using var files = new Fixture();
        const string original = """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"valid"}}}""";
        await File.WriteAllTextAsync(path: files.ProjectFile, contents: original);
        await using (var lease = await LocalFileLock.Acquire((string) $"{files.ProjectFile}.lock", CancellationToken.None))
        {
            using var cancellation = new CancellationTokenSource();
            var edit = CliSettings.Edit(
                new() { Project = files.ProjectDirectory },
                SettingsScope.Project,
                key: "GoogleCalendarConfig.calendarName",
                value: "\"cancelled\"",
                false,
                cancellation.Token,
                invocationDirectory: files.ProjectDirectory,
                userFile: files.UserFile);
            await Task.Delay(200);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => edit);
        }
        Assert.Equal(expected: original, actual: await File.ReadAllTextAsync(files.ProjectFile));
        await Assert.ThrowsAsync<IOException>(() => AtomicFile.Publish(
            files.ProjectFile,
            async (stream, token) =>
            {
                await stream.WriteAsync(
                    new byte[]
                    {
                        1,
                        2,
                        3,
                    },
                    token);
                throw new IOException("fixture write failure");
            },
            CancellationToken.None));
        Assert.Equal(expected: original, actual: await File.ReadAllTextAsync(files.ProjectFile));
        Assert.Empty(Directory.GetFiles(path: files.ProjectDirectory, searchPattern: "*.tmp"));
    }

    internal sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), (string) $"schedulelib-writes-{Guid.NewGuid().ToString("N")}");

        public string ProjectDirectory => Path.Combine(Root, "project");
        public string ProjectFile => Path.Combine(ProjectDirectory, CliSettings.FileName);

        public string UserFile
        {
            get
            {
                return Path.Combine(
                    Root,
                    "user",
                    "ScheduleLib",
                    CliSettings.FileName);
            }
        }

        public Fixture()
        {
            Directory.CreateDirectory(ProjectDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
        }

        public Task User(string json) => File.WriteAllTextAsync(path: UserFile, contents: json);

        public Process Start(params string[] args)
        {
            var start = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = ProjectDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(Root, "user");
            start.Environment["APPDATA"] = Path.Combine(Root, "user");
            start.ArgumentList.Add(typeof(CliHost).Assembly.Location);
            start.ArgumentList.Add("config");
            foreach (var arg in args)
            {
                start.ArgumentList.Add(arg);
            }

            start.ArgumentList.Add("--project");
            start.ArgumentList.Add(ProjectDirectory);
            start.ArgumentList.Add("--json");
            return Process.Start(start)!;
        }

        public async Task<(int Exit, string Output, string Error)> Run(params string[] args)
        {
            using var process = Start(args);
            return await Finish(process);
        }

        public static async Task<(int Exit, string Output, string Error)> Finish(Process process)
        {
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            return (process.ExitCode, await output, await error);
        }

        public async Task Success(params string[] args)
        {
            var result = await Run(args);
            Assert.True(result.Exit == 0, (string) $"{result.Output}{result.Error}");
            Assert.Empty(result.Error);
            using var document = JsonDocument.Parse(result.Output);
            Assert.Equal(expected: 1, actual: document.RootElement.GetProperty("schemaVersion").GetInt32());
        }

        public async Task<string> Get(string key, params string[] args)
        {
            var result = await Run(
                new[]
                {
                    "get",
                    key,
                }.Concat(args).ToArray());
            Assert.True(result.Exit == 0, (string) $"{result.Output}{result.Error}");
            using var document = JsonDocument.Parse(result.Output);
            return document.RootElement.GetProperty("data").GetProperty("value").ToString();
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
