using System.Diagnostics;
using System.Text.Json;
using CommandDotNet;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class SettingsTests
{
    [Fact]
    public async Task LayersResolveTypedValuesAndReachOperationalDataProvider()
    {
        using var files = new SettingsFixture();
        await files.User("""
            {"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"user","credentials":{"credentialsPath":"user-tokens"}}},
            "profiles":{"Curmanschii Anton":{"GoogleCalendarConfig":{"calendarName":"user-profile"}}}}
            """);
        await files.Project("""
            {"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"project"}},
            "profiles":{"Curmanschii Anton":{"GoogleCalendarConfig":{"calendarName":"project-profile"}}}}
            """);
        using var global = await files.Load(new());
        Assert.Equal("project", global.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(Path.Combine(files.Root, "user", "user-tokens"), global.Get(GoogleCalendarConfig.Key)!.Credentials!.CredentialsPath);
        using var teacher = await files.Load(new() { Profile = "Curmanschii Anton" });
        Assert.Equal("project-profile", teacher.Get(GoogleCalendarConfig.Key)!.CalendarName);
        using var cli = await files.Load(new() { Profile = "Curmanschii Anton" }, new() { CalendarName = "CLI" });
        Assert.Equal("CLI", cli.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(new[] { "code defaults", "user defaults", "project settings", "user profile: Curmanschii Anton", "project profile: Curmanschii Anton", "CLI arguments" },
            cli.Inspect("GoogleCalendarConfig.calendarName").Sources.Select(x => x.Name));
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        teacher.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal("project-profile", scope.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal("project-profile", scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>().Get()!.CalendarName);
        Assert.Equal("Curmanschii Anton", scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>().TeacherName.ToString());
        // Switching teacher scopes uses that teacher's path, rather than reusing the selected teacher's settings.
        await using var other = provider.CreateMarkerScope(x => x.TeacherName = ScheduleLib.Parsing.NameHelper.Parse("Nartea Nichita"));
        Assert.Null(other.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleDriveConfig.Key));
        Assert.Equal("project", other.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleCalendarConfig.Key)!.CalendarName);
    }

    [Fact]
    public async Task NullInheritanceKeyedMergingAndDefiningFilePaths()
    {
        using var files = new SettingsFixture();
        await files.User("""
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"lessonDelayLimit":8,"goodColor":"Blue"},
            "GoogleCalendarConfig":{"calendarName":"user"},"LessonAttendanceConfig":{"sources":[{"filePath":"shared.xlsx","cellValueFormat":"IgnoreGrade"}]}}}
            """);
        await files.Project("""
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"lessonDelayLimit":null},"GoogleCalendarConfig":{"calendarName":null},
            "LessonAttendanceConfig":{"sources":[{"filePath":"../user/shared.xlsx","repeatedCourseBehavior":"Error"},{"filePath":"new.xlsx"}]},
            "LessonTopicsConfig":{"sources":[{"$type":"ManifestLessonTopicSourceDefinition","path":"topics"}]}}}
            """);
        using var settings = await files.Load(new());
        Assert.Equal("user", settings.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(8, settings.Get(DeadlinesExcelConfig.Key)!.LessonDelayLimit);
        Assert.Equal(System.Drawing.Color.Blue, settings.Get(DeadlinesExcelConfig.Key)!.GoodColor);
        var attendance = settings.Get(LessonAttendanceConfig.Key)!;
        Assert.Equal(2, attendance.Sources.Count);
        var shared = Assert.Single(attendance.Sources.Where(x => x.FilePath == Path.Combine(files.Root, "user", "shared.xlsx")));
        Assert.Equal("IgnoreGrade", shared.CellValueFormat.ToString());
        Assert.Equal("Error", shared.RepeatedCourseBehavior.ToString());
        Assert.Contains(attendance.Sources, x => x.FilePath == Path.Combine(files.Root, "project", "new.xlsx"));
        Assert.Contains(settings.Get(LessonTopicsConfig.Key)!.Sources, x => x is ManifestLessonTopicSourceDefinition manifest && manifest.Path == Path.Combine(files.Root, "project", "topics"));
    }

    [Fact]
    public async Task NearestProjectAndExplicitDirectoryWithoutFilePreserveCwd()
    {
        using var files = new SettingsFixture();
        await files.Project("""{"schemaVersion":1}""");
        var nested = Path.Combine(files.Root, "project", "nested");
        Directory.CreateDirectory(Path.Combine(nested, "child"));
        await File.WriteAllTextAsync(Path.Combine(nested, CliSettings.FileName), """{"schemaVersion":1}""");
        var cwd = Environment.CurrentDirectory;
        using var nearest = await CliSettings.Load(new(), Path.Combine(nested, "child"), files.UserFile);
        Assert.Equal(nested, nearest.ProjectDirectory);
        using var explicitRoot = await CliSettings.Load(new() { Project = "../../user" }, Path.Combine(nested, "child"), files.UserFile);
        Assert.Equal(Path.Combine(files.Root, "project", "user"), explicitRoot.ProjectDirectory);
        Assert.Equal(cwd, Environment.CurrentDirectory);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"RegistryConfig\":{\"commandProcessingConfig\":{\"unknown\":[]}}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"LessonTopicsConfig\":{\"fallbackProviders\":[{\"lessonType\":\"Lab\",\"provider\":{}}]}}}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"RegistryLessonFilterConfig\":{\"skipAttendance\":999}}}")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"StudyYearOptions\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"GoogleCalendarConfig\":{\"calendarNmae\":null}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"GoogleCalendarConfig\":{\"calendarName\":10}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"TeacherLayerConfig\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":{\"Other teacher\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":{\"Nartea Nichita\":{\"GoogleCalendarConfig\":{\"unknown\":true}}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"MoodleConfig\":{\"credentials\":{\"$type\":\"ValueCredentialsSource\",\"value\":{\"password\":\"secret\"}}}}}")]
    public async Task InvalidSettingsFailBeforeExecution(string document)
    {
        using var files = new SettingsFixture();
        await files.Project(document);
        await Assert.ThrowsAsync<JsonException>(() => files.Load(new()));
    }

    [Fact]
    public async Task CodeProfileSettingsOverrideProjectDefaults()
    {
        using var files = new SettingsFixture();
        await files.Project("""{"schemaVersion":1,"defaults":{"RegistryLessonFilterConfig":{"skipAttendance":"Zi"}}}""");
        using var global = await files.Load(new());
        Assert.Equal("Zi", global.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance.ToString());
        using var teacher = await files.Load(new() { Profile = "Iatasina Tamara" });
        Assert.Equal("FrecventaRedusa", teacher.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance.ToString());
    }

    [Fact]
    public async Task InspectionRedactsExistingCodeSecrets()
    {
        using var files = new SettingsFixture();
        using var settings = await files.Load(new());
        settings.Tree.Defaults.Moodle().ConfigureValue(x => x.Credentials = new ScheduleLib.Scraping.Common.Config.ValueCredentialsSource
        {
            Value = new() { Login = "user", Password = "never-print-this" },
        });
        var inspected = settings.Inspect("MoodleConfig").Value!.ToJsonString();
        Assert.DoesNotContain("never-print-this", inspected);
        Assert.Contains("[redacted]", inspected);
    }

    [Fact]
    public async Task DesktopSerializerContractRemainsUnchanged()
    {
        using var files = new SettingsFixture();
        using var settings = await files.Load(new());
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        using var provider = services.BuildServiceProvider();
        var json = provider.GetRequiredService<IOptionsMonitor<JsonSerializerOptions>>().Get(TreeSerializer.ServiceKey);
        Assert.Null(json.PropertyNamingPolicy);
        Assert.Equal(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip, json.UnmappedMemberHandling);
        var tree = provider.GetRequiredService<TreeBuilder>();
        tree.Defaults.GoogleCalendar().ConfigureValue(x => x.CalendarName = "desktop");
        using var output = new MemoryStream();
        await provider.GetRequiredService<TreeSerializer>().SerializeValues([tree.BaseNode], output);
        using var document = JsonDocument.Parse(output.ToArray());
        Assert.Equal("desktop", document.RootElement[0].GetProperty("GoogleCalendarConfig").GetProperty("CalendarName").GetString());
        Assert.True(document.RootElement[0].TryGetProperty("$LayerName", out _));
    }

    [Fact]
    public async Task FreshProcessConfigIsCleanJsonWithoutScheduleData()
    {
        using var files = new SettingsFixture();
        await files.Project("""{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"fresh-process"}}}""");
        var assembly = typeof(CliHost).Assembly.Location;
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(files.Root, "project"), RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(assembly);
        foreach (var argument in new[] { "config", "get", "GoogleCalendarConfig.calendarName", "--project", ".", "--calendar-name", "CLI-process", "--json" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("", await stderr);
        using var document = JsonDocument.Parse(await stdout);
        Assert.Equal("CLI-process", document.RootElement.GetProperty("data").GetProperty("value").GetString());
        Assert.Equal("config get", document.RootElement.GetProperty("command").GetString());
        Assert.Equal(new[] { CliSettings.FileName }, Directory.GetFiles(Path.Combine(files.Root, "project")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(new[] { "config", "get", "--json" }, 2)]
    [InlineData(new[] { "query", "lessons", "--calendar-name", "irrelevant", "--json" }, 2)]
    public async Task MissingAndIrrelevantArgumentsProduceVersionedJsonErrors(string[] arguments, int expected)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(expected, await CliHost.Run(arguments));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task UnexpectedFailureBeforeInvocationRetainsExitOne()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(1, await CliHost.RunCore(["config", "profiles", "--json"], runner =>
                runner.Configure(builder => builder.UseMiddleware((_, _) => throw new InvalidOperationException("startup failure"),
                    CommandDotNet.Execution.MiddlewareSteps.InvokeCommand - 2))));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task ExportUsesExplicitProjectRootForItsDefaultOutput()
    {
        using var files = new SettingsFixture();
        var project = Path.Combine(files.Root, "project");
        var original = Console.Out;
        var cwd = Environment.CurrentDirectory;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new SettingsFixtureExport().TeachersExcel(
                new() { DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"), NoCache = true },
                new(), new() { Json = true }, settings: new() { Project = project }));
            using var document = JsonDocument.Parse(output.ToString());
            var directory = document.RootElement.GetProperty("data").GetProperty("outputDirectory").GetString()!;
            Assert.StartsWith(Path.Combine(project, "output") + Path.DirectorySeparatorChar, directory);
            Assert.True(File.Exists(Path.Combine(directory, "all-teachers.xlsx")));
            Assert.Equal(cwd, Environment.CurrentDirectory);
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task ConcurrentAtomicReplacementsAndSettingsReadsUseCompleteSnapshots()
    {
        using var files = new SettingsFixture();
        var path = Path.Combine(files.Root, "project", CliSettings.FileName);
        // Large valid documents keep asynchronous readers active while publication replaces
        // the path. On Windows this also exercises delete sharing on the open reader.
        var value = new string('x', 1024 * 1024);
        var before = JsonSerializer.Serialize(new { schemaVersion = 1, defaults = new { GoogleCalendarConfig = new { calendarName = value } } });
        var after = JsonSerializer.Serialize(new { schemaVersion = 1, defaults = new { GoogleCalendarConfig = new { calendarName = "replacement" + value } } });
        await files.Project(before);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        async Task Read()
        {
            for (var i = 0; i < 8; i++)
            {
                using var settings = await CliSettings.Load(new(), Path.Combine(files.Root, "project"), files.UserFile,
                    cancellationToken: deadline.Token);
                Assert.Contains(settings.Get(GoogleCalendarConfig.Key)!.CalendarName, new[] { value, "replacement" + value });
            }
        }
        var readers = Enumerable.Range(0, 4).Select(_ => Read()).ToArray();
        for (var i = 0; i < 8; i++)
        {
            await AtomicFile.Publish(path, async (stream, token) =>
            {
                using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync((i % 2 == 0 ? after : before).AsMemory(), token);
                await writer.FlushAsync(token);
            }, deadline.Token);
        }
        await Task.WhenAll(readers);
    }

    private sealed class SettingsFixtureExport : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<ScheduleLib.Dates.StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = ScheduleLib.Dates.Semester.Sem2; });
        }
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "schedulelib-settings-" + Guid.NewGuid().ToString("N"));
        public string UserFile => Path.Combine(Root, "user", CliSettings.FileName);
        public SettingsFixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "user"));
            Directory.CreateDirectory(Path.Combine(Root, "project"));
            Directory.CreateDirectory(Path.Combine(Root, "project", "user"));
        }
        public Task User(string value) => File.WriteAllTextAsync(UserFile, value);
        public Task Project(string value) => File.WriteAllTextAsync(Path.Combine(Root, "project", CliSettings.FileName), value);
        public Task<ResolvedSettings> Load(SettingsArguments arguments, ConfigOverrideArguments? overrides = null) =>
            CliSettings.Load(arguments, Path.Combine(Root, "project"), UserFile, overrides: overrides);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
