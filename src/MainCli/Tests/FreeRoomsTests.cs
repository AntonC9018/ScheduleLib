using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Xunit;

public sealed class FreeRoomsTests
{
    [Theory]
    [InlineData(new[] { "export", "free-rooms", "--help" }, 0)]
    [InlineData(new[] { "export", "free-rooms", "--unknown" }, 2)]
    [InlineData(new[] { "export", "free-rooms", "--output" }, 2)]
    public async Task HelpAndParseErrorsDoNotInitialize(string[] args, int expected)
    {
        // The invocation directory contains no schedule data; these must still succeed/fail as parsing requires.
        Assert.Equal(expected, await CliHost.Run(args));
    }

    [Fact]
    public async Task WorkbookContainsBothParityWorksheetsAndFreeRooms()
    {
        using var temp = new TempDirectory();
        var result = await Export(temp.Path);
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        var data = result.GetProperty("data");
        Assert.Equal(127, data.GetProperty("lessonCount").GetInt32());
        Assert.Equal(new[] { "par", "impar" }, data.GetProperty("worksheets").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Equal(temp.Path, data.GetProperty("outputDirectory").GetString());
        Assert.Equal(
            new[] { Path.Combine(temp.Path, "free-rooms.xlsx"), Path.Combine(temp.Path, "schedulelib-manifest.json") },
            result.GetProperty("outputs").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Contains("Free rooms inspect every weekly period, not only the latest one.",
            result.GetProperty("warnings").EnumerateArray().Select(x => x.GetString() ?? ""));

        using var workbook = new XLWorkbook(Path.Combine(temp.Path, "free-rooms.xlsx"));
        Assert.Equal(new[] { "par", "impar" }, workbook.Worksheets.Select(x => x.Name).ToArray());
        foreach (var sheet in workbook.Worksheets)
        {
            // Six days that have lessons, each a single-cell header followed by seven time slots.
            Assert.Equal(
                new[] { (Row: 1, Day: "Luni"), (Row: 10, Day: "Marţi"), (Row: 19, Day: "Miercuri"), (Row: 28, Day: "Joi"), (Row: 37, Day: "Vineri"), (Row: 46, Day: "Sâmbătă") },
                sheet.RowsUsed().Where(x => x.CellsUsed().Count() == 1)
                    .Select(x => (Row: x.RowNumber(), Day: x.Cell(1).GetFormattedString())).ToArray());
            Assert.Equal(53, sheet.LastRowUsed()!.RowNumber());
            Assert.Equal(new[] { "8:00-9:30", "9:45-11:15", "11:30-13:00", "13:15-14:45", "15:00-16:30", "16:45-18:15", "18:30-20:00" },
                sheet.Column(1).Cells(2, 8).Select(x => x.GetFormattedString()).ToArray());
        }

        var even = workbook.Worksheet("par");
        var odd = workbook.Worksheet("impar");
        Assert.Equal(
            new[] { "8:00-9:30", "113/4", "143/4", "145/4", "145a/4", "213a/4", "214/4", "218/4", "222/4", "237/4", "239/4", "251/4", "326/4", "350/4", "401/4", "404/4", "415/4", "419/4", "423/4", "218/4a", "219/4a" },
            Row(even, 2));
        Assert.Equal(Row(even, 2), Row(odd, 2));
        // On Wednesday 13:15-14:45 both parities share the same rooms.
        Assert.Equal(
            new[] { "13:15-14:45", "113/4", "143/4", "145a/4", "213a/4", "214/4", "218/4", "222/4", "237/4", "239/4", "326/4", "401/4", "404/4", "415/4", "419/4", "423/4", "216a/4a", "218/4a" },
            Row(even, 23));
        Assert.Equal(Row(even, 23), Row(odd, 23));
        // On Wednesday 15:00-16:30 the two parities occupy different rooms: 251/4 is free only in even weeks.
        Assert.Equal(
            new[] { "15:00-16:30", "143/4", "145a/4", "213a/4", "214/4", "218/4", "222/4", "237/4", "239/4", "251/4", "326/4", "350/4", "401/4", "404/4", "415/4", "419/4", "423/4", "216a/4a", "218/4a" },
            Row(even, 24));
        Assert.Equal(
            new[] { "15:00-16:30", "143/4", "145a/4", "213a/4", "214/4", "218/4", "222/4", "237/4", "239/4", "326/4", "350/4", "401/4", "404/4", "415/4", "419/4", "423/4", "216a/4a", "218/4a" },
            Row(odd, 24));

        using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(temp.Path, "schedulelib-manifest.json")));
        Assert.Equal("export free-rooms", manifest.RootElement.GetProperty("Command").GetString());
        Assert.Equal("succeeded", manifest.RootElement.GetProperty("Status").GetString());
        Assert.Equal("free-rooms.xlsx", manifest.RootElement.GetProperty("Artifacts")[0].GetProperty("Name").GetString());
        Assert.Equal(64, manifest.RootElement.GetProperty("Artifacts")[0].GetProperty("Sha256").GetString()!.Length);
    }

