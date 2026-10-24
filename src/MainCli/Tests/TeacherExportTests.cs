using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.Theses.Parsing;

public sealed class TeacherExportTests
{
    [Theory]
    [InlineData("lab-deadlines")]
    [InlineData("pre-defense")]
    public async Task HelpAndInvalidOptionsDoNotLoadInputs(string command)
    {
        Assert.Equal(expected: 0, actual: await CliHost.Run(["export", command, "--help"]));
        Assert.Equal(expected: 2, actual: await CliHost.Run(["export", command, "--unknown", "--json"]));
    }

    [Fact]
    public async Task DeadlinesSelectTeacherAndApplyProfileSettingsToWorkbook()
    {
        using var files = new Fixture();
        await File.WriteAllTextAsync(
            path: Path.Combine(files.Path, "schedulelib.json"),
            contents: """
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"maxTaskRows":9,"columnWidth":8}},
             "profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":3,"columnWidth":12,"lessonDelayLimit":5},
             "LabTasksDatabaseConfig":{"sources":[]}}}}
            """);
        var originalSource = await File.ReadAllBytesAsync(files.SourceFile);
        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Contains(expectedSubstring: "every period", actualString: result.Stderr);
        Assert.Contains(expectedSubstring: "partition/course combinations", actualString: result.Stderr);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "deadlines.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Contains(expectedSubstring: "MIA2601", actualString: sheet.Name);
        Assert.DoesNotContain(expectedSubstring: "IASD2601", actualString: sheet.Name);
        Assert.Equal(expected: "01.09", actual: sheet.Cell(row: 1, column: 2).GetString());
        Assert.Equal(expected: 12, actual: sheet.Column(2).Width);
        Assert.False(sheet.Cell(row: 4, column: 2).IsEmpty());
        Assert.True(sheet.Cell(row: 5, column: 2).IsEmpty());
        Assert.Contains(expectedSubstring: "-5", actualString: sheet.Cell(row: 2, column: 2).FormulaA1);
        Assert.Equal(expected: 6, actual: sheet.ConditionalFormats.Count());
        AssertManifest(
            result.Json,
            files.Output.Directory!,
            "export lab-deadlines",
            "deadlines.xlsx");
        Assert.Equal(expected: originalSource, actual: await File.ReadAllBytesAsync(files.SourceFile));
    }

