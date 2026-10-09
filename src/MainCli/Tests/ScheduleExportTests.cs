using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Xunit;

public sealed class ScheduleExportTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("ics")]
    public async Task HelpAndParseFailureNeedNoSources(string format)
    {
        Assert.Equal(0, await CliHost.Run(["export", format, "--help"]));
        using var capture = new Capture();
        Assert.Equal(2, await CliHost.Run(["export", format, "--unknown", "--json"]));
        Assert.Equal(2, capture.Result().GetProperty("exitCode").GetInt32());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationProducesOneJsonResultWithoutOutputs(bool calendar)
    {
        using var capture = new Capture();
        Assert.Equal(130, await Execute(new FixtureExport(), calendar, new(), new(), new CancellationToken(true)));
        var result = capture.Result();
        Assert.Equal(130, result.GetProperty("exitCode").GetInt32());
        Assert.Empty(result.GetProperty("outputs").EnumerateArray());
    }

    [Fact]
    public async Task PortableFixturesProduceMatchingGroupPartitionTeacherArtifacts()
    {
        using var temp = new TempDirectory();
        var pdfs = Path.Combine(temp.Path, "pdf");
        var calendars = Path.Combine(temp.Path, "ics");
        await Export(false, pdfs);
        var calendarResult = await Export(true, calendars);
        var names = Directory.GetFiles(pdfs, "*.pdf").Select(Path.GetFileNameWithoutExtension).Order().ToArray();
        Assert.True(names.Length > 5);
        Assert.Contains(names, x => x!.Contains('_')); // Existing partition artifacts.
        Assert.Contains(names, x => x!.StartsWith("IA"));
        var calendarNames = Directory.GetFiles(calendars, "*.ics").Select(Path.GetFileNameWithoutExtension).Order().ToArray();
        Assert.NotEmpty(calendarNames);
        var skipped = calendarResult.GetProperty("warnings").EnumerateArray()
            .Select(x => x.GetString()!).Where(x => x.StartsWith("Skipping calendar "))
            .Select(x => x["Skipping calendar ".Length..x.IndexOf(".ics:")]).ToArray();
        Assert.Equal(names.Except(skipped).Order(), calendarNames);
        Assert.Contains(calendarNames, x => x!.Contains('_'));
        Assert.Contains(calendarNames, x => x!.StartsWith("IA"));
        var pdf = Directory.GetFiles(pdfs, "*.pdf").First(x => Path.GetFileName(x).StartsWith("IA"));
        Assert.StartsWith("%PDF-", System.Text.Encoding.ASCII.GetString(await File.ReadAllBytesAsync(pdf)));
        if (OperatingSystem.IsLinux() && File.Exists("/usr/bin/pdftotext"))
        {
            var info = new ProcessStartInfo("/usr/bin/pdftotext") { RedirectStandardOutput = true, RedirectStandardError = true };
            info.ArgumentList.Add(pdf);
            info.ArgumentList.Add("-");
            using var process = Process.Start(info)!;
            var text = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Luni", text);
            Assert.Contains("Sisteme", text);
            Assert.Contains("423/4", string.Join(" ", await Task.WhenAll(Directory.GetFiles(calendars, "*.ics").Select(x => File.ReadAllTextAsync(x)))));
        }
        foreach (var path in Directory.GetFiles(calendars, "*.ics"))
        {
            var ics = await File.ReadAllTextAsync(path);
            Assert.StartsWith("BEGIN:VCALENDAR\r\n", ics);
            Assert.Contains("BEGIN:VEVENT\r\n", ics);
            Assert.Contains("DTSTART", ics);
            Assert.Contains("DTEND", ics);
            Assert.EndsWith("END:VCALENDAR\r\n", ics);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterFirstArtifactStopsFurtherGeneration(bool calendar)
    {
        var services = CliRuntime.CreateServices(Source(), services =>
        {
            AppConfiguration.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var count = 0;
        async Task Publish(string name, Func<Stream, CancellationToken, Task> generate, CancellationToken token)
        {
            using var stream = new MemoryStream();
            await generate(stream, token);
            Assert.True(stream.Length > 0);
            count++;
            cancellation.Cancel();
        }
        if (calendar)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider.GetRequiredService<GenerateIcsCalendarsTaskHandler>().RunAsync(new()
            {
                CancellationToken = cancellation.Token, PublishArtifact = Publish,
            }));
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scope.ServiceProvider.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>().Run(new()
            {
                CancellationToken = cancellation.Token, PublishArtifact = Publish,
            }));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task MissingDatesReportSkippedCalendarsAndKeepUnrelatedFiles()
    {
        using var temp = new TempDirectory();
        var notes = Path.Combine(temp.Path, "notes.txt");
        await File.WriteAllTextAsync(notes, "caller data");
        using var capture = new Capture();
        Assert.Equal(0, await Execute(new FixtureExport(missingDates: true), true, Source(), new() { Directory = temp.Path }));
        var result = capture.Result();
        Assert.Contains(result.GetProperty("warnings").EnumerateArray(), x => x.GetString()!.StartsWith("Skipping calendar"));
        Assert.Empty(Directory.GetFiles(temp.Path, "*.ics"));
        Assert.Equal("caller data", await File.ReadAllTextAsync(notes));
        Assert.True(File.Exists(Path.Combine(temp.Path, "schedulelib-manifest.json")));
    }

    private static async Task<JsonElement> Export(bool calendar, string destination)
    {
        using var capture = new Capture();
        Assert.Equal(0, await Execute(new FixtureExport(), calendar, Source(), new() { Directory = destination }));
        var result = capture.Result();
        Assert.Equal("succeeded", result.GetProperty("status").GetString());
        Assert.True(result.GetProperty("data").GetProperty("lessonCount").GetInt32() > 0);
        Assert.True(File.Exists(Path.Combine(destination, "schedulelib-manifest.json")));
        return result;
    }

    private static SourceArguments Source() => new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") };
    private static Task<int> Execute(FixtureExport export, bool calendar, SourceArguments source, OutputArguments output, CancellationToken token = default)
        => calendar ? export.Ics(source, output, new() { Json = true }, token) : export.Pdf(source, output, new() { Json = true }, token);

    private sealed class FixtureExport(bool missingDates = false) : ExportCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
            if (missingDates) services.AddSingleton(new CurrentYearSemesterIntervalBuilder().Build());
        }
    }

    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _text = new();
        public Capture() => Console.SetOut(_text);
        public JsonElement Result() { using var document = JsonDocument.Parse(_text.ToString()); return document.RootElement.Clone(); }
        public void Dispose() { Console.SetOut(_original); _text.Dispose(); }
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-formats-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
