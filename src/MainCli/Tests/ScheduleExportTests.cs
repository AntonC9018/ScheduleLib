using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public sealed class ScheduleExportTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("ics")]
    public async Task HelpAndParseFailureNeedNoSources(string format)
    {
        Assert.Equal(expected: 0, actual: await CliHost.Run(["export", format, "--help"]));
        using var capture = new Capture();
        Assert.Equal(expected: 2, actual: await CliHost.Run(["export", format, "--json", "--unknown"]));
        Assert.Equal(
            expected: 2,
            actual: capture.Result()
                .GetProperty("exitCode")
                .GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationProducesOneJsonResultWithoutOutputs(bool calendar)
    {
        using var capture = new Capture();

        CancellationToken CalculateToken()
        {
            var token = new CancellationToken(true);
            return token;
        }

        Assert.Equal(
            expected: 130,
            actual: await Execute(
                new FixtureExport(),
                calendar,
                new(),
                new(),
                CalculateToken()));
        var result = capture.Result();
        Assert.Equal(
            expected: 130,
            actual: result.GetProperty("exitCode")
                .GetInt32());
        Assert.Empty(result.GetProperty("outputs")
            .EnumerateArray());
    }

    [Fact]
    public async Task PortableFixturesProduceMatchingGroupPartitionTeacherArtifacts()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var pdfs = Path.Combine(temp.Path, "pdf");
        var calendars = Path.Combine(temp.Path, "ics");
        await Export(false, pdfs);
        var calendarResult = await Export(true, calendars);
        var names = Directory.GetFiles(path: pdfs, searchPattern: "*.pdf")
            .Select(Path.GetFileNameWithoutExtension)
            .Order()
            .ToArray();
        Assert.True(names.Length > 5);
        Assert.Contains(names, x => x!.Contains('_')); // Existing partition artifacts.
        Assert.Contains(names, x => x!.StartsWith("IA"));
        var calendarNames = Directory.GetFiles(path: calendars, searchPattern: "*.ics")
            .Select(Path.GetFileNameWithoutExtension)
            .Order()
            .ToArray();
        Assert.NotEmpty(calendarNames);
        var skipped = calendarResult.GetProperty("warnings")
            .EnumerateArray()
            .Select(x => x.GetString()!)
            .Where(x => x.StartsWith("Skipping calendar "))
            .Select(x => x["Skipping calendar ".Length..x.IndexOf(".ics:")])
            .ToArray();
        Assert.Equal(
            expected: names.Except(skipped)
                .Order(),
            actual: calendarNames);
        Assert.Contains(calendarNames, x => x!.Contains('_'));
        Assert.Contains(calendarNames, x => x!.StartsWith("IA"));
        var pdf = Directory.GetFiles(path: pdfs, searchPattern: "*.pdf")
            .First(x => Path.GetFileName(x)
                .StartsWith("IA"));
        Assert.StartsWith(
            expectedStartString: "%PDF-",
            actualString: System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(pdf)));
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/pdftotext"))
        {
            var info = new ProcessStartInfo("/usr/bin/pdftotext")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add(pdf);
            info.ArgumentList.Add("-");
            using var process = Process.Start(info)!;
            var text = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(expected: 0, actual: process.ExitCode);
            Assert.Contains(expectedSubstring: "Luni", actualString: text);
            Assert.Contains(expectedSubstring: "Sisteme", actualString: text);
            Assert.Contains(
                expectedSubstring: "423/4",
                actualString: string.Join(
                    " ",
                    await Task.WhenAll(Directory.GetFiles(path: calendars, searchPattern: "*.ics")
                        .Select(x => File.ReadAllTextAsync(x)))));
        }
        foreach (var path in Directory.GetFiles(path: calendars, searchPattern: "*.ics"))
        {
            var ics = await File.ReadAllTextAsync(path);
            Assert.StartsWith(expectedStartString: "BEGIN:VCALENDAR\r\n", actualString: ics);
            Assert.Contains(expectedSubstring: "BEGIN:VEVENT\r\n", actualString: ics);
            Assert.Contains(expectedSubstring: "DTSTART", actualString: ics);
            Assert.Contains(expectedSubstring: "DTEND", actualString: ics);
            Assert.EndsWith(expectedEndString: "END:VCALENDAR\r\n", actualString: ics);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterFirstArtifactStopsFurtherGeneration(bool calendar)
    {
        var services = CliRuntime.CreateServices(
            Source(),
            services =>
            {
                AppConfiguration.ConfigureServices(services);
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
            });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var count = 0;

        async Task Publish(
            string name,
            Func<Stream, CancellationToken, Task> generate,
            CancellationToken token)
        {
            using var stream = new MemoryStream();
            await generate(stream, token);
            Assert.True(stream.Length > 0);
            count++;
            cancellation.Cancel();
        }

        if (calendar)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                scope.ServiceProvider.GetRequiredService<GenerateIcsCalendarsTaskHandler>()
                    .RunAsync(new()
                    {
                        CancellationToken = cancellation.Token,
                        PublishArtifact = Publish,
                    }));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await scope.ServiceProvider.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>()
                    .Run(new()
                    {
                        CancellationToken = cancellation.Token,
                        PublishArtifact = Publish,
                    }));
        }

        Assert.Equal(expected: 1, actual: count);
    }

    [Fact]
    public async Task MissingDatesReportSkippedCalendarsAndKeepUnrelatedFiles()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var notes = Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(path: notes, contents: "caller data");
        using var capture = new Capture();

        FixtureExport CalculateExport()
        {
            var export = new FixtureExport(missingDates: true);
            return export;
        }

        Assert.Equal(
            expected: 0,
            actual: await Execute(
                CalculateExport(),
                true,
                Source(),
                new()
                {
                    Directory = temp.Path,
                }));
        var result = capture.Result();
        Assert.Contains(
            result.GetProperty("warnings")
                .EnumerateArray(),
            x => x.GetString()!
                .StartsWith("Skipping calendar"));

        string[] CalculateCollection()
        {
            string[] collection = Directory.GetFiles(path: temp.Path, searchPattern: "*.ics");
            return collection;
        }

        Assert.Empty(CalculateCollection());
        Assert.Equal(expected: "caller data", actual: await File.ReadAllTextAsync(notes));

        bool CalculateCondition()
        {
            string path2 = Path.Combine(temp.Path, "schedulelib-manifest.json");
            bool condition = File.Exists(path2);
            return condition;
        }

        Assert.True(CalculateCondition());
    }

    private static async Task<JsonElement> Export(bool calendar, string destination)
    {
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await Execute(
                new FixtureExport(),
                calendar,
                Source(),
                new()
                {
                    Directory = destination,
                }));
        var result = capture.Result();
        Assert.Equal(
            expected: "succeeded",
            actual: result.GetProperty("status")
                .GetString());
        Assert.True(result.GetProperty("data")
                .GetProperty("lessonCount")
                .GetInt32()
            > 0);

        bool CalculateCondition()
        {
            string path = Path.Combine(destination, "schedulelib-manifest.json");
            bool condition = File.Exists(path);
            return condition;
        }

        Assert.True(CalculateCondition());
        return result;
    }

    private static SourceArguments Source()
    {
        return new()
        {
            NoCache = true,
            DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
        };
    }

    private static Task<int> Execute(
        FixtureExport export,
        bool calendar,
        SourceArguments source,
        OutputArguments output,
        CancellationToken token = default)
    {
        if (calendar)
        {
            return export.Ics(
                source,
                output,
                new()
                {
                    Json = true,
                },
                token);
        }

        return export.Pdf(
            source,
            output,
            new()
            {
                Json = true,
            },
            token);
    }

    private sealed class FixtureExport(bool missingDates = false) : ExportCommands
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
            if (missingDates)
            {
                services.AddSingleton(new CurrentYearSemesterIntervalBuilder().Build());
            }
        }
    }

    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _text = new();
        public Capture() => Console.SetOut(_text);

        public JsonElement Result()
        {
            using var document = JsonDocument.Parse(_text.ToString());
            return document.RootElement.Clone();
        }

        public void Dispose()
        {
            Console.SetOut(_original);
            _text.Dispose();
        }
    }


}
