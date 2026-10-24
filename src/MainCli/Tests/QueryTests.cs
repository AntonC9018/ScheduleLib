using System.Security.Cryptography;
using System.Text.Json;
using ConvertDocToDocx;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class QueryTests
{
    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(
        new[]
        {
            "query",
            "lessons",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "query",
            "lessons",
            "--day",
            "nonsense",
        },
        2)]
    [InlineData(
        new[]
        {
            "query",
            "lessons",
            "--unknown",
        },
        2)]
    public async Task HelpAndParseErrorsDoNotInitialize(string[] args, int expected)
    {
        // The invocation directory contains no schedule data; these must still succeed/fail as parsing requires.
        var exit = await CliHost.Run(args);
        Assert.Equal(expected: expected, actual: exit);
    }

    [Fact]
    public async Task PortableDocxAndXlsxFixtureUsesPredicatesAndCleanJson()
    {
        var all = await Execute(new());
        Assert.NotEmpty(all.GetProperty("data").EnumerateArray());
        var first = all.GetProperty("data")[0];
        var room = first.GetProperty("room").GetString()!;
        var day = Enum.Parse<DayOfWeek>(first.GetProperty("day").GetString()!);
        var parity = Enum.Parse<Parity>(first.GetProperty("parity").GetString()!);
        var filtered = await Execute(new()
        {
            Room = room,
            Day = day,
            Parity = parity,
            BeforeSlot = first.GetProperty("slot").GetInt32(),
        });
        Assert.NotEmpty(filtered.GetProperty("data").EnumerateArray());
        foreach (var lesson in filtered.GetProperty("data").EnumerateArray())
        {
            Assert.Equal(expected: room, actual: lesson.GetProperty("room").GetString());
            Assert.Equal(expected: day.ToString(), actual: lesson.GetProperty("day").GetString());
            Assert.True(Enum.Parse<Parity>(lesson.GetProperty("parity").GetString()!).IsMatch(parity));
            Assert.True(lesson.GetProperty("slot").GetInt32() <= first.GetProperty("slot").GetInt32());
        }
        Assert.Empty((await Execute(new() { Day = DayOfWeek.Sunday })).GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task MissingInputReturnsConfigurationError()
    {
        Assert.Equal(
            expected: 3,
            actual: await new FixtureQuery().Lessons(
                new(),
                new() { DataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) },
                new()));
    }

    [Fact]
    public async Task LegacyWordCapabilityPreservesOriginalOnLinux()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string CalculatePath2()
        {
            string path2 = $"schedulelib-test-{Guid.NewGuid():N}";
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.doc");
        var output = Path.Combine(directory, "source.docx");
        try
        {
            await File.WriteAllTextAsync(path: input, contents: "original document bytes");
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() =>
                DocToDocxConversionHelper.TryConvertFile(
                    inputPath: input,
                    outputPath: output,
                    CancellationToken.None));
            Assert.Equal(expected: hash, actual: SHA256.HashData(await File.ReadAllBytesAsync(input)));
            bool condition = File.Exists(output);
            Assert.False(condition);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task XlsxFixtureProducesLessonsIndependently()
    {
        string CalculatePath2()
        {
            string path2 = $"schedulelib-xlsx-{Guid.NewGuid():N}";
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        var zi = Path.Combine(
            directory,
            "2025_sem2",
            "zi");
        Directory.CreateDirectory(zi);
        try
        {
            string CalculateSourceFileName()
            {
                string sourceFileName2 = Path.Combine(
                                    AppContext.BaseDirectory,
                                    "fixtures",
                                    "2025_sem2",
                                    "zi",
                                    "master.xlsx");
                return sourceFileName2;
            }

            string sourceFileName3 = CalculateSourceFileName();
            string destFileName2 = Path.Combine(zi, "master.xlsx");
            File.Copy(
                            sourceFileName: sourceFileName3, destFileName: destFileName2);
            Assert.NotEmpty((await Execute(new(), directory)).GetProperty("data").EnumerateArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancelledQueryReturns130BeforeLoading()
    {
        global::System.Threading.CancellationToken CalculateCancellationToken()
        {
            global::System.Threading.CancellationToken cancellationToken = new CancellationToken(canceled: true);
            return cancellationToken;
        }

        Assert.Equal(
                    expected: 130,
                    actual: await new FixtureQuery().Lessons(
                        new(),
                        new(),
                        new(),
                        CalculateCancellationToken()));
    }

    [Fact]
    public async Task ParserFailureJsonIsOneResultObject()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(expected: 2, actual: await CliHost.Run(["query", "lessons", "--day", "invalid", "--json"]));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 2, actual: document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData("lessons")]
    [InlineData("free-hours")]
    [InlineData("free-rooms")]
    [InlineData("teachers-excel")]
    [InlineData("pdf")]
    [InlineData("ics")]
    [InlineData("website-schedules")]
    [InlineData("website-theses")]
    public async Task SourceRequestsDistinguishTimeoutConnectionFailureAndCallerCancellation(string command)
    {
        var original = Console.Out;
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var failure in new[]
                     {
                         "timeout",
                         "connection",
                         "cancel",
                     })
            {
                using var stdout = new StringWriter();
                using var caller = new CancellationTokenSource();
                using var deadline = new CancellationTokenSource();
                // A provider deadline cancels its own linked token, not the caller's token.
                deadline.Cancel();
                global::System.Exception exception;
                if (failure == "connection")
                {
                    global::System.Net.Http.HttpRequestException httpRequestException = new HttpRequestException("source unavailable");
                    exception = (Exception) httpRequestException;
                }
                else
                {
                    global::System.OperationCanceledException operationCanceledException = new OperationCanceledException(deadline.Token);
                    exception = operationCanceledException;
                }

                var error = exception;
                global::System.Exception failure2 = error;
                global::System.Action? beforeFailure;
                if (failure == "cancel")
                {
                    beforeFailure = caller.Cancel;
                }
                else
                {
                    beforeFailure = null;
                }

                var initializer = new FailingInitializer(failure2, beforeFailure);
                var query = new FailingQuery(initializer);
                var export = new FailingExport(initializer);
                var source = new SourceArguments
                {
                    NoCache = true,
                    DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                };
                var destination = new OutputArguments { Directory = Path.Combine(directory, (string) $"{command}{failure}") };
                var result = new ResultArguments { Json = true };
                Console.SetOut(stdout);
                var exit = command switch
                {
                    "lessons" => await query.Lessons(
                        new(),
                        source,
                        result,
                        caller.Token),
                    "free-hours" => await query.FreeHours(
                        new() { Groups = ["IA2301"] },
                        source,
                        result,
                        caller.Token),
                    "free-rooms" => await export.FreeRooms(
                        source,
                        destination,
                        result,
                        caller.Token),
                    "teachers-excel" => await export.TeachersExcel(
                        source,
                        destination,
                        result,
                        caller.Token),
                    "pdf" => await export.Pdf(
                        source,
                        destination,
                        result,
                        caller.Token),
                    "ics" => await export.Ics(
                        source,
                        destination,
                        result,
                        caller.Token),
                    "website-schedules" => await export.WebsiteSchedules(
                        source,
                        destination,
                        result,
                        caller.Token),
                    "website-theses" => await export.WebsiteTheses(
                        source,
                        destination,
                        result,
                        caller.Token),
                    _ => throw new InvalidOperationException(),
                };
                using var json = JsonDocument.Parse(stdout.ToString());
                int expected2;
                if (failure == "cancel")
                {
                    expected2 = 130;
                }
                else
                {
                    expected2 = 5;
                }

                int actual2 = exit;
                Assert.Equal(expected: expected2, actual: actual2);
                Assert.Equal(expected: exit, actual: json.RootElement.GetProperty("exitCode").GetInt32());
                Assert.Equal(expected: "failed", actual: json.RootElement.GetProperty("status").GetString());
                Assert.Empty(json.RootElement.GetProperty("outputs").EnumerateArray());
                Assert.True(initializer.Called);
                global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(
                        path: directory,
                        searchPattern: "*.json",
                        SearchOption.AllDirectories);
                global::System.Collections.IEnumerable collection2 = collection;
                Assert.Empty(
                    collection2);
            }
        }
        finally
        {
            Console.SetOut(original);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task NoCacheAssertionDetectsRootPrefixedCacheFiles()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        global::System.Text.Encoding encoding = System.Text.Encoding.UTF8;
        string s = Path.GetFullPath(fixtures);
        byte[] source = encoding.GetBytes(s);
        byte[] inArray = SHA256.HashData(source);
        var identity = Convert.ToHexStringLower(
            inArray);
        string path1 = fixtures;
        string path2 = $"{identity}_schedule_2025_2.json";
        var cache = Path.Combine(path1, path2);
        await File.WriteAllTextAsync(path: cache, contents: "{}");
        try
        {
            await Assert.ThrowsAsync<Xunit.Sdk.EmptyException>(() => Execute(new()));
        }
        finally
        {
            File.Delete(cache);
        }
        // Restoring the clean source must make the same query pass again.
        await Execute(new());
    }

    private sealed class FailingInitializer(Exception failure, Action? beforeFailure) : IScheduleInitializer
    {
        public bool Called { get; private set; }

        public Task Initialize(ScheduleBuilder builder, CancellationToken cancellationToken)
        {
            Called = true;
            beforeFailure?.Invoke();
            return Task.FromException(failure);
        }
    }

    private sealed class FailingQuery(IScheduleInitializer initializer) : QueryCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton(initializer);
        }
    }

    private sealed class FailingExport(IScheduleInitializer initializer) : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.AddSingleton(initializer);
        }
    }

    private static async Task<JsonElement> Execute(QueryArguments query, string? sourceDirectory = null)
    {
        var original = Console.Out;
        var cwd = Environment.CurrentDirectory;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureQuery().Lessons(
                query,
                new()
                {
                    NoCache = true,
                    DataDirectory = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, "fixtures"),
                },
                new() { Json = true });
            Assert.Equal(expected: 0, actual: exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1, actual: document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(expected: "query lessons", actual: document.RootElement.GetProperty("command").GetString());
            Assert.Equal(expected: cwd, actual: Environment.CurrentDirectory);
            global::System.Collections.Generic.IEnumerable<string> CalculateCollection()
            {
                string path2 = Path.Combine(AppContext.BaseDirectory, "fixtures");
                string searchPattern2 = "*schedule_*.json";
                global::System.IO.SearchOption searchOption = SearchOption.AllDirectories;
                global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(
                                    path: path2, searchPattern: searchPattern2, searchOption);
                return collection;
            }

            Assert.Empty(
                            CalculateCollection());
            return document.RootElement.Clone();
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class FixtureQuery : QueryCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                global::ScheduleLib.Application.Core.ScheduleBuilderInitializerOptions scheduleBuilderInitializerOptions = x;
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
