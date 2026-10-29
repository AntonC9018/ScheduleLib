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

public sealed class WebsiteExportTests
{
    /// <summary>One thesis row written to the export workbook.</summary>
    private readonly record struct ThesisRow(string Mentor, string Student, string Group);

    /// <summary>A teacher identity served by the fake website API.</summary>
    private readonly record struct TeacherIdentity(string FirstName, string LastName, string Slug);

    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static SourceArguments FixtureSource
    {
        get
        {
            return new()
            {
                NoCache = true,
                DataDirectory = Fixtures,
            };
        }
    }

    [Fact]
    public async Task WebsiteSchedulesProducesPerTeacherFilesAndZip()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ScheduleKey1] = "mapped-teacher";
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(path: Path.Combine(output, "notes.txt"), contents: "user file");
        var (exit, stdout, stderr) = await Capture(async () =>
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 0, actual: exit);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(
            expected: 1,
            actual: root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            expected: "export website-schedules",
            actual: root.GetProperty("command").GetString());
        Assert.Equal(
            expected: "succeeded",
            actual: root.GetProperty("status").GetString());
        var data = root.GetProperty("data");
        var fileCountElement = data.GetProperty("fileCount");
        var fileCount = fileCountElement.GetInt32();
        Assert.True(fileCount >= 1);
        Assert.NotEmpty(data.GetProperty("missingSlugs").EnumerateArray());
        Assert.Contains(expectedSubstring: "No slug", actualString: stderr);
        var teacherFile = Path.Combine(output, "mapped-teacher.json");
        bool condition = File.Exists(teacherFile);
        Assert.True(condition);
        using (var content = JsonDocument.Parse(await File.ReadAllTextAsync(teacherFile)))
        {
            Assert.NotEmpty(content.RootElement.GetProperty("scheduleDaysDto")
                .EnumerateArray());
        }

        var zipPath = Path.Combine(output, "orar.zip");
        bool condition2 = File.Exists(zipPath);
        Assert.True(condition2);
        Assert.Equal(
            expected: zipPath,
            actual: data.GetProperty("zipPath").GetString());
        AssertZipMatchesDirectory(zipPath: zipPath, directory: output);
        string path2 = Path.Combine(output, "schedulelib-manifest.json");
        bool condition3 = File.Exists(path2);
        Assert.True(condition3);
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));

        var scheduleFiles = Directory.EnumerateFiles(
            path: Fixtures,
            searchPattern: "*schedule_*.json",
            SearchOption.AllDirectories);
        Assert.Empty(scheduleFiles);
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
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()));
        Assert.Equal(expected: 0, actual: exit);
        string expectedSubstring2 = Path.Combine(output, "orar.zip");
        string actualString2 = stdout;
        Assert.Contains(expectedSubstring: expectedSubstring2, actualString: actualString2);
        string expectedSubstring3 = Path.Combine(output, "mapped-teacher.json");
        string actualString3 = stdout;
        Assert.Contains(expectedSubstring: expectedSubstring3, actualString: actualString3);
    }

    [Fact]
    public async Task WebsiteThesesProducesFilesAndReportsMissingSlugs()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.Slugs[teachers.ThesisKey1] = "mapped-teacher";
        fixture.WebsiteTeachers.Add(new(
            FirstName: teachers.ApiFirst1,
            LastName: teachers.ApiLast1,
            Slug: "mapped-teacher"));
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 0, actual: exit);
        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement;
        Assert.Equal(
            expected: 1,
            actual: root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            expected: "export website-theses",
            actual: root.GetProperty("command").GetString());
        Assert.Equal(
            expected: "succeeded",
            actual: root.GetProperty("status").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(
            expected: 1,
            actual: data.GetProperty("fileCount").GetInt32());
        var missing = data.GetProperty("missingSlugs")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.Contains(teachers.ThesisKey2.ToString(), missing);
        Assert.NotEmpty(root.GetProperty("warnings").EnumerateArray());
        var teacherFile = Path.Combine(output, "mapped-teacher.json");
        bool condition = File.Exists(teacherFile);
        Assert.True(condition);
        using (var content = JsonDocument.Parse(await File.ReadAllTextAsync(teacherFile)))
        {
            var items = content.RootElement.GetProperty("content")
                .EnumerateArray()
                .ToArray();
            Assert.Single(items);
            Assert.Equal(
                expected: "I2301",
                actual: items[0].GetProperty("studentGroup").GetString());
            Assert.Equal(
                expected: teachers.ThesisKey1.ToString(),
                actual: items[0].GetProperty("teacher").GetString());
        }
        var zipPath = Path.Combine(output, "theses.zip");
        bool condition2 = File.Exists(zipPath);
        Assert.True(condition2);
        Assert.Equal(
            expected: zipPath,
            actual: data.GetProperty("zipPath").GetString());
        AssertZipMatchesDirectory(zipPath: zipPath, directory: output);
        string path = Path.Combine(output, "schedulelib-manifest.json");
        bool condition3 = File.Exists(path);
        Assert.True(condition3);
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
        await File.WriteAllTextAsync(path: sentinel, contents: "sentinel-bytes");
        var output = Path.Combine(temp.Path, "website");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 5, actual: exit);
        Assert.Equal(expected: "sentinel-bytes", actual: await File.ReadAllTextAsync(sentinel));
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            expected: 5,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsTraversalSlugBeforeAnyStagedWrite()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add(new(
            FirstName: teachers.ApiFirst1,
            LastName: teachers.ApiLast1,
            Slug: "../../schedulelib-traversal-sentinel-theses"));
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        var sentinel = Path.Combine(Path.GetTempPath(), "schedulelib-traversal-sentinel-theses.json");
        await File.WriteAllTextAsync(path: sentinel, contents: "sentinel-bytes");
        try
        {
            using var temp = new TempDirectory();
            var output = Path.Combine(temp.Path, "theses");
            var (exit, stdout, _) = await Capture(async () =>
                await fixture.WebsiteTheses(
                    FixtureSource,
                    new()
                    {
                        Directory = output,
                    },
                    new()
                    {
                        Json = true,
                    }));
            Assert.Equal(expected: 5, actual: exit);
            Assert.Equal(expected: "sentinel-bytes", actual: await File.ReadAllTextAsync(sentinel));
            var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
            System.Collections.IEnumerable collection2 = collection;
            Assert.Empty(collection2);
            using var document = JsonDocument.Parse(stdout);
            Assert.Equal(
                expected: "failed",
                actual: document.RootElement.GetProperty("status").GetString());
            Assert.Equal(
                expected: 5,
                actual: document.RootElement.GetProperty("exitCode").GetInt32());
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
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new())
            : await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new());
        Assert.Equal(expected: 5, actual: exit);
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Theory]
    [InlineData("website-schedules")]
    [InlineData("website-theses")]
    public async Task WebsiteExportsMapConnectionFailureToExit5(string command)
    {
        HttpRequestException CalculateError()
        {
            var error = new HttpRequestException("connection refused");
            return error;
        }

        var fixture = new FixtureWebsite
        {
            UseFakeSlugProvider = false,
            HttpHandlerOverride = new FakeThrowingHandler(CalculateError()),
        };
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new())
            : await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new());
        Assert.Equal(expected: 5, actual: exit);
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
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
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "output");
        var exit = command == "website-schedules"
            ? await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new())
            : await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new());
        Assert.Equal(expected: 5, actual: exit);
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
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
        await File.WriteAllTextAsync(path: Path.Combine(output, "notes.txt"), contents: "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 5, actual: exit);
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            expected: 5,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsCaseCollidingSlugs()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add(new(
            FirstName: "Ion",
            LastName: "Popescu",
            Slug: "Case-Teacher"));
        fixture.WebsiteTeachers.Add(new(
            FirstName: "Maria",
            LastName: "Ionescu",
            Slug: "case-teacher"));
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(path: Path.Combine(output, "notes.txt"), contents: "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 5, actual: exit);
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            expected: 5,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
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
        await File.WriteAllTextAsync(path: Path.Combine(output, "notes.txt"), contents: "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 5, actual: exit);
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            expected: 5,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public async Task WebsiteThesesRejectsReservedNameCaseVariant()
    {
        var fixture = new FixtureWebsite();
        var teachers = await fixture.DiscoverTeachersAsync();
        fixture.WebsiteTeachers.Add(new(
            FirstName: teachers.ApiFirst1,
            LastName: teachers.ApiLast1,
            Slug: "SCHEDULELIB-MANIFEST"));
        fixture.ThesesXlsx = BuildThesesXlsx(mappedMentor: teachers.Display1, unmappedMentor: teachers.Display2);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "theses");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(path: Path.Combine(output, "notes.txt"), contents: "user file");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteTheses(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 5, actual: exit);
        Assert.Equal(expected: "user file", actual: await File.ReadAllTextAsync(Path.Combine(output, "notes.txt")));
        var collection = Directory.EnumerateFiles(path: output, searchPattern: "*.json");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        using var document = JsonDocument.Parse(stdout);
        Assert.Equal(
            expected: "failed",
            actual: document.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            expected: 5,
            actual: document.RootElement.GetProperty("exitCode").GetInt32());
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
        Assert.Equal(expected: 0, actual: counts.LatestTeacherless);
        Assert.Equal(expected: 0, actual: counts.AllTeacherless);
        Assert.Equal(expected: counts.AllWeekly, actual: counts.LatestWeekly);
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "website");
        var (exit, stdout, _) = await Capture(async () =>
            await fixture.WebsiteSchedules(
                FixtureSource,
                new()
                {
                    Directory = output,
                },
                new()
                {
                    Json = true,
                }));
        Assert.Equal(expected: 0, actual: exit);
        using var document = JsonDocument.Parse(stdout);
        var data = document.RootElement.GetProperty("data");
        var fileCountElement = data.GetProperty("fileCount");
        var fileCount = fileCountElement.GetInt32();
        Assert.True(fileCount >= 1);
        var warnings = document.RootElement.GetProperty("warnings")
            .EnumerateArray()
            .Select(x => x.GetString())
            .ToArray();
        Assert.DoesNotContain(warnings, x => x is not null && x.Contains("no teacher", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledWebsiteCommandsReturn130()
    {
        var cancelled = new CancellationToken(canceled: true);
        Assert.Equal(
            expected: 130,
            actual: await new FixtureWebsite().WebsiteSchedules(
                new(),
                new(),
                new(),
                cancelled));
        Assert.Equal(
            expected: 130,
            actual: await new FixtureWebsite().WebsiteTheses(
                new(),
                new(),
                new(),
                cancelled));
    }

    [Fact]
    public async Task MissingSourceReturnsConfigurationError()
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var missing = Path.Combine(Path.GetTempPath(), CalculatePath2());
        var source = new SourceArguments
        {
            NoCache = true,
            DataDirectory = missing,
        };
        Assert.Equal(
            expected: 3,
            actual: await new FixtureWebsite().WebsiteSchedules(
                source,
                new(),
                new()));
        Assert.Equal(
            expected: 3,
            actual: await new FixtureWebsite().WebsiteTheses(
                source,
                new(),
                new()));
    }

    private static void AssertZipMatchesDirectory(string zipPath, string directory)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(x => !string.IsNullOrEmpty(x.Name))
            .Select(x => x.FullName)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var files = Directory.EnumerateFiles(path: directory, searchPattern: "*.json")
            .Select(Path.GetFileName)
            .Where(x => x != "schedulelib-manifest.json")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected: files, actual: entries);
    }

    private static byte[] BuildThesesXlsx(string mappedMentor, string unmappedMentor)
    {
        using var workbook = new XLWorkbook();
        AddThesisSheet(
            workbook,
            "An",
            [
                new(
                    Mentor: mappedMentor,
                    Student: "Popescu Ion",
                    Group: "I2301"),
                new(
                    Mentor: unmappedMentor,
                    Student: "Ionescu Maria",
                    Group: "I2302"),
            ]);
        AddThesisSheet(
            workbook,
            "Licenta",
            []);
        AddThesisSheet(
            workbook,
            "Master",
            []);
        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        return buffer.ToArray();
    }

    private static void AddThesisSheet(
        XLWorkbook workbook,
        string name,
        ThesisRow[] rows)
    {
        var sheet = workbook.AddWorksheet(name);
        string[] headers =
        [
            "Nr.", "Grupa", "Student", "Numele conducatorului stiintific", "Tema in rusa", "Tema in romana",
            "Tema in engleza",
        ];
        for (var column = 0; column < headers.Length; column++)
        {
            sheet.Cell(row: 1, column: column + 1).Value = headers[column];
        }

        for (var row = 0; row < rows.Length; row++)
        {
            var iXLWorksheet = sheet;
            int row2 = row + 2;
            int column2 = 1;
            var cell = iXLWorksheet.Cell(row: row2, column: column2);
            int int32 = row + 1;
            cell.Value = int32;
            sheet.Cell(row: row + 2, column: 2).Value = rows[row].Group;
            sheet.Cell(row: row + 2, column: 3).Value = rows[row].Student;
            sheet.Cell(row: row + 2, column: 4).Value = rows[row].Mentor;
            sheet.Cell(row: row + 2, column: 5).Value = "Тема работы";
            sheet.Cell(row: row + 2, column: 6).Value = "Tema lucrarii";
            sheet.Cell(row: row + 2, column: 7).Value = "Thesis topic";
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

    private sealed record TeacherDiscovery(
        string Display1,
        Name ScheduleKey1,
        Name ScheduleKey2,
        Name ThesisKey1,
        string ApiFirst1,
        string ApiLast1,
        string Display2,
        Name ThesisKey2);

    private sealed class FakeSlugProvider(TeacherSlugMap map) : ISlugProvider
    {
        public ValueTask<TeacherSlugMap> SlugMap(CancellationToken cancellationToken) => ValueTask.FromResult(map);
    }

    private sealed class FakeWebsiteHttpHandler(
        IReadOnlyList<TeacherIdentity> teachers) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var content = teachers.Select((teacher, index) => new
            {
                userId = index + 1,
                firstName = teacher.FirstName,
                lastName = teacher.LastName,
                slug = teacher.Slug,
            })
                .ToArray();
            var payload = new
            {
                content,
                pageable = new
                {
                    pageNumber = 0,
                    pageSize = 1000,
                    offset = 0,
                },
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
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class FakeThesesFileProvider(byte[] bytes) : IThesesFileProvider
    {
        public ValueTask<Stream> Open(CancellationToken cancellationToken)
        {
            MemoryStream CalculateResult()
            {
                var result = new MemoryStream(bytes, writable: false);
                return result;
            }

            return ValueTask.FromResult<Stream>(CalculateResult());
        }
    }

    private sealed class FakeFailingStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            HttpResponseMessage CalculateResult()
            {
                var result = new HttpResponseMessage(status);
                return result;
            }

            return Task.FromResult(CalculateResult());
        }
    }

    private sealed class FakeThrowingHandler(Exception error) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(error);
    }

    private sealed class FakeTimeoutHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            TaskCanceledException CalculateException()
            {
                var exception = new TaskCanceledException(
                    "The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.");
                return exception;
            }

            return Task.FromException<HttpResponseMessage>(CalculateException());
        }
    }

    private sealed class FixtureWebsite : ExportCommands
    {
        public TeacherSlugMap Slugs { get; } = new(0);

        public List<TeacherIdentity> WebsiteTeachers { get; } = [];

        public byte[]? ThesesXlsx { get; set; }

        public bool UseFakeSlugProvider { get; set; } = true;

        public HttpMessageHandler? HttpHandlerOverride { get; set; }

        public void ConfigureForTest(IServiceCollection services) => ConfigureServices(services);

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
            if (UseFakeSlugProvider)
            {
                var iServiceCollection = services;
                var implementationInstance = new FakeSlugProvider(Slugs);
                ISlugProvider implementationInstance2 = implementationInstance;
                iServiceCollection.AddSingleton(implementationInstance2);
            }
#pragma warning disable CA2000 // The service provider owns and disposes the client and handler.
            services.AddSingleton(new ItUsmWebsiteHttpClient(
                new HttpClient(HttpHandlerOverride ?? new FakeWebsiteHttpHandler(WebsiteTeachers))
                {
                    BaseAddress = new Uri("https://it.usm.md/api/"),
                }));
#pragma warning restore CA2000
            if (ThesesXlsx is { } xlsx)
            {
                var iServiceCollection2 = services;
                var implementationInstance3 = new FakeThesesFileProvider(xlsx);
                IThesesFileProvider implementationInstance4 = implementationInstance3;
                iServiceCollection2.AddSingleton(implementationInstance4);
            }
        }

        public async Task<TeacherDiscovery> DiscoverTeachersAsync()
        {
            using var resolved = await CliSettings.Load(new(), cancellationToken: CancellationToken.None);
            var services = CliRuntime.CreateServices(
                FixtureSource,
                ConfigureForTest,
                resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(CancellationToken.None);
            await using var scope = provider.CreateAsyncScope();
            var full = scope.ServiceProvider.GetRequiredService<Schedule>();
            var filtered = scope.ServiceProvider.LatestPeriodSchedule();
            var remapper = scope.ServiceProvider.GetRequiredKeyedService<INameRemapper>(NameMappingKeys.Teacher);
            var ids = filtered.EnumerateWeeklyLessons()
                .Where(x => x.Lesson.Teachers.Any())
                .Select(x => x.Lesson.Teachers[0])
                .Distinct()
                .Take(2)
                .ToArray();
            Assert.True(
                ids.Length == 2,
                "The fixture schedule must contain at least two teachers with weekly lessons.");
            var first = full.Get(ids[0]);
            var second = full.Get(ids[1]);
            var display1 = first.PersonName.ToString();
            var display2 = second.PersonName.ToString();
            var parsed1 = NameHelper.Parse(display1);
            string display12 = display1;
            Name scheduleKey1 = new(first.PersonName.AsNameFields());
            Name scheduleKey2 = new(second.PersonName.AsNameFields());
            var thesisKey1 = remapper.RemapName(parsed1);
            string apiFirst1 = JoinParts(parsed1.FirstName);
            string apiLast1 = JoinParts(parsed1.LastName);
            string display22 = display2;
            var iNameRemapper = remapper;
            var name = NameHelper.Parse(display2);
            var thesisKey2 = iNameRemapper.RemapName(name);
            TeacherDiscovery teacherDiscovery = new(
                Display1: display12,
                ScheduleKey1: scheduleKey1,
                ScheduleKey2: scheduleKey2,
                ThesisKey1: thesisKey1,
                ApiFirst1: apiFirst1,
                ApiLast1: apiLast1,
                Display2: display22,
                ThesisKey2: thesisKey2);
            return teacherDiscovery;
        }

        public async Task<(int LatestTeacherless, int LatestWeekly, int AllTeacherless, int AllWeekly)>
            LatestPeriodTeacherlessAsync()
        {
            using var resolved = await CliSettings.Load(new(), cancellationToken: CancellationToken.None);
            var services = CliRuntime.CreateServices(
                FixtureSource,
                ConfigureForTest,
                resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(CancellationToken.None);
            await using var scope = provider.CreateAsyncScope();
            var latest = scope.ServiceProvider.LatestPeriodSchedule()
                .EnumerateWeeklyLessons()
                .ToArray();
            var full = scope.ServiceProvider.GetRequiredService<Schedule>();
            var all = full.EnumerateWeeklyLessons().ToArray();
            return (latest.Count(x => !x.Lesson.Teachers.Any()), latest.Length,
                all.Count(x => !x.Lesson.Teachers.Any()), all.Length);
        }

        private static string JoinParts(NameParts<string?> parts)
        {
            var items = new List<string>();
            for (var i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrEmpty(parts[i]))
                {
                    items.Add(parts[i]!);
                }
            }

            return string.Join(' ', items);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CalculatePath2());

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static string CalculatePath2()
        {
            string path2 = $"schedulelib-website-{Guid.NewGuid():N}";
            return path2;
        }
    }
}
