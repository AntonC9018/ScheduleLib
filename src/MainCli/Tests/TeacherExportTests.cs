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

        string CalculatePath()
        {
            string path2 = Path.Combine(files.Path, "schedulelib.json");
            return path2;
        }

        await File.WriteAllTextAsync(
            path: CalculatePath(),
            contents: """
                      {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"maxTaskRows":9,"columnWidth":8}},
                       "profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":3,"columnWidth":12,"lessonDelayLimit":5},
                       "LabTasksDatabaseConfig":{"sources":[]}}}}
                      """);
        var originalSource = await File.ReadAllBytesAsync(files.SourceFile);
        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Contains(expectedSubstring: "every period", actualString: result.Stderr);
        Assert.Contains(expectedSubstring: "partition/course combinations", actualString: result.Stderr);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "deadlines.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Contains(expectedSubstring: "MIA2601", actualString: sheet.Name);
        Assert.DoesNotContain(expectedSubstring: "IASD2601", actualString: sheet.Name);
        string expected2 = "01.09";
        var cell = sheet.Cell(row: 1, column: 2);
        string actual2 = cell.GetString();
        Assert.Equal(expected: expected2, actual: actual2);
        double expected3 = 12;
        var column2 = sheet.Column(2);
        double actual3 = column2.Width;
        Assert.Equal(expected: expected3, actual: actual3);
        var cell2 = sheet.Cell(row: 4, column: 2);
        bool condition = cell2.IsEmpty();
        Assert.False(condition);
        var cell3 = sheet.Cell(row: 5, column: 2);
        bool condition2 = cell3.IsEmpty();
        Assert.True(condition2);
        string expectedSubstring2 = "-5";
        var cell4 = sheet.Cell(row: 2, column: 2);
        string actualString2 = cell4.FormulaA1;
        Assert.Contains(expectedSubstring: expectedSubstring2, actualString: actualString2);
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
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.True(result.Exit == 0, result.Stderr);
        Assert.Equal(expected: 1, actual: files.Theses.Reads);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "comisia_7.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        Assert.Equal(expected: "Comisia 7", actual: sheet.Name);
        string expected2 = "Nume, prenume student";
        var cell = sheet.Cell(row: 1, column: 2);
        string actual2 = cell.GetString();
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "Popescu Ion";
        var cell2 = sheet.Cell(row: 2, column: 2);
        string actual3 = cell2.GetString();
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "I2301";
        var cell3 = sheet.Cell(row: 2, column: 3);
        string actual4 = cell3.GetString();
        Assert.Equal(expected: expected4, actual: actual4);
        string expected5 = "Curmanschii Anton";
        var cell4 = sheet.Cell(row: 2, column: 4);
        string actual5 = cell4.GetString();
        Assert.Equal(expected: expected5, actual: actual5);
        string expected6 = "Laborator AVR";
        var cell5 = sheet.Cell(row: 2, column: 5);
        string actual6 = cell5.GetString();
        Assert.Equal(expected: expected6, actual: actual6);
        string expected7 = "da/da";
        var cell6 = sheet.Cell(row: 2, column: 6);
        string actual7 = cell6.GetString();
        Assert.Equal(expected: expected7, actual: actual7);
        string expected8 = "Tema sintetica";
        var cell7 = sheet.Cell(row: 2, column: 7);
        string actual8 = cell7.GetString();
        Assert.Equal(expected: expected8, actual: actual8);
        int expected9 = 10;
        var cell8 = sheet.Cell(row: 2, column: 8);
        int actual9 = cell8.GetValue<int>();
        Assert.Equal(expected: expected9, actual: actual9);
        Assert.Equal(expected: 2,
            actual: sheet.LastRowUsed()!
                .RowNumber());
        Assert.Contains(expectedSubstring: "not part of any commission", actualString: result.Stderr);
        Assert.Single(result.Json.GetProperty("warnings")
            .EnumerateArray());
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
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Contains(expectedSubstring: "commission", actualString: result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs")
            .EnumerateArray());
        Assert.Equal(expected: 0, actual: files.Theses.Reads);

        bool CalculateCondition()
        {
            bool condition = Directory.Exists(files.Output.Directory);
            return condition;
        }

        Assert.False(CalculateCondition());
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
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);

        bool CalculateCondition()
        {
            bool condition = Directory.Exists(files.Output.Directory);
            return condition;
        }

        Assert.False(CalculateCondition());
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
                    Members =
                    [
                        new()
                        {
                            Name = "Ionescu Maria",
                        },
                    ],
                },
            ],
            "member-within" or "remapped-within" =>
            [
                new()
                {
                    Number = 7,
                    Room = "101",
                    Members =
                    [
                        .. commission.Members, new()
                        {
                            Name = repeatedName,
                        },
                    ],
                },
            ],
            "empty-name" or "invalid-name" =>
            [
                new()
                {
                    Number = 7,
                    Room = "101",
                    Members =
                    [
                        .. commission.Members, new()
                        {
                            Name = duplicate == "empty-name" ? "" : "Invalid",
                        },
                    ],
                },
            ],
            _ =>
            [
                commission, new()
                {
                    Number = 8,
                    Room = "102",
                    Members =
                    [
                        new()
                        {
                            Name = repeatedName,
                        },
                    ],
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
            new()
            {
                Json = true,
            },
            files.Settings));

        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 3,
            actual: result.Json.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(expected: "failed",
            actual: result.Json.GetProperty("status")
                .GetString());
        Assert.Equal(expected: "export pre-defense",
            actual: result.Json.GetProperty("command")
                .GetString());
        var error = Assert.Single(result.Json.GetProperty("errors")
                .EnumerateArray())
            .GetString();
        Assert.Contains(
            expectedSubstring: duplicate switch
            {
                "number" => "commission number 7",
                "empty-name" or "invalid-name" => "commission 7 member 2 has an invalid name",
                _ => "commission members after name remapping",
            },
            actualString: error);
        Assert.Contains(error!, result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs")
            .EnumerateArray());
        Assert.Empty(result.Json.GetProperty("warnings")
            .EnumerateArray());
        Assert.Equal(expected: 0,
            actual: result.Json.GetProperty("data")
                .GetProperty("workbookCount")
                .GetInt32());
        Assert.Equal(expected: JsonValueKind.Null,
            actual: result.Json.GetProperty("data")
                .GetProperty("outputDirectory").ValueKind);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.Equal(expected: originalSource, actual: await File.ReadAllBytesAsync(files.SourceFile));
        Assert.Equal(expected: settings, actual: await File.ReadAllTextAsync(settingsFile));
        if (existingOutput)
        {
            Assert.Equal(expected: "caller workbook", actual: await File.ReadAllTextAsync(existingWorkbook));
            Assert.Equal(expected: "caller manifest", actual: await File.ReadAllTextAsync(existingManifest));
            int expected2 = 2;
            string[] fileSystemEntries = Directory.GetFileSystemEntries(files.Output.Directory!);
            int actual2 = fileSystemEntries.Length;
            Assert.Equal(expected: expected2, actual: actual2);
        }
        else
        {
            bool CalculateCondition()
            {
                bool condition = Directory.Exists(files.Output.Directory);
                return condition;
            }

            Assert.False(CalculateCondition());
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

        string CalculateNewValue()
        {
            string newValue = rows.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return newValue;
        }

        var settings = """
                       {"schemaVersion":1,"defaults":{"DeadlinesExcelConfig":{"maxTaskRows":9}},
                        "profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":ROWS}}}}
                       """.Replace("ROWS", CalculateNewValue());
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
            new()
            {
                Json = true,
            },
            files.Settings));

        Assert.Equal(expected: 3, actual: result.Exit);
        Assert.Equal(expected: 3,
            actual: result.Json.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(expected: "failed",
            actual: result.Json.GetProperty("status")
                .GetString());
        Assert.Equal(expected: "export lab-deadlines",
            actual: result.Json.GetProperty("command")
                .GetString());
        var error = Assert.Single(result.Json.GetProperty("errors")
                .EnumerateArray())
            .GetString();
        Assert.Contains(expectedSubstring: "maxTaskRows", actualString: error);
        Assert.Contains(expectedSubstring: "Curmanschii Anton", actualString: error);
        Assert.Contains(expectedSubstring: "between 1 and 1048575", actualString: error);

        string CalculateExpectedSubstring()
        {
            string expectedSubstring2 = $"resolved value is {rows}";
            return expectedSubstring2;
        }

        Assert.Contains(expectedSubstring: CalculateExpectedSubstring(), actualString: error);
        Assert.Contains(error!, result.Stderr);
        Assert.Empty(result.Json.GetProperty("outputs")
            .EnumerateArray());
        Assert.Empty(result.Json.GetProperty("warnings")
            .EnumerateArray());
        Assert.Equal(expected: 0,
            actual: result.Json.GetProperty("data")
                .GetProperty("workbookCount")
                .GetInt32());
        Assert.Equal(expected: JsonValueKind.Null,
            actual: result.Json.GetProperty("data")
                .GetProperty("outputDirectory").ValueKind);
        Assert.Equal(expected: 0, actual: files.Export.SourceReads);
        Assert.Equal(expected: 0, actual: files.Theses.Reads);
        Assert.Equal(expected: originalSource, actual: await File.ReadAllBytesAsync(files.SourceFile));
        Assert.Equal(expected: settings, actual: await File.ReadAllTextAsync(settingsFile));
        if (existingOutput)
        {
            Assert.Equal(expected: "caller workbook", actual: await File.ReadAllTextAsync(existingWorkbook));
            Assert.Equal(expected: "caller manifest", actual: await File.ReadAllTextAsync(existingManifest));
            int expected2 = 2;
            string[] fileSystemEntries = Directory.GetFileSystemEntries(files.Output.Directory!);
            int actual2 = fileSystemEntries.Length;
            Assert.Equal(expected: expected2, actual: actual2);
        }
        else
        {
            bool CalculateCondition()
            {
                bool condition = Directory.Exists(files.Output.Directory);
                return condition;
            }

            Assert.False(CalculateCondition());
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
            string CalculatePath()
            {
                string path2 = Path.Combine(files.Path, "schedulelib.json");
                return path2;
            }

            await File.WriteAllTextAsync(
                path: CalculatePath(),
                contents: """
                          {"schemaVersion":1,"profiles":{"Curmanschii Anton":{"DeadlinesExcelConfig":{"maxTaskRows":1},
                           "LabTasksDatabaseConfig":{"sources":[]}}}}
                          """);
        }

        var result = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new()
            {
                Json = true,
            },
            files.Settings));

        Assert.True(result.Exit == 0, result.Stderr);
        using var book = new XLWorkbook(Path.Combine(files.Output.Directory!, "deadlines.xlsx"));
        var sheet = Assert.Single(book.Worksheets);
        string expected2 = "01.09";
        var cell = sheet.Cell(row: 1, column: 2);
        string actual2 = cell.GetString();
        Assert.Equal(expected: expected2, actual: actual2);
        var iXLWorksheet = sheet;
        int row2 = rows + 1;
        int column2 = 2;
        var cell2 = iXLWorksheet.Cell(row: row2, column: column2);
        bool condition = cell2.IsEmpty();
        Assert.False(condition);
        var iXLWorksheet2 = sheet;
        int row3 = rows + 2;
        int column3 = 2;
        var cell3 = iXLWorksheet2.Cell(row: row3, column: column3);
        bool condition2 = cell3.IsEmpty();
        Assert.True(condition2);
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
            new()
            {
                Json = true,
            },
            files.Settings));
        var second = await Capture(() => files.Export.PreDefense(
            new(),
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.Equal(expected: 0, actual: first.Exit);
        Assert.Equal(expected: 0, actual: second.Exit);
        var directory = first.Json.GetProperty("data")
            .GetProperty("outputDirectory")
            .GetString()!;

        string CalculateExpected()
        {
            string expected2 = Path.Combine(files.Path, "output");
            return expected2;
        }

        string expected3 = CalculateExpected();
        string? actual2 = Path.GetDirectoryName(directory);
        Assert.Equal(expected: expected3, actual: actual2);
        Assert.NotEqual(expected: directory,
            actual: second.Json.GetProperty("data")
                .GetProperty("outputDirectory")
                .GetString());
        var notes = Path.Combine(directory, "notes.txt");
        await File.WriteAllTextAsync(path: notes, contents: "keep me");
        var replaced = await Capture(() => files.Export.PreDefense(
            new()
            {
                Directory = directory,
            },
            new()
            {
                Json = true,
            },
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
            new()
            {
                Json = true,
            },
            new()));
        Assert.Equal(expected: 3, actual: missing.Exit);
        Assert.Contains(expectedSubstring: "--profile", actualString: missing.Stderr);
        var absent = await Capture(() => files.Export.LabDeadlines(
            files.Source,
            files.Output,
            new()
            {
                Json = true,
            },
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
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.Equal(expected: 8, actual: result.Exit);
        Assert.Contains(expectedSubstring: "does not support", actualString: result.Stderr);

        bool CalculateCondition()
        {
            bool condition = Directory.Exists(files.Output.Directory);
            return condition;
        }

        Assert.False(CalculateCondition());
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
                Members =
                [
                    new()
                    {
                        Name = "Ionescu Maria",
                    },
                ],
            },
        ];
        Directory.CreateDirectory(files.Output.Directory!);
        var collision = Path.Combine(files.Output.Directory!, "comisia_8.xlsx");
        await File.WriteAllTextAsync(path: collision, contents: "caller data");
        var result = await Capture(() => files.Export.PreDefense(
            files.Output,
            new()
            {
                Json = true,
            },
            files.Settings));
        Assert.Equal(expected: 6, actual: result.Exit);
        Assert.Equal(expected: "partial",
            actual: result.Json.GetProperty("status")
                .GetString());
        Assert.Equal(expected: "caller data", actual: await File.ReadAllTextAsync(collision));
        string path2 = Path.Combine(files.Output.Directory!, "comisia_7.xlsx");
        bool condition = File.Exists(path2);
        Assert.True(condition);
        using var manifest = JsonDocument.Parse(
            await File.ReadAllTextAsync(Path.Combine(files.Output.Directory!, "schedulelib-manifest.json")));
        Assert.Equal(expected: "partial",
            actual: manifest.RootElement.GetProperty("Status")
                .GetString());
        Assert.Single(manifest.RootElement.GetProperty("Artifacts")
            .EnumerateArray());
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
                new()
                {
                    Json = true,
                },
                files.Settings));
            Assert.Equal(expected: 7, actual: busy.Exit);
        }
        var cancelled = await Capture(() =>
        {
            CancellationToken CalculateCancellationToken()
            {
                CancellationToken cancellationToken = new(canceled: true);
                return cancellationToken;
            }

            return files.Export.LabDeadlines(
                files.Source,
                files.Output,
                new()
                {
                    Json = true,
                },
                files.Settings,
                CalculateCancellationToken());
        });
        Assert.Equal(expected: 130, actual: cancelled.Exit);
        Assert.Equal(expected: "cancelled",
            actual: cancelled.Json.GetProperty("status")
                .GetString());
        string path = Path.Combine(files.Output.Directory!, "deadlines.xlsx");
        bool condition = File.Exists(path);
        Assert.False(condition);
        using var cancellation = new CancellationTokenSource();
        files.Theses.OnRead = cancellation.Cancel;
        var interrupted = await Capture(() => files.Export.PreDefense(
            files.Output,
            new()
            {
                Json = true,
            },
            files.Settings,
            cancellation.Token));
        Assert.Equal(expected: 130, actual: interrupted.Exit);
        string path2 = Path.Combine(files.Output.Directory!, "comisia_7.xlsx");
        bool condition2 = File.Exists(path2);
        Assert.False(condition2);
    }

    private static void AssertManifest(
        JsonElement result,
        string directory,
        string command,
        string artifact)
    {
        Assert.Equal(expected: 1,
            actual: result.GetProperty("schemaVersion")
                .GetInt32());
        Assert.Equal(expected: command,
            actual: result.GetProperty("command")
                .GetString());
        Assert.Equal(expected: 1,
            actual: result.GetProperty("data")
                .GetProperty("workbookCount")
                .GetInt32());
        Assert.Equal(
            expected: new[]
            {
                Path.Combine(directory, artifact),
                Path.Combine(directory, "schedulelib-manifest.json"),
            },
            actual: result.GetProperty("outputs")
                .EnumerateArray()
                .Select(x => x.GetString()));

        string CalculateJson()
        {
            string path = Path.Combine(directory, "schedulelib-manifest.json");
            string json = File.ReadAllText(path);
            return json;
        }

        using var manifest = JsonDocument.Parse(CalculateJson());
        Assert.Equal(expected: command,
            actual: manifest.RootElement.GetProperty("Command")
                .GetString());
        Assert.Equal(expected: artifact,
            actual: manifest.RootElement.GetProperty("Artifacts")[0]
                .GetProperty("Name")
                .GetString());
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
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CalculatePath2());

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

        public OutputArguments Output
        {
            get
            {
                return new()
                {
                    Directory = System.IO.Path.Combine(Path, "results"),
                };
            }
        }

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
            string? sourceDirectory = System.IO.Path.GetDirectoryName(SourceFile);
            Directory.CreateDirectory(sourceDirectory!);

            string CalculateFile()
            {
                string file = System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "fixtures",
                    "2025_sem2",
                    "zi",
                    "master.xlsx");
                return file;
            }

            using var book = new XLWorkbook(
                CalculateFile());
            var sheet = book.Worksheet(1);
            sheet.Range("D4:E18")
                .Clear(XLClearOptions.Contents);
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

        private static string CalculatePath2()
        {
            string path2 = $"schedulelib-teacher-{Guid.NewGuid():N}";
            return path2;
        }
    }

    private sealed class FixtureExport(FakeTheses theses) : ExportCommands
    {
        public Commission<CommissionMember>[]? Commissions { get; set; }

        public bool RejectSourceReads { get; set; }

        public int SourceReads { get; private set; }

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            // The coded academic calendar intentionally has no master ranges. Supply a synthetic
            // master range for workbook evidence without changing production academic settings.
            var dates = new CurrentYearSemesterIntervalBuilder();
            dates.Scope(x =>
            {
                x.AttendanceMode(AttendanceMode.Zi);
                x.QualificationType(QualificationType.Master);
                x.Semester(Semester.Sem1);

                DateOnly CalculateStart()
                {
                    var start = new DateOnly(
                        year: 2026,
                        month: 9,
                        day: 1);
                    return start;
                }

                x.LessonsStart(CalculateStart());

                DateOnly CalculateEnd()
                {
                    var end = new DateOnly(
                        year: 2026,
                        month: 12,
                        day: 13);
                    return end;
                }

                x.LessonsEndInclusive(CalculateEnd());

                Grade CalculateGrade()
                {
                    Grade grade = new(1);
                    return grade;
                }

                x.Range()
                    .Grade(CalculateGrade());
            });
            services.AddSingleton(dates.Build());
            services.Configure<PreDefenseOptions>(x =>
            {
                x.Commissions = Commissions;
                x.AvrStudents = [new(Student: "Popescu Ion", Group: "I2301")];
            });
            services.AddSingleton<IThesesFileProvider>(theses);
            if (RejectSourceReads)
            {
                var iServiceCollection = services;
                var implementationInstance = new RejectingInitializer(this);
                IScheduleInitializer implementationInstance2 = implementationInstance;
                iServiceCollection.AddSingleton(implementationInstance2);
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
