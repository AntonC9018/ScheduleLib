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
            var global = defaults.GetGlobal(ScheduleDerivationConfig.Key);
            bool? condition = global!.AutoMergeLabSubGroups;
            Assert.False(condition);
        }

        await files.User(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":true}}}""");
        await files.Project(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":null}}}""");
        using (var settings = await files.Load(new() { Profile = "Curmanschii Anton" }))
        {
            var global2 = settings.GetGlobal(ScheduleDerivationConfig.Key);
            bool? condition2 = global2!.AutoMergeLabSubGroups;
            Assert.True(condition2);
            var services = new ServiceCollection();
            services.AddConfigsServices();
            settings.ConfigureServices(services);
            using var provider = services.BuildServiceProvider();
            Assert.True(provider.GetRequiredService<IOptions<ScheduleDerivationOptions>>().Value.AutoMergeLabSubGroups);
        }
        await files.Project(
            """{"schemaVersion":1,"defaults":{"ScheduleDerivationConfig":{"autoMergeLabSubGroups":false}}}""");
        using var overridden = await files.Load(new() { Profile = "Nartea Nichita" });
        var global3 = overridden.GetGlobal(ScheduleDerivationConfig.Key);
        bool? condition3 = global3!.AutoMergeLabSubGroups;
        Assert.False(condition3);
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
        var get = settings.Get(LessonAttendanceConfig.Key);
        IEnumerable<LessonAttendanceSource> collection = get!.Sources;
        var source = Assert.Single(collection);
        string CalculateExpected()
        {
            string expected2 = Path.Combine(
                            files.Root,
                            "project",
                            "private-attendance.xlsx");
            return expected2;
        }

        Assert.Equal(
                    expected: CalculateExpected(),
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
        ExtraLessonInstanceAction? expected2 = ExtraLessonInstanceAction.LeaveAlone;
        var get = settings.Get(RegistryConfig.Key);
        var actual2 = get!.ExtraLessonInstanceAction;
        Assert.Equal(expected: expected2, actual: actual2);
        var services = new ServiceCollection();
        services.AddConfigsServices();
        settings.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Equal(expected: teacher, actual: scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>().TeacherName
            .ToString());
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
        string expected4 = "project";
        var get = global.Get(GoogleCalendarConfig.Key);
        string? actual3 = get!.CalendarName;
        Assert.Equal(expected: expected4, actual: actual3);
        string CalculateExpected()
        {
            string expected2 = Path.Combine(
                            files.Root,
                            "user",
                            "user-tokens");
            return expected2;
        }

        string expected5 = CalculateExpected();
        var get2 = global.Get(GoogleCalendarConfig.Key);
        string? actual4 = get2!.Credentials!.CredentialsPath;
        Assert.Equal(
                    expected: expected5, actual: actual4);
        using var teacher = await files.Load(new() { Profile = "Curmanschii Anton" });
        string expected6 = "project-profile";
        var get3 = teacher.Get(GoogleCalendarConfig.Key);
        string? actual5 = get3!.CalendarName;
        Assert.Equal(expected: expected6, actual: actual5);
        using var cli = await files.Load(new() { Profile = "Curmanschii Anton" }, new() { CalendarName = "CLI" });
        string expected7 = "CLI";
        var get4 = cli.Get(GoogleCalendarConfig.Key);
        string? actual6 = get4!.CalendarName;
        Assert.Equal(expected: expected7, actual: actual6);
        global::System.Collections.Generic.IEnumerable<string> expected3 = new[]
            {
                "code defaults",
                "user defaults",
                "project settings",
                "user profile: Curmanschii Anton",
                "project profile: Curmanschii Anton",
                "CLI arguments",
            };
        global::ScheduleLib.Cli.SettingsValue inspect = cli.Inspect("GoogleCalendarConfig.calendarName");
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Cli.SettingsSource> iReadOnlyList = inspect.Sources;
        global::System.Func<global::ScheduleLib.Cli.SettingsSource, string> selector = x => x.Name;
        global::System.Collections.Generic.IEnumerable<string> actual2 = iReadOnlyList.Select(selector);
        Assert.Equal(
            expected: expected3, actual: actual2);
        var services = new ServiceCollection();
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        teacher.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        string expected8 = "project-profile";
        var get5 = scope.ServiceProvider.GetRequiredService<DataProvider>()
            .Get(GoogleCalendarConfig.Key);
        string? actual7 = get5!.CalendarName;
        Assert.Equal(
            expected: expected8, actual: actual7);
        Assert.Equal(
            expected: "project-profile",
            actual: scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>()
                .Get()!.CalendarName);
        Assert.Equal(
            expected: "Curmanschii Anton",
            actual: scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>().TeacherName
                .ToString());
        // Switching teacher scopes uses that teacher's path, rather than reusing the selected teacher's settings.
        await using var other = provider.CreateMarkerScope(x =>
        {
            global::ScheduleLib.Application.Config.TeacherLayerConfig teacherLayerConfig = x;
            global::ScheduleLib.Parsing.Name parse = ScheduleLib.Parsing.NameHelper.Parse("Nartea Nichita");
            teacherLayerConfig.TeacherName = parse;
        });
        global::ScheduleLib.Application.Config.GoogleDriveConfig? CalculateObjectValue()
        {
            global::ScheduleLib.Application.Config.GoogleDriveConfig? objectValue = other.ServiceProvider.GetRequiredService<DataProvider>()
                .Get(GoogleDriveConfig.Key);
            return objectValue;
        }

        Assert.Null(CalculateObjectValue());
        string expected9 = "project";
        var get6 = other.ServiceProvider.GetRequiredService<DataProvider>()
            .Get(GoogleCalendarConfig.Key);
        string? actual8 = get6!.CalendarName;
        Assert.Equal(
            expected: expected9, actual: actual8);
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
        string expected2 = "user";
        var get = settings.Get(GoogleCalendarConfig.Key);
        string? actual2 = get!.CalendarName;
        Assert.Equal(expected: expected2, actual: actual2);
        int expected3 = 8;
        var get2 = settings.Get(DeadlinesExcelConfig.Key);
        int actual3 = get2!.LessonDelayLimit;
        Assert.Equal(expected: expected3, actual: actual3);
        System.Drawing.Color? expected4 = System.Drawing.Color.Blue;
        var get3 = settings.Get(DeadlinesExcelConfig.Key);
        var actual4 = get3!.GoodColor;
        Assert.Equal(expected: expected4, actual: actual4);
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
        var get4 = settings.Get(LessonTopicsConfig.Key);
        IEnumerable<LessonTopicSourceDefinition> collection = get4!.Sources;
        Predicate<LessonTopicSourceDefinition> filter = x => x is ManifestLessonTopicSourceDefinition manifest
                && manifest.Path
                == Path.Combine(
                    files.Root,
                    "project",
                    "topics");
        Assert.Contains(
collection, filter);
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
        string path2 = Path.Combine(nested, "child");
        Directory.CreateDirectory(path2);
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
        string CalculateExpected()
        {
            string expected2 = Path.Combine(
                            files.Root,
                            "project",
                            "user");
            return expected2;
        }

        Assert.Equal(
                    expected: CalculateExpected(),
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
        Assert.Equal(expected: "Zi", actual: global.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance
            .ToString());
        using var teacher = await files.Load(new() { Profile = "Iatasina Tamara" });
        Assert.Equal(expected: "FrecventaRedusa", actual: teacher.Get(RegistryLessonFilterConfig.Key)!.SkipAttendance
            .ToString());
    }

    [Fact]
    public async Task InspectionRedactsExistingCodeSecrets()
    {
        using var files = new SettingsFixture();
        using var settings = await files.Load(new());
        settings.Tree.Defaults.Moodle()
            .ConfigureValue(x =>
            x.Credentials = new ScheduleLib.Scraping.Common.Config.ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "user",
                    Password = "never-print-this",
                },
            });
        var inspected = settings.Inspect("MoodleConfig").Value!
            .ToJsonString();
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
        var json = provider.GetRequiredService<IOptionsMonitor<JsonSerializerOptions>>()
            .Get(TreeSerializer.ServiceKey);
        Assert.Null(json.PropertyNamingPolicy);
        Assert.Equal(expected: System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip, actual: json.UnmappedMemberHandling);
        var tree = provider.GetRequiredService<TreeBuilder>();
        tree.Defaults.GoogleCalendar()
            .ConfigureValue(x =>
        {
            global::ScheduleLib.Application.Config.GoogleCalendarConfig googleCalendarConfig = x;
            googleCalendarConfig.CalendarName = "desktop";
        });
        using var output = new MemoryStream();
        await provider.GetRequiredService<TreeSerializer>()
            .SerializeValues([tree.BaseNode], output);
        using var document = JsonDocument.Parse(output.ToArray());
        Assert.Equal(
            expected: "desktop",
            actual: document.RootElement[0].GetProperty("GoogleCalendarConfig")
                .GetProperty("CalendarName")
                .GetString());
        bool condition = document.RootElement[0].TryGetProperty("$LayerName", out _);
        Assert.True(condition);
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
        Assert.Equal(expected: "CLI-process", actual: document.RootElement.GetProperty("data")
            .GetProperty("value")
            .GetString());
        Assert.Equal(expected: "config get", actual: document.RootElement.GetProperty("command")
            .GetString());
        global::System.Collections.Generic.IEnumerable<string?> CalculateActual()
        {
            string path = Path.Combine(files.Root, "project");
            string[] files2 = Directory.GetFiles(path);
            global::System.Func<string, string?> selector = Path.GetFileName;
            global::System.Collections.Generic.IEnumerable<string?> actual2 = files2.Select(selector);
            return actual2;
        }

        Assert.Equal(
                    expected: new[]
                    {
                CliSettings.FileName,
                    },
                    actual: CalculateActual());
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
            Assert.Equal(expected: 2, actual: document.RootElement.GetProperty("exitCode")
                .GetInt32());
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
            Assert.Equal(expected: 1, actual: document.RootElement.GetProperty("exitCode")
                .GetInt32());
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
            var directory = document.RootElement.GetProperty("data")
                .GetProperty("outputDirectory")
                .GetString()!;
            string combine = Path.Combine(project, "output");
            string expectedStartString2 = combine + Path.DirectorySeparatorChar;
            string actualString2 = directory;
            Assert.StartsWith(expectedStartString: expectedStartString2, actualString: actualString2);
            string path = Path.Combine(directory, "all-teachers.xlsx");
            bool condition = File.Exists(path);
            Assert.True(condition);
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
        global::System.TimeSpan CalculateDelay()
        {
            global::System.TimeSpan delay = TimeSpan.FromSeconds(30);
            return delay;
        }

        using var deadline = new CancellationTokenSource(CalculateDelay());

        async Task Read()
        {
            for (var i = 0; i < 8; i++)
            {
                string CalculateInvocationDirectory()
                {
                    string invocationDirectory2 = Path.Combine(files.Root, "project");
                    return invocationDirectory2;
                }

                using var settings = await CliSettings.Load(
                                    new(),
                                    invocationDirectory: CalculateInvocationDirectory(),
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

        var readers = Enumerable.Range(start: 0, count: 4)
            .Select(_ => Read())
            .ToArray();
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
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                global::ScheduleLib.Application.Core.ScheduleBuilderInitializerOptions scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<ScheduleLib.Dates.StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = ScheduleLib.Dates.Semester.Sem2;
            });
        }
    }

    private sealed class SettingsFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), CalculatePath2());

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
            string path = Path.Combine(Root, "user");
            Directory.CreateDirectory(path);
            string path2 = Path.Combine(Root, "project");
            Directory.CreateDirectory(path2);
            string path3 = Path.Combine(
                    Root,
                    "project",
                    "user");
            Directory.CreateDirectory(
                path3);
        }

        public Task User(string value) => File.WriteAllTextAsync(path: UserFile, contents: value);

        public Task Project(string value)
        {
            string path2 = Path.Combine(
                    Root,
                    "project",
                    CliSettings.FileName);
            string contents2 = value;
            global::System.Threading.Tasks.Task writeAllText = File.WriteAllTextAsync(
                            path: path2, contents: contents2);
            return writeAllText;
        }

        public Task<ResolvedSettings> Load(SettingsArguments arguments, ConfigOverrideArguments? overrides = null)
        {
            global::ScheduleLib.Cli.SettingsArguments arguments2 = arguments;
            string invocationDirectory2 = Path.Combine(Root, "project");
            string userFile2 = UserFile;
            global::ScheduleLib.Cli.ConfigOverrideArguments? overrides2 = overrides;
            global::System.Threading.Tasks.Task<global::ScheduleLib.Cli.ResolvedSettings> load = CliSettings.Load(
            arguments2, invocationDirectory: invocationDirectory2, userFile: userFile2, overrides: overrides2);
            return load;
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);

        private static string CalculatePath2()
        {
            string toString = Guid.NewGuid()
                .ToString("N");
            string text = $"schedulelib-settings-{toString}";
            string path2 = (string) text;
            return path2;
        }
    }
}
