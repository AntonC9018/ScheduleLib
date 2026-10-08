using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using FmiWebsiteInterop.Api;
using FmiWebsiteInterop.Teachers;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Core.Services;
using ScheduleLib.Dates;
using ScheduleLib.Parsing;
using ScheduleLib.Theses.Parsing;
using Xunit;

public sealed class WebsiteExportTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static SourceArguments FixtureSource => new() { NoCache = true, DataDirectory = Fixtures };

    [Fact]
    public async Task WebsiteSchedulesProducesPerTeacherFilesAndZip()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "mapped-teacher";
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "notes.txt"), "user file");
        var (exit, stdout, stderr) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("export website-schedules", root.GetProperty("command").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("fileCount").GetInt32() >= 1);
        Assert.NotEmpty(data.GetProperty("missingSlugs").EnumerateArray());
        Assert.Contains("No slug", stderr);
        var teacherFile = Path.Combine(output, "mapped-teacher.json");
        Assert.True(File.Exists(teacherFile));
        using (var content = JsonDocument.Parse(await File.ReadAllTextAsync(teacherFile)))
            Assert.NotEmpty(content.RootElement.GetProperty("scheduleDaysDto").EnumerateArray());
        var zipPath = Path.Combine(output, "orar.zip");
        Assert.True(File.Exists(zipPath));
        Assert.Equal(zipPath, data.GetProperty("zipPath").GetString());
        AssertZipMatchesDirectory(zipPath, output);
        Assert.True(File.Exists(Path.Combine(output, "schedulelib-manifest.json")));
        Assert.Equal("user file", await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        Assert.Empty(Directory.EnumerateFiles(Fixtures, "schedule_*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task WebsiteSchedulesTextModeListsOutputPaths()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "mapped-teacher";
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new()));
        Assert.Equal(0, exit);
        Assert.Contains(Path.Combine(output, "orar.zip"), stdout);
        Assert.Contains(Path.Combine(output, "mapped-teacher.json"), stdout);
    }

    [Fact]
    public async Task WebsiteThesesProducesFilesAndReportsMissingSlugs()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ThesisKey1] = "mapped-teacher";
        fixture.WebsiteTeachers.Add((teachers.ApiFirst1, teachers.ApiLast1, "mapped-teacher"));
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("export website-theses", root.GetProperty("command").GetString());
        Assert.Equal("succeeded", root.GetProperty("status").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(1, data.GetProperty("fileCount").GetInt32());
        var missing = data.GetProperty("missingSlugs").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains(teachers.ThesisKey2.ToString(), missing);
        Assert.NotEmpty(root.GetProperty("warnings").EnumerateArray());
        var teacherFile = Path.Combine(output, "mapped-teacher.json");
        Assert.True(File.Exists(teacherFile));
        using (var content = JsonDocument.Parse(await File.ReadAllTextAsync(teacherFile)))
        {
            var items = content.RootElement.GetProperty("content").EnumerateArray().ToArray();
            Assert.Single(items);
            Assert.Equal("I2301", items[0].GetProperty("studentGroup").GetString());
            Assert.Equal(teachers.ThesisKey1.ToString(), items[0].GetProperty("teacher").GetString());
        }
        var zipPath = Path.Combine(output, "theses.zip");
        Assert.True(File.Exists(zipPath));
        Assert.Equal(zipPath, data.GetProperty("zipPath").GetString());
        AssertZipMatchesDirectory(zipPath, output);
        Assert.True(File.Exists(Path.Combine(output, "schedulelib-manifest.json")));
    }

    [Fact]
    public async Task WebsiteSchedulesRejectsTraversalSlugBeforeAnyPublish()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "aaa-valid-teacher";
        fixture.Slugs[teachers.ScheduleKey2] = "zzz-traversal/../../schedulelib-traversal-sentinel-sched";
        using var temp = new TempDirectory();
        var sentinel = Path.Combine(temp.Path, "schedulelib-traversal-sentinel-sched.json");
        await File.WriteAllTextAsync(sentinel, "sentinel-bytes");
        var output = Path.Combine(temp.Path, "website");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(5, exit);
        Assert.Equal("sentinel-bytes", await File.ReadAllTextAsync(sentinel));
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsTraversalSlugBeforeAnyStagedWrite()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add((teachers.ApiFirst1, teachers.ApiLast1, "../../schedulelib-traversal-sentinel-theses"));
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        var sentinel = Path.Combine(Path.GetTempPath(), "schedulelib-traversal-sentinel-theses.json");
        await File.WriteAllTextAsync(sentinel, "sentinel-bytes");
        try
        {
            using var temp = new TempDirectory();
            var output = Path.Combine(temp.Path, "theses");
            var (exit, stdout, _) = await Capture(async () =>
                await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new() { Json = true }));
            Assert.Equal(5, exit);
            Assert.Equal("sentinel-bytes", await File.ReadAllTextAsync(sentinel));
            Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
            using var document = JsonDocument.Parse(stdout);
            Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
            Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            File.Delete(sentinel);
        }
    }

    [Theory]
    [InlineData("website-schedules")]
    [InlineData("website-theses")]
    public async Task WebsiteExportsMapHttp503ToExit5(string command)
    {
        var fixture = new FixtureWebsite
        {
            UseFakeSlugProvider = false,
            HttpHandlerOverride = new FakeFailingStatusHandler(HttpStatusCode.ServiceUnavailable),
        };
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new())
            : await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new());
        Assert.Equal(5, exit);
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
    }

    [Theory]
    [InlineData("website-schedules")]
    [InlineData("website-theses")]
    public async Task WebsiteExportsMapConnectionFailureToExit5(string command)
    {
        var fixture = new FixtureWebsite
        {
            UseFakeSlugProvider = false,
            HttpHandlerOverride = new FakeThrowingHandler(new HttpRequestException("connection refused")),
        };
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new())
            : await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new());
        Assert.Equal(5, exit);
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
    }

    [Theory]
    [InlineData("website-schedules")]
    [InlineData("website-theses")]
    public async Task WebsiteExportsMapTimeoutToExit5(string command)
    {
        var fixture = new FixtureWebsite
        {
            UseFakeSlugProvider = false,
            HttpHandlerOverride = new FakeTimeoutHandler(),
        };
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new())
            : await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new());
        Assert.Equal(5, exit);
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
    }

    [Fact]
    public async Task WebsiteSchedulesRejectsCaseCollidingSlugs()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "Case-Teacher";
        fixture.Slugs[teachers.ScheduleKey2] = "case-teacher";
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "notes.txt"), "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(5, exit);
        Assert.Equal("user file", await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsCaseCollidingSlugs()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add(("Ion", "Popescu", "Case-Teacher"));
        fixture.WebsiteTeachers.Add(("Maria", "Ionescu", "case-teacher"));
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "notes.txt"), "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(5, exit);
        Assert.Equal("user file", await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteSchedulesRejectsReservedNameCaseVariant()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "aaa-valid-teacher";
        fixture.Slugs[teachers.ScheduleKey2] = "SCHEDULELIB-MANIFEST";
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "notes.txt"), "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(5, exit);
        Assert.Equal("user file", await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsReservedNameCaseVariant()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add((teachers.ApiFirst1, teachers.ApiLast1, "SCHEDULELIB-MANIFEST"));
        fixture.ThesesXlsx = BuildThesesXlsx(teachers.Display1, teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "notes.txt"), "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(5, exit);
        Assert.Equal("user file", await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        Assert.Empty(Directory.EnumerateFiles(output, "*.json"));
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteSchedulesReportsLatestPeriodTeacherlessLessons()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "mapped-teacher";
        var counts = await fixture.LatestPeriodTeacherlessAsync();
        Assert.True(counts.LatestWeekly > 0, "Fixture must contain latest-period weekly lessons.");
        // Minimal fixture (2025_sem2 single period, enrichment disabled for determinism) contains
        // no teacherless weekly lessons. Assert the known 0 explicitly so the positive warning
        // path is visibly uncovered; a positive-count fixture would require changing source data,
        // which #194 preserves. Latest-period semantics are asserted via LatestWeekly/AllWeekly.
        Assert.Equal(0, counts.LatestTeacherless);
        Assert.Equal(0, counts.AllTeacherless);
        Assert.Equal(counts.AllWeekly, counts.LatestWeekly);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(FixtureSource, new() { Directory = output }, new() { Json = true }));
        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(stdout);
        var data = document.RootElement.GetProperty("data");
        Assert.True(data.GetProperty("fileCount").GetInt32() >= 1);
        var warnings = document.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.DoesNotContain(warnings, x => x is not null && x.Contains("no teacher", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledWebsiteCommandsReturn130()
    {
        var cancelled = new CancellationToken(canceled: true);
        Assert.Equal(130, await new FixtureWebsite().WebsiteSchedules(new(), new(), new(), cancelled));
        Assert.Equal(130, await new FixtureWebsite().WebsiteTheses(new(), new(), new(), cancelled));
    }

    [Fact]
    public async Task MissingSourceReturnsConfigurationError()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var source = new SourceArguments { NoCache = true, DataDirectory = missing };
        Assert.Equal(3, await new FixtureWebsite().WebsiteSchedules(source, new(), new()));
        Assert.Equal(3, await new FixtureWebsite().WebsiteTheses(source, new(), new()));
    }

    private static void AssertZipMatchesDirectory(string zipPath, string directory)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name)).Select(x => x.FullName)
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var files = Directory.EnumerateFiles(directory, "*.json")
            .Select(Path.GetFileName)
            .Where(x => x != "schedulelib-manifest.json")
            .OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(files, entries);
    }

    private static byte[] BuildThesesXlsx(string mappedMentor, string unmappedMentor)
    {
        using var workbook = new XLWorkbook();
        AddThesisSheet(workbook, "An", [(mappedMentor, "Popescu Ion", "I2301"), (unmappedMentor, "Ionescu Maria", "I2302")]);
        AddThesisSheet(workbook, "Licenta", []);
        AddThesisSheet(workbook, "Master", []);
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static void AddThesisSheet(XLWorkbook workbook, string name, (string Mentor, string Student, string Group)[] rows)
    {
        var sheet = workbook.AddWorksheet(name);
        string[] headers = ["Nr.", "Grupa", "Student", "Numele conducatorului stiintific", "Tema in rusa", "Tema in romana", "Tema in engleza"];
        for (var column = 0; column < headers.Length; column++)
            sheet.Cell(1, column + 1).Value = headers[column];
        for (var row = 0; row < rows.Length; row++)
        {
            sheet.Cell(row + 2, 1).Value = row + 1;
            sheet.Cell(row + 2, 2).Value = rows[row].Group;
            sheet.Cell(row + 2, 3).Value = rows[row].Student;
            sheet.Cell(row + 2, 4).Value = rows[row].Mentor;
            sheet.Cell(row + 2, 5).Value = "Тема работы";
            sheet.Cell(row + 2, 6).Value = "Tema lucrarii";
            sheet.Cell(row + 2, 7).Value = "Thesis topic";
        }
    }

    private static async Task<(int Exit, string Stdout, string Stderr)> Capture(Func<Task<int>> run)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exit = await run();
            return (exit, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed record TeacherDiscovery(string Display1, Name ScheduleKey1, Name ScheduleKey2, Name ThesisKey1, string ApiFirst1, string ApiLast1, string Display2, Name ThesisKey2);

    private sealed class FakeSlugProvider(TeacherSlugMap map) : ISlugProvider
    {
        public ValueTask<TeacherSlugMap> SlugMap(CancellationToken cancellationToken) => ValueTask.FromResult(map);
    }

    private sealed class FakeWebsiteHttpHandler(IReadOnlyList<(string FirstName, string LastName, string Slug)> teachers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = teachers.Select((teacher, index) => new
            {
                userId = index + 1,
                firstName = teacher.FirstName,
                lastName = teacher.LastName,
                slug = teacher.Slug,
            }).ToArray();
            var payload = new
            {
                content,
                pageable = new { pageNumber = 0, pageSize = 1000, offset = 0 },
                totalPages = 1,
                totalElements = content.Length,
                numberOfElements = content.Length,
                size = 1000,
                number = 0,
                last = true,
                first = true,
                empty = content.Length == 0,
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class FakeThesesFileProvider(byte[] bytes) : IThesesFileProvider
    {
        public ValueTask<Stream> Open(CancellationToken cancellationToken) =>
            ValueTask.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private sealed class FakeFailingStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class FakeThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error);
    }

    private sealed class FakeTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing."));
    }

    private sealed class FixtureWebsite : ExportCommands
    {
        public TeacherSlugMap Slugs { get; } = new(0);
        public List<(string FirstName, string LastName, string Slug)> WebsiteTeachers { get; } = [];
        public byte[]? ThesesXlsx { get; set; }
        public bool UseFakeSlugProvider { get; set; } = true;
        public HttpMessageHandler? HttpHandlerOverride { get; set; }

        public void ConfigureForTest(IServiceCollection services) => ConfigureServices(services);

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
            if (UseFakeSlugProvider)
                services.AddSingleton<ISlugProvider>(new FakeSlugProvider(Slugs));
#pragma warning disable CA2000 // The service provider owns and disposes the client and handler.
            services.AddSingleton(new ItUsmWebsiteHttpClient(new HttpClient(HttpHandlerOverride ?? new FakeWebsiteHttpHandler(WebsiteTeachers))
            {
                BaseAddress = new Uri("https://it.usm.md/api/"),
            }));
#pragma warning restore CA2000
            if (ThesesXlsx is { } xlsx) services.AddSingleton<IThesesFileProvider>(new FakeThesesFileProvider(xlsx));
        }

        public async Task<TeacherDiscovery> DiscoverTeachersAsync()
        {
            using var resolved = await CliSettings.Load(new(), cancellationToken: CancellationToken.None);
            var services = CliRuntime.CreateServices(FixtureSource, ConfigureForTest, resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(CancellationToken.None);
            await using var scope = provider.CreateAsyncScope();
            var full = scope.ServiceProvider.GetRequiredService<ScheduleLib.Schedule>();
            var filtered = scope.ServiceProvider.LatestPeriodSchedule();
            var remapper = scope.ServiceProvider.GetRequiredKeyedService<INameRemapper>(NameMappingKeys.Teacher);
            var ids = filtered.EnumerateWeeklyLessons()
                .Where(x => x.Lesson.Teachers.Any())
                .Select(x => x.Lesson.Teachers[0])
                .Distinct()
                .Take(2)
                .ToArray();
            Assert.True(ids.Length == 2, "The fixture schedule must contain at least two teachers with weekly lessons.");
            var first = full.Get(ids[0]);
            var second = full.Get(ids[1]);
            var display1 = first.PersonName.ToString();
            var display2 = second.PersonName.ToString();
            var parsed1 = NameHelper.Parse(display1);
            return new(display1,
                new(first.PersonName.AsNameFields()), new(second.PersonName.AsNameFields()), remapper.RemapName(parsed1),
                JoinParts(parsed1.FirstName), JoinParts(parsed1.LastName),
                display2, remapper.RemapName(NameHelper.Parse(display2)));
        }

        public async Task<(int LatestTeacherless, int LatestWeekly, int AllTeacherless, int AllWeekly)> LatestPeriodTeacherlessAsync()
        {
            using var resolved = await CliSettings.Load(new(), cancellationToken: CancellationToken.None);
            var services = CliRuntime.CreateServices(FixtureSource, ConfigureForTest, resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(CancellationToken.None);
            await using var scope = provider.CreateAsyncScope();
            var latest = scope.ServiceProvider.LatestPeriodSchedule().EnumerateWeeklyLessons().ToArray();
            var full = scope.ServiceProvider.GetRequiredService<ScheduleLib.Schedule>();
            var all = full.EnumerateWeeklyLessons().ToArray();
            return (latest.Count(x => !x.Lesson.Teachers.Any()), latest.Length,
                all.Count(x => !x.Lesson.Teachers.Any()), all.Length);
        }

        private static string JoinParts(NameParts<string?> parts)
        {
            var items = new List<string>();
            for (var i = 0; i < parts.Length; i++)
                if (!string.IsNullOrEmpty(parts[i])) items.Add(parts[i]!);
            return string.Join(' ', items);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-website-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
