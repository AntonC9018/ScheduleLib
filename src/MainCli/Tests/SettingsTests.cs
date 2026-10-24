using System.Diagnostics;
using System.Text.Json;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

public sealed class SettingsTests
{
    [Fact]
    public async Task LabSubGroupMergeSettingIsOptInGlobalAndUsesUserProjectLayering()
    {
        using var files = new SettingsFixture();
        using (var defaults = await files.Load(new()))
        {
            Assert.False(defaults.GetGlobal(ScheduleDerivationConfig.Key)!.AutoMergeLabSubGroups);
        }

        await files.User(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":true}}}""");
        await files.Project(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":null}}}""");
        using (var settings = await files.Load(new() { Profile = "Curmanschii Anton" }))
        {
            Assert.True(settings.GetGlobal(ScheduleDerivationConfig.Key)!.AutoMergeLabSubGroups);
            var services = new ServiceCollection();
            services.AddConfigsServices();
            settings.ConfigureServices(services);
            using var provider = services.BuildServiceProvider();
            Assert.True(provider.GetRequiredService<IOptions<ScheduleDerivationOptions>>().Value.AutoMergeLabSubGroups);
        }
        await files.Project(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":false}}}""");
        using var overridden = await files.Load(new() { Profile = "Nartea Nichita" });
        Assert.False(overridden.GetGlobal(ScheduleDerivationConfig.Key)!.AutoMergeLabSubGroups);
    }

    [Theory]
    [InlineData(
        "{\"schemaVersion\":1,\"profiles\":{\"Curmanschii Anton\":{\"ScheduleDerivationConfig\":{\"autoMergeLabSubGroups\":true}}}}")]
    [InlineData(
        "{\"schemaVersion\":1,\"operations\":{\"profiles\":{\"Curmanschii Anton\":[{\"operation\":\"reset\",\"key\":\"ScheduleDerivationConfig\"}]}}}")]
    public async Task GlobalLabMergeSettingRejectsProfileOverrides(string document)
    {
        using var files = new SettingsFixture();
        await files.Project(document);
        var error = await Assert.ThrowsAsync<JsonException>(() => files.Load(new()));
        Assert.Contains(expectedSubstring: "global", actualString: error.Message);
    }

    [Fact]
    public async Task AttendanceWorkbookComesFromProfileInsteadOfPersonalCodedPath()
    {
        using var files = new SettingsFixture();
        using (var defaults = await files.Load(new() { Profile = "Curmanschii Anton" }))
        {
            Assert.Empty(defaults.Get(LessonAttendanceConfig.Key)?.Sources ?? []);
        }

        await files.Project(
            """
            {"schemaVersion":1,"profiles":{"Curmanschii Anton":{"LessonAttendanceConfig":{
            "sources":[{"filePath":"private-attendance.xlsx","repeatedCourseBehavior":"Error","cellValueFormat":"IgnoreGrade"}]}}}}
            """);
        using var settings = await files.Load(new() { Profile = "Curmanschii Anton" });
        var source = Assert.Single(settings.Get(LessonAttendanceConfig.Key)!.Sources);
        Assert.Equal(
            expected: Path.Combine(
                files.Root,
                "project",
                "private-attendance.xlsx"),
            actual: source.FilePath);
    }

    [Theory]
    [InlineData("Visnevschi Vlada")]
    [InlineData("Pugaci Diana")]
    [InlineData("Tretiacova Elizaveta")]
    [InlineData("Plesca Natalia")]
    public async Task RegistryAnalysisTeachersHaveSeparateSelectableProfiles(string teacher)
    {
        using var files = new SettingsFixture();
        using var settings = await files.Load(new() { Profile = teacher });
        Assert.Contains(teacher, settings.Profiles);
        Assert.Equal(expected: teacher, actual: settings.Profile);
        Assert.Equal(expected: ExtraLessonInstanceAction.LeaveAlone, actual: settings.Get(RegistryConfig.Key)!.ExtraLessonInstanceAction);
        var services = new ServiceCollection();
        services.AddConfigsServices();
        settings.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(expected: teacher, actual: scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>().TeacherName.ToString());
    }

