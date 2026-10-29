using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public sealed class FreeRoomsTests
{
    [Theory]
    [InlineData(
        new[]
        {
            "export",
            "free-rooms",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "export",
            "free-rooms",
            "--unknown",
        },
        2)]
    [InlineData(
        new[]
        {
            "export",
            "free-rooms",
            "--output",
        },
        2)]
    public async Task HelpAndParseErrorsDoNotInitialize(string[] args, int expected)
    {
        // The invocation directory contains no schedule data; these must still succeed/fail as parsing requires.
        var exit = await CliHost.Run(args);
        Assert.Equal(expected: expected, actual: exit);
    }

    [Fact]
    public async Task WorkbookContainsBothParityWorksheetsAndFreeRooms()
    {
        using var temp = new TempDirectory();
        var result = await Export(temp.Path);
        Assert.Equal(
            expected: 0,
            actual: result.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(
            expected: "succeeded",
            actual: result.GetProperty("status")
                .GetString());
        var data = result.GetProperty("data");
        Assert.Equal(
            expected: 127,
            actual: data.GetProperty("lessonCount")
                .GetInt32());
        Assert.Equal(
            expected: new[]
            {
                "par",
                "impar",
            },
            actual: data.GetProperty("worksheets")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
        Assert.Equal(
            expected: temp.Path,
            actual: data.GetProperty("outputDirectory")
                .GetString());
        Assert.Equal(
            expected: new[]
            {
                Path.Combine(temp.Path, "free-rooms.xlsx"),
                Path.Combine(temp.Path, "schedulelib-manifest.json"),
            },
            actual: result.GetProperty("outputs")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
        Assert.Contains(
            "Free rooms inspect every weekly period, not only the latest one.",
            result.GetProperty("warnings")
                .EnumerateArray()
                .Select(x => x.GetString() ?? ""));

        string CalculateFile()
        {
            string file = Path.Combine(temp.Path, "free-rooms.xlsx");
            return file;
        }

        using var workbook = new XLWorkbook(CalculateFile());
        Assert.Equal(
            expected: new[]
            {
                "par",
                "impar",
            },
            actual: workbook.Worksheets.Select(x => x.Name)
                .ToArray());
        foreach (var sheet in workbook.Worksheets)
        {
            // Six days that have lessons, each a single-cell header followed by seven time slots.
            Assert.Equal(
                expected: new[]
                {
                    (Row: 1, Day: "Luni"),
                    (Row: 10, Day: "Marţi"),
                    (Row: 19, Day: "Miercuri"),
                    (Row: 28, Day: "Joi"),
                    (Row: 37, Day: "Vineri"),
                    (Row: 46, Day: "Sâmbătă"),
                },
                actual: sheet.RowsUsed()
                    .Where(x => x.CellsUsed()
                            .Count()
                        == 1)
                    .Select(x => (Row: x.RowNumber(), Day: x.Cell(1)
                        .GetFormattedString()))
                    .ToArray());
            Assert.Equal(
                expected: 53,
                actual: sheet.LastRowUsed()!
                    .RowNumber());
            string[] expected2 = new[]
            {
                "8:00-9:30",
                "9:45-11:15",
                "11:30-13:00",
                "13:15-14:45",
                "15:00-16:30",
                "16:45-18:15",
                "18:30-20:00",
            };
            var column = sheet.Column(1);
            int firstRow2 = 2;
            int lastRow2 = 8;
            var cells = column.Cells(firstRow: firstRow2, lastRow: lastRow2);
            Func<IXLCell, string> selector = x => x.GetFormattedString();
            var select = cells.Select(selector);
            string[] actual2 = select.ToArray();
            Assert.Equal(
                expected: expected2,
                actual: actual2);
        }

        var even = workbook.Worksheet("par");
        var odd = workbook.Worksheet("impar");
        string[] expected3 = new[]
        {
            "8:00-9:30",
            "113/4",
            "143/4",
            "145/4",
            "145a/4",
            "213a/4",
            "214/4",
            "218/4",
            "222/4",
            "237/4",
            "239/4",
            "251/4",
            "326/4",
            "350/4",
            "401/4",
            "404/4",
            "415/4",
            "419/4",
            "423/4",
            "218/4a",
            "219/4a",
        };
        string[] actual3 = Row(even, 2);
        Assert.Equal(
            expected: expected3,
            actual: actual3);
        string[] expected4 = Row(even, 2);
        string[] actual4 = Row(odd, 2);
        Assert.Equal(expected: expected4, actual: actual4);
        // On Wednesday 13:15-14:45 both parities share the same rooms.
        Assert.Equal(
            expected: new[]
            {
                "13:15-14:45",
                "113/4",
                "143/4",
                "145a/4",
                "213a/4",
                "214/4",
                "218/4",
                "222/4",
                "237/4",
                "239/4",
                "326/4",
                "401/4",
                "404/4",
                "415/4",
                "419/4",
                "423/4",
                "216a/4a",
                "218/4a",
            },
            actual: Row(even, 23));
        string[] expected5 = Row(even, 23);
        string[] actual5 = Row(odd, 23);
        Assert.Equal(expected: expected5, actual: actual5);
        // On Wednesday 15:00-16:30 the two parities occupy different rooms: 251/4 is free only in even weeks.
        Assert.Equal(
            expected: new[]
            {
                "15:00-16:30",
                "143/4",
                "145a/4",
                "213a/4",
                "214/4",
                "218/4",
                "222/4",
                "237/4",
                "239/4",
                "251/4",
                "326/4",
                "350/4",
                "401/4",
                "404/4",
                "415/4",
                "419/4",
                "423/4",
                "216a/4a",
                "218/4a",
            },
            actual: Row(even, 24));
        string[] expected6 = new[]
        {
            "15:00-16:30",
            "143/4",
            "145a/4",
            "213a/4",
            "214/4",
            "218/4",
            "222/4",
            "237/4",
            "239/4",
            "326/4",
            "350/4",
            "401/4",
            "404/4",
            "415/4",
            "419/4",
            "423/4",
            "216a/4a",
            "218/4a",
        };
        string[] actual6 = Row(odd, 24);
        Assert.Equal(
            expected: expected6,
            actual: actual6);

        string CalculatePath()
        {
            string path = Path.Combine(temp.Path, "schedulelib-manifest.json");
            return path;
        }

        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(CalculatePath()));
        Assert.Equal(
            expected: "export free-rooms",
            actual: manifest.RootElement.GetProperty("Command")
                .GetString());
        Assert.Equal(
            expected: "succeeded",
            actual: manifest.RootElement.GetProperty("Status")
                .GetString());
        Assert.Equal(
            expected: "free-rooms.xlsx",
            actual: manifest.RootElement.GetProperty("Artifacts")[0]
                .GetProperty("Name")
                .GetString());
        Assert.Equal(
            expected: 64,
            actual: manifest.RootElement.GetProperty("Artifacts")[0]
                .GetProperty("Sha256")
                .GetString()!.Length);
    }

    [Fact]
    public async Task DefaultOutputLivesUnderTheProjectRoot()
    {
        using var temp = new TempDirectory();
        var result = await Export(
            null,
            settings: new()
            {
                Project = temp.Path,
            });
        var directory = result.GetProperty("data")
            .GetProperty("outputDirectory")
            .GetString()!;

        string CalculateExpected()
        {
            string expected2 = Path.Combine(temp.Path, "output");
            return expected2;
        }

        string expected3 = CalculateExpected();
        string? actual2 = Path.GetDirectoryName(directory);
        Assert.Equal(expected: expected3, actual: actual2);
        string path = Path.Combine(directory, "free-rooms.xlsx");
        bool condition = File.Exists(path);
        Assert.True(condition);
        string path2 = Path.Combine(directory, "schedulelib-manifest.json");
        bool condition2 = File.Exists(path2);
        Assert.True(condition2);
        Assert.Equal(
            expected: new[]
            {
                Path.Combine(directory, "free-rooms.xlsx"),
                Path.Combine(directory, "schedulelib-manifest.json"),
            },
            actual: result.GetProperty("outputs")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
    }

    [Fact]
    public async Task ScheduleWithoutLessonsSucceedsWithAnEmptyWorkbook()
    {
        using var source = new TempDirectory();
        var zi = Path.Combine(
            source.Path,
            "2025_sem2",
            "zi");
        Directory.CreateDirectory(zi);

        string CalculateFile()
        {
            string file = Path.Combine(
                AppContext.BaseDirectory,
                "fixtures",
                "2025_sem2",
                "zi",
                "master.xlsx");
            return file;
        }

        using (var workbook = new XLWorkbook(
                   CalculateFile()))
        {
            workbook.Worksheet(1)
                .Rows(firstRow: 4, lastRow: 18)
                .Delete();
            var xLWorkbook = workbook;
            string file3 = Path.Combine(zi, "master.xlsx");
            xLWorkbook.SaveAs(file3);
        }
        using var output = new TempDirectory();
        var result = await Export(outputDirectory: output.Path, dataDirectory: source.Path);
        Assert.Equal(
            expected: 0,
            actual: result.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(
            expected: 0,
            actual: result.GetProperty("data")
                .GetProperty("lessonCount")
                .GetInt32());
        Assert.Contains(
            "The schedule contains no weekly lessons; the workbook has no day rows.",
            result.GetProperty("warnings")
                .EnumerateArray()
                .Select(x => x.GetString() ?? ""));

        string CalculateFile2()
        {
            string file2 = Path.Combine(output.Path, "free-rooms.xlsx");
            return file2;
        }

        using var book = new XLWorkbook(CalculateFile2());
        Assert.Equal(
            expected: new[]
            {
                "par",
                "impar",
            },
            actual: book.Worksheets.Select(x => x.Name)
                .ToArray());
        Assert.All(book.Worksheets, sheet => Assert.Null(sheet.LastRowUsed()));
    }

    [Fact]
    public async Task CancelledExportReturns130BeforeLoading()
    {
        using var temp = new TempDirectory();

        CancellationToken CalculateCancellationToken()
        {
            var cancellationToken = new CancellationToken(canceled: true);
            return cancellationToken;
        }

        Assert.Equal(
            expected: 130,
            actual: await new FixtureExport().FreeRooms(
                new(),
                new()
                {
                    Directory = temp.Path,
                },
                new(),
                CalculateCancellationToken()));

        IEnumerable<string> CalculateCollection()
        {
            var collection = Directory.EnumerateFileSystemEntries(temp.Path);
            return collection;
        }

        Assert.Empty(CalculateCollection());
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
            Assert.Equal(
                expected: 3,
                actual: await new FixtureExport().FreeRooms(
                    new()
                    {
                        NoCache = true,
                        DataDirectory = Path.Combine(
                            Path.GetTempPath(),
                            Guid.NewGuid()
                                .ToString()),
                    },
                    new()
                    {
                        Directory = temp.Path,
                    },
                    new()
                    {
                        Json = true,
                    }));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: 3,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
            Assert.Equal(
                expected: "failed",
                actual: result.RootElement.GetProperty("status")
                    .GetString());

            bool CalculateCondition()
            {
                string path = Path.Combine(temp.Path, "schedulelib-manifest.json");
                bool condition = File.Exists(path);
                return condition;
            }

            Assert.False(CalculateCondition());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string[] Row(IXLWorksheet sheet, int row)
    {
        return sheet.Row(row)
            .Cells(
                firstColumn: 1,
                lastColumn: sheet.LastColumnUsed()!
                    .ColumnNumber())
            .Select(x => x.GetFormattedString())
            .Where(x => x.Length != 0)
            .ToArray();
    }

    private static async Task<JsonElement> Export(
        string? outputDirectory,
        string? dataDirectory = null,
        SettingsArguments? settings = null)
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureExport().FreeRooms(
                new()
                {
                    NoCache = true,
                    DataDirectory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "fixtures"),
                },
                new()
                {
                    Directory = outputDirectory,
                },
                new()
                {
                    Json = true,
                },
                CancellationToken.None,
                settings);
            Assert.Equal(expected: 0, actual: exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: 1,
                actual: document.RootElement.GetProperty("schemaVersion")
                    .GetInt32());
            Assert.Equal(
                expected: "export free-rooms",
                actual: document.RootElement.GetProperty("command")
                    .GetString());
            return document.RootElement.Clone();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CalculatePath2());

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static string CalculatePath2()
        {
            string path2 = $"schedulelib-free-rooms-{Guid.NewGuid():N}";
            return path2;
        }
    }

    private sealed class FixtureExport : ExportCommands
    {
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
        }
    }
}