    [Fact]
    public async Task PreDefenseUsesExistingMappingAndWriterForCommissionContents()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Equal(expected: 1, actual: files.Theses.Reads);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "comisia_7.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Equal(expected: "Comisia 7", actual: sheet.Name);
        Assert.Equal(expected: "Nume, prenume student", actual: sheet.Cell(row: 1, column: 2).GetString());
        Assert.Equal(expected: "Popescu Ion", actual: sheet.Cell(row: 2, column: 2).GetString());
        Assert.Equal(expected: "I2301", actual: sheet.Cell(row: 2, column: 3).GetString());
        Assert.Equal(expected: "Curmanschii Anton", actual: sheet.Cell(row: 2, column: 4).GetString());
        Assert.Equal(expected: "Laborator AVR", actual: sheet.Cell(row: 2, column: 5).GetString());
        Assert.Equal(expected: "da/da", actual: sheet.Cell(row: 2, column: 6).GetString());
        Assert.Equal(expected: "Tema sintetica", actual: sheet.Cell(row: 2, column: 7).GetString());
        Assert.Equal(expected: 10, actual: sheet.Cell(row: 2, column: 8).GetValue<int>());
        Assert.Equal(expected: 2, actual: sheet.LastRowUsed()!.RowNumber());
        Assert.Contains(expectedSubstring: "not part of any commission", actualString: result.Stderr);
        Assert.Single(result.Json.GetProperty("warnings").EnumerateArray());
        AssertManifest(
            result.Json,
            files.Output.Directory!,
            "export pre-defense",
            "comisia_7.xlsx");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyCommissionConfigurationIsPrerequisite(bool explicitEmpty)
    {
        using var files = new Fixture();
        if (explicitEmpty)
        {
            files.Export.Commissions = [];
        }

        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Contains(expectedSubstring: "commission", actualString: result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs").EnumerateArray());
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Fact]
    public async Task ACommissionWithoutMembersIsAlsoAPrerequisite()
    {
        using var files = new Fixture();
        files.Export.Commissions =
        [
            new()
            {
                Number = 7,
                Room = "101",
                Members = [],
            },
        ];
        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Theory]
    [InlineData("number", false)]
    [InlineData("number", true)]
    [InlineData("member-within", false)]
    [InlineData("member-within", true)]
    [InlineData("member-across", false)]
    [InlineData("member-across", true)]
    [InlineData("remapped-within", false)]
    [InlineData("remapped-within", true)]
    [InlineData("remapped-across", false)]
    [InlineData("remapped-across", true)]
    [InlineData("empty-name", false)]
    [InlineData("empty-name", true)]
    [InlineData("invalid-name", false)]
    [InlineData("invalid-name", true)]
    public async Task InvalidCommissionConfigurationFailsBeforeReadingOrWriting(string duplicate, bool existingOutput)
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        var commission = Assert.Single(files.Export.Commissions!);
        var repeatedName = duplicate.StartsWith("remapped", StringComparison.Ordinal)
            ? "Curmanschi Anton"
            : "Curmanscii Anton";
        files.Export.Commissions = duplicate switch
        {
            "number" =>
            [
                commission, new()
                {
                    Number = 7,
                    Room = "102",
                    Members = [new() { Name = "Ionescu Maria" }],
                },
            ],
            "member-within" or "remapped-within" =>
            [
                new()
                {
                    Number = 7,
                    Room = "101",
                    Members = [.. commission.Members, new() { Name = repeatedName }],
                },
            ],
            "empty-name" or "invalid-name" =>
            [
                new()
                {
                    Number = 7,
                    Room = "101",
                    Members = [.. commission.Members, new() { Name = duplicate == "empty-name" ? "" : "Invalid" }],
                },
            ],
            _ =>
            [
                commission, new()
                {
                    Number = 8,
                    Room = "102",
                    Members = [new() { Name = repeatedName }],
                },
            ],
        };
        var originalSource = await File.ReadAllBytesAsync(files.SourceFile);
        var settingsFile = Path.Combine(files.Path, "schedulelib.json");
        const string settings = "{\"schemaVersion\":1,\"defaults\":{}}";
        await File.WriteAllTextAsync(path: settingsFile, contents: settings);
        var existingWorkbook = Path.Combine(files.Output.Directory!, "comisia_7.xlsx");
        var existingManifest = Path.Combine(files.Output.Directory!, "schedulelib-manifest.json");
        if (existingOutput)
        {
            Directory.CreateDirectory(files.Output.Directory!);
            await File.WriteAllTextAsync(path: existingWorkbook, contents: "caller workbook");
            await File.WriteAllTextAsync(path: existingManifest, contents: "caller manifest");
        }

        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings));

        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 3, actual: result.Json.GetProperty("exitCode").GetInt32());
        Assert.Equal(expected: "failed", actual: result.Json.GetProperty("status").GetString());
        Assert.Equal(expected: "export pre-defense", actual: result.Json.GetProperty("command").GetString());
        var error = Assert.Single(result.Json.GetProperty("errors").EnumerateArray()).GetString();
        Assert.Contains(
            expectedSubstring: duplicate switch
            {
                "number" => "commission number 7",
                "empty-name" or "invalid-name" => "commission 7 member 2 has an invalid name",
                _ => "commission members after name remapping",
            },
            actualString: error);
        Assert.Contains(error!, result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs").EnumerateArray());
        Assert.Empty(result.Json.GetProperty("warnings").EnumerateArray());
        Assert.Equal(expected: 0, actual: result.Json.GetProperty("data").GetProperty("workbookCount").GetInt32());
        Assert.Equal(expected: JsonValueKind.Null, actual: result.Json.GetProperty("data").GetProperty("outputDirectory").ValueKind);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.Equal(expected: originalSource, actual: await File.ReadAllBytesAsync(files.SourceFile));
        Assert.Equal(expected: settings, actual: await File.ReadAllTextAsync(settingsFile));
        if (existingOutput)
        {
            Assert.Equal(expected: "caller workbook", actual: await File.ReadAllTextAsync(existingWorkbook));
            Assert.Equal(expected: "caller manifest", actual: await File.ReadAllTextAsync(existingManifest));
            Assert.Equal(expected: 2, actual: Directory.GetFileSystemEntries(files.Output.Directory!).Length);
        }
        else
        {
            Assert.False(Directory.Exists(files.Output.Directory));
        }
    }

    [Theory]
    [InlineData(-2, false)]
    [InlineData(-2, true)]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1048576, false)]
    [InlineData(int.MaxValue, true)]
    public async Task InvalidResolvedDeadlineRowsFailBeforeSourceReadsOrOutputCreation(int rows, bool existingOutput)
    {
        using var files = new Fixture();
        files.Export.RejectSourceReads = true;
        var settingsFile = Path.Combine(files.Path, "schedulelib.json");
        var settings = """
                       {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"maxTaskRows":9}},
                        "profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":ROWS}}}}
                       """.Replace("ROWS", rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await File.WriteAllTextAsync(path: settingsFile, contents: settings);
        var originalSource = await File.ReadAllBytesAsync(files.SourceFile);
        var existingWorkbook = Path.Combine(files.Output.Directory!, "deadlines.xlsx");
        var existingManifest = Path.Combine(files.Output.Directory!, "schedulelib-manifest.json");
        if (existingOutput)
        {
            Directory.CreateDirectory(files.Output.Directory!);
            await File.WriteAllTextAsync(path: existingWorkbook, contents: "caller workbook");
            await File.WriteAllTextAsync(path: existingManifest, contents: "caller manifest");
        }

        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            files.Settings));

        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 3, actual: result.Json.GetProperty("exitCode").GetInt32());
        Assert.Equal(expected: "failed", actual: result.Json.GetProperty("status").GetString());
        Assert.Equal(expected: "export lab-deadlines", actual: result.Json.GetProperty("command").GetString());
        var error = Assert.Single(result.Json.GetProperty("errors").EnumerateArray()).GetString();
        Assert.Contains(expectedSubstring: "maxTaskRows", actualString: error);
        Assert.Contains(expectedSubstring: "Curmanschii Anton", actualString: error);
        Assert.Contains(expectedSubstring: "between 1 and 1048575", actualString: error);
        Assert.Contains(expectedSubstring: $"resolved value is {rows}", actualString: error);
        Assert.Contains(error!, result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs").EnumerateArray());
        Assert.Empty(result.Json.GetProperty("warnings").EnumerateArray());
        Assert.Equal(expected: 0, actual: result.Json.GetProperty("data").GetProperty("workbookCount").GetInt32());
        Assert.Equal(expected: JsonValueKind.Null, actual: result.Json.GetProperty("data").GetProperty("outputDirectory").ValueKind);
        Assert.Equal(expected: 0, actual: files.Export.SourceReads);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.Equal(expected: originalSource, actual: await File.ReadAllBytesAsync(files.SourceFile));
        Assert.Equal(expected: settings, actual: await File.ReadAllTextAsync(settingsFile));
        if (existingOutput)
        {
            Assert.Equal(expected: "caller workbook", actual: await File.ReadAllTextAsync(existingWorkbook));
            Assert.Equal(expected: "caller manifest", actual: await File.ReadAllTextAsync(existingManifest));
            Assert.Equal(expected: 2, actual: Directory.GetFileSystemEntries(files.Output.Directory!).Length);
        }
        else
        {
            Assert.False(Directory.Exists(files.Output.Directory));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public async Task PositiveDeadlineRowsAndCodedDefaultProduceWorkbook(int rows)
    {
        using var files = new Fixture();
        if (rows == 1)
        {
            await File.WriteAllTextAsync(
                path: Path.Combine(files.Path, "schedulelib.json"),
                contents: """
                {"schemaVersion":1,"profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":1},
                 "LabTasksDatabaseConfig":{"sources":[]}}}}
                """);
        }

        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            files.Settings));

        Assert.True(result.Exit == 0, result.Stderr);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "deadlines.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Equal(expected: "01.09", actual: sheet.Cell(row: 1, column: 2).GetString());
        Assert.False(sheet.Cell(row: rows + 1, column: 2).IsEmpty());
        Assert.True(sheet.Cell(row: rows + 2, column: 2).IsEmpty());
        AssertManifest(
            result.Json,
            files.Output.Directory!,
            "export lab-deadlines",
            "deadlines.xlsx");
    }

    [Fact]
    public async Task DefaultsAreIsolatedAndOwnedReplacementPreservesOtherFiles()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        var first = await Capture(() => files.Export.PreDefense(
            new(),
            new() { Json = true },
            files.Settings));
        var second = await Capture(() => files.Export.PreDefense(
            new(),
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 0, actual: first.Exit);
        Assert.Equal(expected: 0, actual: second.Exit);
        var directory = first.Json.GetProperty("data").GetProperty("outputDirectory").GetString()!;
        Assert.Equal(expected: Path.Combine(files.Path, "output"), actual: Path.GetDirectoryName(directory));
        Assert.NotEqual(expected: directory, actual: second.Json.GetProperty("data").GetProperty("outputDirectory").GetString());
        var notes = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(path: notes, contents: "keep me");
        var replaced = await Capture(() => files.Export.PreDefense(
            new() { Directory = directory },
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 0, actual: replaced.Exit);
        Assert.Equal(expected: "keep me", actual: await File.ReadAllTextAsync(notes));
        AssertManifest(
            replaced.Json,
            directory: directory,
            command: "export pre-defense",
            artifact: "comisia_7.xlsx");
    }

    [Fact]
    public async Task MissingAndAbsentTeacherAreConfigurationErrors()
    {
        using var files = new Fixture();
        var missing = await Capture(() => files.Export.LabDeadlines(
            new(),
            files.Output,
            new() { Json = true },
            new()));
        Assert.Equal(expected: 3, actual: missing.Exit);
        Assert.Contains(expectedSubstring: "--profile", actualString: missing.Stderr);
        var absent = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            new()
            {
                Project = files.Path,
                Profile = "Nartea Nichita",
            }));
        Assert.Equal(expected: 3, actual: absent.Exit);
        Assert.Contains(expectedSubstring: "absent", actualString: absent.Stderr);
    }

    [Fact]
    public async Task MixedSharedLabCombinationsReportExistingCapabilityLimit()
    {
        using var files = new Fixture(mixed: true);
        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 8, actual: result.Exit);
        Assert.Contains(expectedSubstring: "does not support", actualString: result.Stderr);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Fact]
    public async Task ExplicitOutputCollisionPreservesUnrelatedFilesAndReportsPartialWork()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        files.Export.Commissions =
        [
            .. files.Export.Commissions!,
            new()
            {
                Number = 8,
                Room = "102",
                Members = [new() { Name = "Ionescu Maria" }],
            },
        ];
        Directory.CreateDirectory(files.Output.Directory!);
        var collision = Path.Combine(files.Output.Directory!, "comisia_8.xlsx");
        await File.WriteAllTextAsync(path: collision, contents: "caller data");
        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings));
        Assert.Equal(expected: 6, actual: result.Exit);
        Assert.Equal(expected: "partial", actual: result.Json.GetProperty("status").GetString());
        Assert.Equal(expected: "caller data", actual: await File.ReadAllTextAsync(collision));
        Assert.True(File.Exists(Path.Combine(files.Output.Directory!, "comisia_7.xlsx")));
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(files.Output.Directory!, "schedulelib-manifest.json")));
        Assert.Equal(expected: "partial", actual: manifest.RootElement.GetProperty("Status").GetString());
        Assert.Single(manifest.RootElement.GetProperty("Artifacts").EnumerateArray());
    }

    [Fact]
    public async Task OutputLeaseReportsBusyAndCancelledCommandsPublishNothing()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        using (var lease = await RunOutput.Create(
                   output: files.Output.Directory,
                   command: "export pre-defense",
                   runId: "holder",
                   CancellationToken.None))
        {
            var busy = await Capture(() => files.Export.PreDefense(
                files.Output,
                new() { Json = true },
                files.Settings));
            Assert.Equal(expected: 7, actual: busy.Exit);
        }
        var cancelled = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new() { Json = true },
            files.Settings,
            new(canceled: true)));
        Assert.Equal(expected: 130, actual: cancelled.Exit);
        Assert.Equal(expected: "cancelled", actual: cancelled.Json.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(files.Output.Directory!, "deadlines.xlsx")));
        using var cancellation = new CancellationTokenSource();
        files.Theses.OnRead = cancellation.Cancel;
        var interrupted = await Capture(() => files.Export.PreDefense(
            files.Output,
            new() { Json = true },
            files.Settings,
            cancellation.Token));
        Assert.Equal(expected: 130, actual: interrupted.Exit);
        Assert.False(File.Exists(Path.Combine(files.Output.Directory!, "comisia_7.xlsx")));
    }

    private static void AssertManifest(
        JsonElement result,
        string directory,
        string command,
        string artifact)
    {
        Assert.Equal(expected: 1, actual: result.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(expected: command, actual: result.GetProperty("command").GetString());
        Assert.Equal(expected: 1, actual: result.GetProperty("data").GetProperty("workbookCount").GetInt32());
        Assert.Equal(
            expected: new[]
            {
                Path.Combine(directory, artifact),
                Path.Combine(directory, "schedulelib-manifest.json"),
            },
            actual: result.GetProperty("outputs").EnumerateArray().Select(x => x.GetString()));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "schedulelib-manifest.json")));
        Assert.Equal(expected: command, actual: manifest.RootElement.GetProperty("Command").GetString());
        Assert.Equal(expected: artifact, actual: manifest.RootElement.GetProperty("Artifacts")[0].GetProperty("Name").GetString());
    }

    private static async Task<(int Exit, JsonElement Json, string Stderr)> Capture(Func<Task<int>> run)
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
            using var doc = JsonDocument.Parse(stdout.ToString());
            return (exit, doc.RootElement.Clone(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-teacher-{Guid.NewGuid():N}");

        public SourceArguments Source
        {
            get
            {
                return new()
                {
                    DataDirectory = System.IO.Path.Combine(Path, "data"),
                    NoCache = true,
                };
            }
        }

        public OutputArguments Output => new() { Directory = System.IO.Path.Combine(Path, "results") };

        public SettingsArguments Settings
        {
            get
            {
                return new()
                {
                    Project = Path,
                    Profile = "Curmanschii Anton",
                };
            }
        }

        public string SourceFile
        {
            get
            {
                return System.IO.Path.Combine(
                    Path,
                    "data",
                    "2026_sem1",
                    "zi",
                    "master.xlsx");
            }
        }

        public FakeTheses Theses { get; } = new();

        public FixtureExport Export { get; }

        public Fixture(bool mixed = false)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SourceFile)!);
            using var book = new XLWorkbook(
                System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "fixtures",
                    "2025_sem2",
                    "zi",
                    "master.xlsx"));
            var sheet = book.Worksheet(1);
            sheet.Range("D4:E18").Clear(XLClearOptions.Contents);
            sheet.Cell("B2").Value = "Sem. I\n01.09.2026 – 13.12.2026";
            sheet.Cell("D3").Value = "MIA 2601";
            sheet.Cell("E3").Value = "IASD 2601";
            sheet.Cell("D6").Value = "Machine Learning (lab), A. Curmanschii, 237/4";
            sheet.Cell("E6").Value = "Proiect practic de știința datelor (lab), V. Ursachi, 219/4a";
            if (mixed)
            {
                sheet.Cell("D8").Value = "Machine Learning (lab, sb.1), A. Curmanschii, 237/4";
            }

            book.SaveAs(SourceFile);
            Export = new(Theses);
        }

        public void ConfigureCommission()
        {
            Export.Commissions =
            [
                new()
                {
                    Number = 7,
                    Room = "101",
                    Members =
                    [
                        new()
                        {
                            Name = "Curmanscii Anton",
                            IsPresident = true,
                        },
                    ],
                },
            ];
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixtureExport(FakeTheses theses) : ExportCommands
    {
        public Commission<CommissionMember>[]? Commissions { get; set; }

        public bool RejectSourceReads { get; set; }

        public int SourceReads { get; private set; }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            // The coded academic calendar intentionally has no master ranges. Supply a synthetic
            // master range for workbook evidence without changing production academic settings.
            var dates = new CurrentYearSemesterIntervalBuilder();
            dates.Scope(x =>
            {
                x.AttendanceMode(AttendanceMode.Zi);
                x.QualificationType(QualificationType.Master);
                x.Semester(Semester.Sem1);
                x.LessonsStart(new DateOnly(
                        year: 2026,
                        month: 9,
                        day: 1));
                x.LessonsEndInclusive(new DateOnly(
                        year: 2026,
                        month: 12,
                        day: 13));
                x.Range().Grade(new(1));
            });
            services.AddSingleton(dates.Build());
            services.Configure<PreDefenseOptions>(x =>
            {
                x.Commissions = Commissions;
                x.AvrStudents = [("Popescu Ion", "I2301")];
            });
            services.AddSingleton<IThesesFileProvider>(theses);
            if (RejectSourceReads)
            {
                services.AddSingleton<IScheduleInitializer>(new RejectingInitializer(this));
            }
        }

        private sealed class RejectingInitializer(FixtureExport owner) : IScheduleInitializer
        {
            public Task Initialize(ScheduleBuilder builder, CancellationToken cancellationToken)
            {
                owner.SourceReads++;
                throw new InvalidOperationException(
                    "Invalid row configuration must be rejected before reading schedule sources.");
            }
        }
    }

    private sealed class FakeTheses : IThesesFileProvider
    {
        public int Reads { get; private set; }

        public Action? OnRead { get; set; }

        public ValueTask<Stream> Open(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            OnRead?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            using var book = new XLWorkbook();
            foreach (var name in new[]
                     {
                         "An",
                         "Licenta",
                         "Master",
                     })
            {
                var sheet = book.AddWorksheet(name);
                string[] headers =
                [
                    "Nr.", "Grupa", "Student", "Numele conducatorului stiintific", "Tema in rusa", "Tema in romana",
                    "Tema in engleza",
                ];
                for (var i = 0; i < headers.Length; i++)
                {
                    sheet.Cell(row: 1, column: i + 1).Value = headers[i];
                }

                if (name != "An")
                {
                    continue;
                }

                for (var row = 2; row <= 3; row++)
                {
                    sheet.Cell(row: row, column: 1).Value = row - 1;
                    sheet.Cell(row: row, column: 2).Value = "I2301";
                    sheet.Cell(row: row, column: 3).Value = row == 2 ? "Popescu Ion" : "Ionescu Maria";
                    sheet.Cell(row: row, column: 4).Value = row == 2 ? "Curmanschi Anton" : "Ionescu Maria";
                    sheet.Cell(row: row, column: 6).Value = "Tema sintetica";
                }
            }
            var stream = new MemoryStream();
            book.SaveAs(stream);
            stream.Position = 0;
            return ValueTask.FromResult<Stream>(stream);
        }
    }
}