    [Fact]
    public async Task LayersResolveTypedValuesAndReachOperationalDataProvider()
    {
        using var files = new SettingsFixture();
        await files.User(
            """
            {"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"user","credentials":{"credentialsPath":"user-tokens"}}},
            "profiles":{"Curmanschii Anton":{"GoogleCalendarConfig":{"calendarName":"user-profile"}}}}
            """);
        await files.Project(
            """
            {"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"project"}},
            "profiles":{"Curmanschii Anton":{"GoogleCalendarConfig":{"calendarName":"project-profile"}}}}
            """);
        using var global = await files.Load(new());
        Assert.Equal(expected: "project", actual: global.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(
            expected: Path.Combine(
                files.Root,
                "user",
                "user-tokens"),
            actual: global.Get(GoogleCalendarConfig.Key)!.Credentials!.CredentialsPath);
        using var teacher = await files.Load(new() { Profile = "Curmanschii Anton" });
        Assert.Equal(expected: "project-profile", actual: teacher.Get(GoogleCalendarConfig.Key)!.CalendarName);
        using var cli = await files.Load(new() { Profile = "Curmanschii Anton" }, new() { CalendarName = "CLI" });
        Assert.Equal(expected: "CLI", actual: cli.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(
            expected: new[]
            {
                "code defaults",
                "user defaults",
                "project settings",
                "user profile: Curmanschii Anton",
                "project profile: Curmanschii Anton",
                "CLI arguments",
            },
            actual: cli.Inspect("GoogleCalendarConfig.calendarName").Sources.Select(x => x.Name));
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        teacher.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(
            expected: "project-profile",
            actual: scope.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(
            expected: "project-profile",
            actual: scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>().Get()!.CalendarName);
        Assert.Equal(
            expected: "Curmanschii Anton",
            actual: scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>().TeacherName.ToString());
        // Switching teacher scopes uses that teacher's path, rather than reusing the selected teacher's settings.
        await using var other = provider.CreateMarkerScope(x =>
            x.TeacherName = ScheduleLib.Parsing.NameHelper.Parse("Nartea Nichita"));
        Assert.Null(other.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleDriveConfig.Key));
        Assert.Equal(
            expected: "project",
            actual: other.ServiceProvider.GetRequiredService<DataProvider>().Get(GoogleCalendarConfig.Key)!.CalendarName);
    }

    [Fact]
    public async Task NullInheritanceKeyedMergingAndDefiningFilePaths()
    {
        using var files = new SettingsFixture();
        await files.User(
            """
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"lessonDelayLimit":8,"goodColor":"Blue"},
            "GoogleCalendarConfig":{"calendarName":"user"},"LessonAttendanceConfig":{"sources":[{"filePath":"shared.xlsx","cellValueFormat":"IgnoreGrade"}]}}}
            """);
        await files.Project(
            """
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"lessonDelayLimit":null},"GoogleCalendarConfig":{"calendarName":null},
            "LessonAttendanceConfig":{"sources":[{"filePath":"../user/shared.xlsx","repeatedCourseBehavior":"Error"},{"filePath":"new.xlsx"}]},
            "LessonTopicsConfig":{"sources":[{"$type":"ManifestLessonTopicSourceDefinition","path":"topics"}]}}}
            """);
        using var settings = await files.Load(new());
        Assert.Equal(expected: "user", actual: settings.Get(GoogleCalendarConfig.Key)!.CalendarName);
        Assert.Equal(expected: 8, actual: settings.Get(DeadlinesExcelConfig.Key)!.LessonDelayLimit);
        Assert.Equal(expected: System.Drawing.Color.Blue, actual: settings.Get(DeadlinesExcelConfig.Key)!.GoodColor);
        var attendance = settings.Get(LessonAttendanceConfig.Key)!;
        Assert.Equal(expected: 2, actual: attendance.Sources.Count);
        var shared = Assert.Single(
            attendance.Sources.Where(x => x.FilePath
                == Path.Combine(
                    files.Root,
                    "user",
                    "shared.xlsx")));
        Assert.Equal(expected: "IgnoreGrade", actual: shared.CellValueFormat.ToString());
        Assert.Equal(expected: "Error", actual: shared.RepeatedCourseBehavior.ToString());
        Assert.Contains(
            attendance.Sources,
            x => x.FilePath
                == Path.Combine(
                    files.Root,
                    "project",
                    "new.xlsx"));
        Assert.Contains(
            settings.Get(LessonTopicsConfig.Key)!.Sources,
            x => x is ManifestLessonTopicSourceDefinition manifest
                && manifest.Path
                == Path.Combine(
                    files.Root,
                    "project",
                    "topics"));
    }

    [Fact]
    public async Task NearestProjectAndExplicitDirectoryWithoutFilePreserveCwd()
    {
        using var files = new SettingsFixture();
        await files.Project("""{"schemaVersion":1}""");
        var nested = Path.Combine(
            files.Root,
            "project",
            "nested");
        Directory.CreateDirectory(Path.Combine(nested, "child"));
        await File.WriteAllTextAsync(path: Path.Combine(nested, CliSettings.FileName), contents: """{"schemaVersion":1}""");
        var cwd = Environment.CurrentDirectory;
        using var nearest = await CliSettings.Load(
            new(),
            invocationDirectory: Path.Combine(nested, "child"),
            userFile: files.UserFile);
        Assert.Equal(expected: nested, actual: nearest.ProjectDirectory);
        using var explicitRoot = await CliSettings.Load(
            new() { Project = "../../user" },
            invocationDirectory: Path.Combine(nested, "child"),
            userFile: files.UserFile);
        Assert.Equal(
            expected: Path.Combine(
                files.Root,
                "project",
                "user"),
            actual: explicitRoot.ProjectDirectory);
        Assert.Equal(expected: cwd, actual: Environment.CurrentDirectory);
    }

    [Theory]
    [InlineData(
        "{\"schemaVersion\":1,\"defaults\":{\"RegistryConfig\":{\"commandProcessingConfig\":{\"unknown\":[]}}}}")]
    [InlineData(
        "{\"schemaVersion\":1,\"defaults\":{\"LessonTopicsConfig\":{\"fallbackProviders\":[{\"lessonType\":\"Lab\",\"provider\":{}}]}}}")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"RegistryLessonFilterConfig\":{\"skipAttendance\":999}}}")]
    [InlineData("{\"schemaVersion\":1,\"unknown\":true}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"StudyYearOptions\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"GoogleCalendarConfig\":{\"calendarNmae\":null}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"GoogleCalendarConfig\":{\"calendarName\":10}}}")]
    [InlineData("{\"schemaVersion\":1,\"defaults\":{\"TeacherLayerConfig\":{}}}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":{\"Other teacher\":{}}}")]
    [InlineData(
        "{\"schemaVersion\":1,\"profiles\":{\"Nartea Nichita\":{\"GoogleCalendarConfig\":{\"unknown\":true}}}}")]
    [InlineData(
        "{\"schemaVersion\":1,\"defaults\":{\"MoodleConfig\":{\"credentials\":{\"$type\":\"ValueCredentialsSource\",\"value\":{\"password\":\"secret\"}}}}}")]
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
        await files.Project(
            """{"schemaVersion":1,"defaults":{"RegistryLessonFilterConfig":{"skipAttendance":"Zi"}}}""");
        using var global = await files.Load(new());
        Assert.Equal(expected: "Zi", actual: global.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance.ToString());
        using var teacher = await files.Load(new() { Profile = "Iatasina Tamara" });
        Assert.Equal(expected: "FrecventaRedusa", actual: teacher.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance.ToString());
    }

    [Fact]
    public async Task InspectionRedactsExistingCodeSecrets()
    {
        using var files = new SettingsFixture();
        using var settings = await files.Load(new());
        settings.Tree.Defaults.Moodle().ConfigureValue(x =>
            x.Credentials = new ScheduleLib.Scraping.Common.Config.ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "user",
                    Password = "never-print-this",
                },
            });
        var inspected = settings.Inspect("MoodleConfig").Value!.ToJsonString();
        Assert.DoesNotContain(expectedSubstring: "never-print-this", actualString: inspected);
        Assert.Contains(expectedSubstring: "[redacted]", actualString: inspected);
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
        Assert.Equal(expected: System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip, actual: json.UnmappedMemberHandling);
        var tree = provider.GetRequiredService<TreeBuilder>();
        tree.Defaults.GoogleCalendar().ConfigureValue(x => x.CalendarName = "desktop");
        using var output = new MemoryStream();
        await provider.GetRequiredService<TreeSerializer>().SerializeValues([tree.BaseNode], output);
        using var document = JsonDocument.Parse(output.ToArray());
        Assert.Equal(
            expected: "desktop",
            actual: document.RootElement[0].GetProperty("GoogleCalendarConfig").GetProperty("CalendarName").GetString());
        Assert.True(document.RootElement[0].TryGetProperty("$LayerName", out _));
    }

    [Fact]
    public async Task FreshProcessConfigIsCleanJsonWithoutScheduleData()
    {
        using var files = new SettingsFixture();
        await files.Project(
            """{"schemaVersion":1,"defaults":{"GoogleCalendarConfig":{"calendarName":"fresh-process"}}}""");
        var assembly = typeof(CliHost).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.Combine(files.Root, "project"),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(assembly);
        foreach (var argument in new[]
                 {
                     "config",
                     "get",
                     "GoogleCalendarConfig.calendarName",
                     "--project",
                     ".",
                     "--calendar-name",
                     "CLI-process",
                     "--json",
                 })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.Equal(expected: 0, actual: process.ExitCode);
        Assert.Equal(expected: "", actual: await stderr);
        using var document = JsonDocument.Parse(await stdout);
        Assert.Equal(expected: "CLI-process", actual: document.RootElement.GetProperty("data").GetProperty("value").GetString());
        Assert.Equal(expected: "config get", actual: document.RootElement.GetProperty("command").GetString());
        Assert.Equal(
            expected: new[]
            {
                CliSettings.FileName,
            },
            actual: Directory.GetFiles(Path.Combine(files.Root, "project")).Select(Path.GetFileName));
    }

    [Theory]
    [InlineData(
        new[]
        {
            "config",
            "get",
            "--json",
        },
        2)]
    [InlineData(
        new[]
        {
            "query",
            "lessons",
            "--calendar-name",
            "irrelevant",
            "--json",
        },
        2)]
    public async Task MissingAndIrrelevantArgumentsProduceVersionedJsonErrors(string[] arguments, int expected)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(expected: expected, actual: await CliHost.Run(arguments));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 2, actual: document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task UnexpectedFailureBeforeInvocationRetainsExitOne()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 1,
                actual: await CliHost.RunCore(
                    ["config", "profiles", "--json"],
                    runner =>
                        runner.Configure(builder => builder.UseMiddleware(
                            (_, _) => throw new InvalidOperationException("startup failure"),
                            CommandDotNet.Execution.MiddlewareSteps.InvokeCommand - 2))));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1, actual: document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
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
            Assert.Equal(
                expected: 0,
                actual: await new SettingsFixtureExport().TeachersExcel(
                    new()
                    {
                        DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                        NoCache = true,
                    },
                    new(),
                    new() { Json = true },
                    settings: new() { Project = project }));
            using var document = JsonDocument.Parse(output.ToString());
            var directory = document.RootElement.GetProperty("data").GetProperty("outputDirectory").GetString()!;
            Assert.StartsWith(expectedStartString: Path.Combine(project, "output") + Path.DirectorySeparatorChar, actualString: directory);
            Assert.True(File.Exists(Path.Combine(directory, "all-teachers.xlsx")));
            Assert.Equal(expected: cwd, actual: Environment.CurrentDirectory);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task ConcurrentAtomicReplacementsAndSettingsReadsUseCompleteSnapshots()
    {
        using var files = new SettingsFixture();
        var path = Path.Combine(
            files.Root,
            "project",
            CliSettings.FileName);
        // Large valid documents keep asynchronous readers active while publication replaces
        // the path. On Windows this also exercises delete sharing on the open reader.
        var value = new string('x', 1024 * 1024);
        var before = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                defaults = new { GoogleCalendarConfig = new { calendarName = value } },
            });
        var after = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                defaults = new { GoogleCalendarConfig = new { calendarName = (string) $"replacement{value}" } },
            });
        await files.Project(before);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        async Task Read()
        {
            for (var i = 0; i < 8; i++)
            {
                using var settings = await CliSettings.Load(
                    new(),
                    invocationDirectory: Path.Combine(files.Root, "project"),
                    userFile: files.UserFile,
                    cancellationToken: deadline.Token);
                Assert.Contains(
                    settings.Get(GoogleCalendarConfig.Key)!.CalendarName,
                    new[]
                    {
                        value,
                        (string) $"replacement{value}",
                    });
            }
        }

        var readers = Enumerable.Range(start: 0, count: 4).Select(_ => Read()).ToArray();
        for (var i = 0; i < 8; i++)
        {
            await AtomicFile.Publish(
                path,
                async (stream, token) =>
                {
                    using var writer = new StreamWriter(stream, leaveOpen: true);
                    await writer.WriteAsync((i % 2 == 0 ? after : before).AsMemory(), token);
                    await writer.FlushAsync(token);
                },
                deadline.Token);
        }
        await Task.WhenAll(readers);
    }

    private sealed class SettingsFixtureExport : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<ScheduleLib.Dates.StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = ScheduleLib.Dates.Semester.Sem2;
            });
        }
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), (string) $"schedulelib-settings-{Guid.NewGuid().ToString("N")}");

        public string UserFile
        {
            get
            {
                return Path.Combine(
                    Root,
                    "user",
                    CliSettings.FileName);
            }
        }

        public SettingsFixture()
        {
            Directory.CreateDirectory(Path.Combine(Root, "user"));
            Directory.CreateDirectory(Path.Combine(Root, "project"));
            Directory.CreateDirectory(
                Path.Combine(
                    Root,
                    "project",
                    "user"));
        }

        public Task User(string value) => File.WriteAllTextAsync(path: UserFile, contents: value);

        public Task Project(string value)
        {
            return File.WriteAllTextAsync(
                path: Path.Combine(
                    Root,
                    "project",
                    CliSettings.FileName),
                contents: value);
        }

        public Task<ResolvedSettings> Load(SettingsArguments arguments, ConfigOverrideArguments? overrides = null)
        {
            return CliSettings.Load(
                arguments,
                invocationDirectory: Path.Combine(Root, "project"),
                userFile: UserFile,
                overrides: overrides);
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