    [Fact]
    public async Task DefaultOutputLivesUnderTheProjectRoot()
    {
        using var temp = new TempDirectory();
        var result = await Export(null, settings: new() { Project = temp.Path });
        var directory = result.GetProperty("data").GetProperty("outputDirectory").GetString()!;
        Assert.Equal(Path.Combine(temp.Path, "output"), Path.GetDirectoryName(directory));
        Assert.True(File.Exists(Path.Combine(directory, "free-rooms.xlsx")));
        Assert.True(File.Exists(Path.Combine(directory, "schedulelib-manifest.json")));
        Assert.Equal(
            new[] { Path.Combine(directory, "free-rooms.xlsx"), Path.Combine(directory, "schedulelib-manifest.json") },
            result.GetProperty("outputs").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task ScheduleWithoutLessonsSucceedsWithAnEmptyWorkbook()
    {
        using var source = new TempDirectory();
        var zi = Path.Combine(source.Path, "2025_sem2", "zi");
        Directory.CreateDirectory(zi);
        using (var workbook = new XLWorkbook(Path.Combine(AppContext.BaseDirectory, "fixtures", "2025_sem2", "zi", "master.xlsx")))
        {
            workbook.Worksheet(1).Rows(4, 18).Delete();
            workbook.SaveAs(Path.Combine(zi, "master.xlsx"));
        }
        using var output = new TempDirectory();
        var result = await Export(output.Path, source.Path);
        Assert.Equal(0, result.GetProperty("exitCode").GetInt32());
        Assert.Equal(0, result.GetProperty("data").GetProperty("lessonCount").GetInt32());
        Assert.Contains("The schedule contains no weekly lessons; the workbook has no day rows.",
            result.GetProperty("warnings").EnumerateArray().Select(x => x.GetString() ?? ""));
        using var book = new XLWorkbook(Path.Combine(output.Path, "free-rooms.xlsx"));
        Assert.Equal(new[] { "par", "impar" }, book.Worksheets.Select(x => x.Name).ToArray());
        Assert.All(book.Worksheets, sheet => Assert.Null(sheet.LastRowUsed()));
    }

    [Fact]
    public async Task CancelledExportReturns130BeforeLoading()
    {
        using var temp = new TempDirectory();
        Assert.Equal(130, await new FixtureExport().FreeRooms(new(), new() { Directory = temp.Path }, new(), new CancellationToken(canceled: true)));
        Assert.Empty(Directory.EnumerateFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task MissingSourceIsAConfigurationError()
    {
        using var temp = new TempDirectory();
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(3, await new FixtureExport().FreeRooms(
                new() { NoCache = true, DataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) },
                new() { Directory = temp.Path }, new() { Json = true }));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(3, result.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal("failed", result.RootElement.GetProperty("status").GetString());
            Assert.False(File.Exists(Path.Combine(temp.Path, "schedulelib-manifest.json")));
        }
        finally { Console.SetOut(original); }
    }

    private static string[] Row(IXLWorksheet sheet, int row) => sheet.Row(row).Cells(1, sheet.LastColumnUsed()!.ColumnNumber())
        .Select(x => x.GetFormattedString()).Where(x => x.Length != 0).ToArray();

    private static async Task<JsonElement> Export(string? outputDirectory, string? dataDirectory = null, SettingsArguments? settings = null)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureExport().FreeRooms(
                new() { NoCache = true, DataDirectory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Directory = outputDirectory }, new() { Json = true }, CancellationToken.None, settings);
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("export free-rooms", document.RootElement.GetProperty("command").GetString());
            return document.RootElement.Clone();
        }
        finally { Console.SetOut(original); }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-free-rooms-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private sealed class FixtureExport : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
    }
}
