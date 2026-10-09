using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Core.Services;
using ScheduleLib;
using ScheduleLib.Dates;
using ScheduleLib.Theses.Parsing;
using Xunit;

public sealed class TeacherExportTests
{
    [Theory]
    [InlineData("lab-deadlines")]
    [InlineData("pre-defense")]
    public async Task HelpAndInvalidOptionsDoNotLoadInputs(string command)
    {
        Assert.Equal(0, await CliHost.Run(["export", command, "--help"]));
        Assert.Equal(2, await CliHost.Run(["export", command, "--unknown", "--json"]));
    }

    [Fact]
    public async Task DeadlinesSelectTeacherAndApplyProfileSettingsToWorkbook()
    {
        using var files = new Fixture();
        await File.WriteAllTextAsync(Path.Combine(files.Path, "schedulelib.json"), """
            {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"maxTaskRows":9,"columnWidth":8}},
             "profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":3,"columnWidth":12,"lessonDelayLimit":5},
             "LabTasksDatabaseConfig":{"sources":[]}}}}
            """);
        var originalSource = await File.ReadAllBytesAsync(files.SourceFile);
        var result = await Capture(() => files.Export.LabDeadlines(files.Source, files.Output, new() { Json = true }, files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Contains("every period", result.Stderr);
        Assert.Contains("partition/course combinations", result.Stderr);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "deadlines.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Contains("MIA2601", sheet.Name);
        Assert.DoesNotContain("IASD2601", sheet.Name);
        Assert.Equal("01.09", sheet.Cell(1, 2).GetString());
        Assert.Equal(12, sheet.Column(2).Width);
        Assert.False(sheet.Cell(4, 2).IsEmpty());
        Assert.True(sheet.Cell(5, 2).IsEmpty());
        Assert.Contains("-5", sheet.Cell(2, 2).FormulaA1);
        Assert.Equal(6, sheet.ConditionalFormats.Count());
        AssertManifest(result.Json, files.Output.Directory!, "export lab-deadlines", "deadlines.xlsx");
        Assert.Equal(originalSource, await File.ReadAllBytesAsync(files.SourceFile));
    }

    [Fact]
    public async Task PreDefenseUsesExistingMappingAndWriterForCommissionContents()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        var result = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Equal(1, files.Theses.Reads);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "comisia_7.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Equal("Comisia 7", sheet.Name);
        Assert.Equal("Nume, prenume student", sheet.Cell(1, 2).GetString());
        Assert.Equal("Popescu Ion", sheet.Cell(2, 2).GetString());
        Assert.Equal("I2301", sheet.Cell(2, 3).GetString());
        Assert.Equal("Curmanschii Anton", sheet.Cell(2, 4).GetString());
        Assert.Equal("Laborator AVR", sheet.Cell(2, 5).GetString());
        Assert.Equal("da/da", sheet.Cell(2, 6).GetString());
        Assert.Equal("Tema sintetica", sheet.Cell(2, 7).GetString());
        Assert.Equal(10, sheet.Cell(2, 8).GetValue<int>());
        Assert.Equal(2, sheet.LastRowUsed()!.RowNumber());
        Assert.Contains("not part of any commission", result.Stderr);
        Assert.Single(result.Json.GetProperty("warnings").EnumerateArray());
        AssertManifest(result.Json, files.Output.Directory!, "export pre-defense", "comisia_7.xlsx");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrEmptyCommissionConfigurationIsPrerequisite(bool explicitEmpty)
    {
        using var files = new Fixture();
        if (explicitEmpty) files.Export.Commissions = [];
        var result = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings));
        Assert.Equal(3, result.Exit);
        Assert.Contains("commission", result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs").EnumerateArray());
        Assert.Equal(0, files.Theses.Reads);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Fact]
    public async Task ACommissionWithoutMembersIsAlsoAPrerequisite()
    {
        using var files = new Fixture();
        files.Export.Commissions = [new() { Number = 7, Room = "101", Members = [] }];
        var result = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings));
        Assert.Equal(3, result.Exit);
        Assert.Equal(0, files.Theses.Reads);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Fact]
    public async Task DefaultsAreIsolatedAndOwnedReplacementPreservesOtherFiles()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        var first = await Capture(() => files.Export.PreDefense(new(), new() { Json = true }, files.Settings));
        var second = await Capture(() => files.Export.PreDefense(new(), new() { Json = true }, files.Settings));
        Assert.Equal(0, first.Exit);
        Assert.Equal(0, second.Exit);
        var directory = first.Json.GetProperty("data").GetProperty("outputDirectory").GetString()!;
        Assert.Equal(Path.Combine(files.Path, "output"), Path.GetDirectoryName(directory));
        Assert.NotEqual(directory, second.Json.GetProperty("data").GetProperty("outputDirectory").GetString());
        var notes = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(notes, "keep me");
        var replaced = await Capture(() => files.Export.PreDefense(new() { Directory = directory }, new() { Json = true }, files.Settings));
        Assert.Equal(0, replaced.Exit);
        Assert.Equal("keep me", await File.ReadAllTextAsync(notes));
        AssertManifest(replaced.Json, directory, "export pre-defense", "comisia_7.xlsx");
    }

    [Fact]
    public async Task MissingAndAbsentTeacherAreConfigurationErrors()
    {
        using var files = new Fixture();
        var missing = await Capture(() => files.Export.LabDeadlines(new(), files.Output, new() { Json = true }, new()));
        Assert.Equal(3, missing.Exit);
        Assert.Contains("--profile", missing.Stderr);
        var absent = await Capture(() => files.Export.LabDeadlines(files.Source, files.Output, new() { Json = true },
            new() { Project = files.Path, Profile = "Nartea Nichita" }));
        Assert.Equal(3, absent.Exit);
        Assert.Contains("absent", absent.Stderr);
    }

    [Fact]
    public async Task MixedSharedLabCombinationsReportExistingCapabilityLimit()
    {
        using var files = new Fixture(mixed: true);
        var result = await Capture(() => files.Export.LabDeadlines(files.Source, files.Output, new() { Json = true }, files.Settings));
        Assert.Equal(8, result.Exit);
        Assert.Contains("does not support", result.Stderr);
        Assert.False(Directory.Exists(files.Output.Directory));
    }

    [Fact]
    public async Task ExplicitOutputCollisionPreservesUnrelatedFilesAndReportsPartialWork()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        files.Export.Commissions = [.. files.Export.Commissions!, new() { Number = 8, Room = "102", Members = [new() { Name = "Ionescu Maria" }] }];
        Directory.CreateDirectory(files.Output.Directory!);
        var collision = Path.Combine(files.Output.Directory!, "comisia_8.xlsx");
        await File.WriteAllTextAsync(collision, "caller data");
        var result = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings));
        Assert.Equal(6, result.Exit);
        Assert.Equal("partial", result.Json.GetProperty("status").GetString());
        Assert.Equal("caller data", await File.ReadAllTextAsync(collision));
        Assert.True(File.Exists(Path.Combine(files.Output.Directory!, "comisia_7.xlsx")));
        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(files.Output.Directory!, "schedulelib-manifest.json")));
        Assert.Equal("partial", manifest.RootElement.GetProperty("Status").GetString());
        Assert.Single(manifest.RootElement.GetProperty("Artifacts").EnumerateArray());
    }

    [Fact]
    public async Task OutputLeaseReportsBusyAndCancelledCommandsPublishNothing()
    {
        using var files = new Fixture();
        files.ConfigureCommission();
        using (var lease = await RunOutput.Create(files.Output.Directory, "export pre-defense", "holder", CancellationToken.None))
        {
            var busy = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings));
            Assert.Equal(7, busy.Exit);
        }
        var cancelled = await Capture(() => files.Export.LabDeadlines(files.Source, files.Output, new() { Json = true }, files.Settings, new(canceled: true)));
        Assert.Equal(130, cancelled.Exit);
        Assert.Equal("cancelled", cancelled.Json.GetProperty("status").GetString());
        Assert.False(File.Exists(Path.Combine(files.Output.Directory!, "deadlines.xlsx")));
        using var cancellation = new CancellationTokenSource();
        files.Theses.OnRead = cancellation.Cancel;
        var interrupted = await Capture(() => files.Export.PreDefense(files.Output, new() { Json = true }, files.Settings, cancellation.Token));
        Assert.Equal(130, interrupted.Exit);
        Assert.False(File.Exists(Path.Combine(files.Output.Directory!, "comisia_7.xlsx")));
    }

    private static void AssertManifest(JsonElement result, string directory, string command, string artifact)
    {
        Assert.Equal(1, result.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(command, result.GetProperty("command").GetString());
        Assert.Equal(1, result.GetProperty("data").GetProperty("workbookCount").GetInt32());
        Assert.Equal(new[] { Path.Combine(directory, artifact), Path.Combine(directory, "schedulelib-manifest.json") },
            result.GetProperty("outputs").EnumerateArray().Select(x => x.GetString()));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "schedulelib-manifest.json")));
        Assert.Equal(command, manifest.RootElement.GetProperty("Command").GetString());
        Assert.Equal(artifact, manifest.RootElement.GetProperty("Artifacts")[0].GetProperty("Name").GetString());
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
        finally { Console.SetOut(originalOut); Console.SetError(originalError); }
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-teacher-{Guid.NewGuid():N}");
        public SourceArguments Source => new() { DataDirectory = System.IO.Path.Combine(Path, "data"), NoCache = true };
        public OutputArguments Output => new() { Directory = System.IO.Path.Combine(Path, "results") };
        public SettingsArguments Settings => new() { Project = Path, Profile = "Curmanschii Anton" };
        public string SourceFile => System.IO.Path.Combine(Path, "data", "2026_sem1", "zi", "master.xlsx");
        public FakeTheses Theses { get; } = new();
        public FixtureExport Export { get; }
        public Fixture(bool mixed = false)
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SourceFile)!);
            using var book = new XLWorkbook(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "2025_sem2", "zi", "master.xlsx"));
            var sheet = book.Worksheet(1);
            sheet.Range("D4:E18").Clear(XLClearOptions.Contents);
            sheet.Cell("B2").Value = "Sem. I\n01.09.2026 – 13.12.2026";
            sheet.Cell("D3").Value = "MIA 2601";
            sheet.Cell("E3").Value = "IASD 2601";
            sheet.Cell("D6").Value = "Machine Learning (lab), A. Curmanschii, 237/4";
            sheet.Cell("E6").Value = "Proiect practic de știința datelor (lab), V. Ursachi, 219/4a";
            if (mixed) sheet.Cell("D8").Value = "Machine Learning (lab, sb.1), A. Curmanschii, 237/4";
            book.SaveAs(SourceFile);
            Export = new(Theses);
        }
        public void ConfigureCommission() => Export.Commissions = [new() { Number = 7, Room = "101", Members = [new() { Name = "Curmanscii Anton", IsPresident = true }] }];
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixtureExport(FakeTheses theses) : ExportCommands
    {
        public Commission<CommissionMember>[]? Commissions { get; set; }
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
                x.LessonsStart(new DateOnly(2026, 9, 1));
                x.LessonsEndInclusive(new DateOnly(2026, 12, 13));
                x.Range().Grade(new(1));
            });
            services.AddSingleton(dates.Build());
            services.Configure<PreDefenseOptions>(x => { x.Commissions = Commissions; x.AvrStudents = [("Popescu Ion", "I2301")]; });
            services.AddSingleton<IThesesFileProvider>(theses);
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
            foreach (var name in new[] { "An", "Licenta", "Master" })
            {
                var sheet = book.AddWorksheet(name);
                string[] headers = ["Nr.", "Grupa", "Student", "Numele conducatorului stiintific", "Tema in rusa", "Tema in romana", "Tema in engleza"];
                for (var i = 0; i < headers.Length; i++) sheet.Cell(1, i + 1).Value = headers[i];
                if (name != "An") continue;
                for (var row = 2; row <= 3; row++)
                {
                    sheet.Cell(row, 1).Value = row - 1;
                    sheet.Cell(row, 2).Value = "I2301";
                    sheet.Cell(row, 3).Value = row == 2 ? "Popescu Ion" : "Ionescu Maria";
                    sheet.Cell(row, 4).Value = row == 2 ? "Curmanschi Anton" : "Ionescu Maria";
                    sheet.Cell(row, 6).Value = "Tema sintetica";
                }
            }
            var stream = new MemoryStream();
            book.SaveAs(stream);
            stream.Position = 0;
            return ValueTask.FromResult<Stream>(stream);
        }
    }
}
