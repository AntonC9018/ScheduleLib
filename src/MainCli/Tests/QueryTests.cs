using System.Text.Json;
using System.Security.Cryptography;
using ConvertDocToDocx;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

public sealed class QueryTests
{
    [Theory]
    [InlineData(new string[0], 0)]
    [InlineData(new[] { "query", "lessons", "--help" }, 0)]
    [InlineData(new[] { "query", "lessons", "--day", "nonsense" }, 2)]
    [InlineData(new[] { "query", "lessons", "--unknown" }, 2)]
    public async Task HelpAndParseErrorsDoNotInitialize(string[] args, int expected)
    {
        // The invocation directory contains no schedule data; these must still succeed/fail as parsing requires.
        Assert.Equal(expected, await CliHost.Run(args));
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
        var filtered = await Execute(new() { Room = room, Day = day, Parity = parity, BeforeSlot = first.GetProperty("slot").GetInt32() });
        Assert.NotEmpty(filtered.GetProperty("data").EnumerateArray());
        foreach (var lesson in filtered.GetProperty("data").EnumerateArray())
        {
            Assert.Equal(room, lesson.GetProperty("room").GetString());
            Assert.Equal(day.ToString(), lesson.GetProperty("day").GetString());
            Assert.True(Enum.Parse<Parity>(lesson.GetProperty("parity").GetString()!).IsMatch(parity));
            Assert.True(lesson.GetProperty("slot").GetInt32() <= first.GetProperty("slot").GetInt32());
        }
        Assert.Empty((await Execute(new() { Day = DayOfWeek.Sunday })).GetProperty("data").EnumerateArray());
    }

    [Fact]
    public async Task MissingInputReturnsConfigurationError()
    {
        Assert.Equal(3, await new FixtureQuery().Lessons(new(), new() { DataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }, new()));
    }

    [Fact]
    public async Task LegacyWordCapabilityPreservesOriginalOnLinux()
    {
        if (OperatingSystem.IsWindows()) return;
        var directory = Path.Combine(Path.GetTempPath(), $"schedulelib-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.doc");
        var output = Path.Combine(directory, "source.docx");
        try
        {
            await File.WriteAllTextAsync(input, "original document bytes");
            var hash = SHA256.HashData(await File.ReadAllBytesAsync(input));
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => DocToDocxConversionHelper.TryConvertFile(input, output, CancellationToken.None));
            Assert.Equal(hash, SHA256.HashData(await File.ReadAllBytesAsync(input)));
            Assert.False(File.Exists(output));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task XlsxFixtureProducesLessonsIndependently()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"schedulelib-xlsx-{Guid.NewGuid():N}");
        var zi = Path.Combine(directory, "2025_sem2", "zi");
        Directory.CreateDirectory(zi);
        try
        {
            File.Copy(Path.Combine(AppContext.BaseDirectory, "fixtures", "2025_sem2", "zi", "master.xlsx"), Path.Combine(zi, "master.xlsx"));
            Assert.NotEmpty((await Execute(new(), directory)).GetProperty("data").EnumerateArray());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public async Task CancelledQueryReturns130BeforeLoading()
    {
        Assert.Equal(130, await new FixtureQuery().Lessons(new(), new(), new(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task ParserFailureJsonIsOneResultObject()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(2, await CliHost.Run(["query", "lessons", "--day", "invalid", "--json"]));
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(2, document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
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
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            foreach (var failure in new[] { "timeout", "connection", "cancel" })
            {
                using var stdout = new StringWriter();
                using var caller = new CancellationTokenSource();
                using var deadline = new CancellationTokenSource();
                // A provider deadline cancels its own linked token, not the caller's token.
                deadline.Cancel();
                var error = failure == "connection" ? (Exception)new HttpRequestException("source unavailable")
                    : new OperationCanceledException(deadline.Token);
                var initializer = new FailingInitializer(error, failure == "cancel" ? caller.Cancel : null);
                var query = new FailingQuery(initializer);
                var export = new FailingExport(initializer);
                var source = new SourceArguments { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") };
                var destination = new OutputArguments { Directory = Path.Combine(directory, command + failure) };
                var result = new ResultArguments { Json = true };
                Console.SetOut(stdout);
                var exit = command switch
                {
                    "lessons" => await query.Lessons(new(), source, result, caller.Token),
                    "free-hours" => await query.FreeHours(new() { Groups = ["IA2301"] }, source, result, caller.Token),
                    "free-rooms" => await export.FreeRooms(source, destination, result, caller.Token),
                    "teachers-excel" => await export.TeachersExcel(source, destination, result, caller.Token),
                    "pdf" => await export.Pdf(source, destination, result, caller.Token),
                    "ics" => await export.Ics(source, destination, result, caller.Token),
                    "website-schedules" => await export.WebsiteSchedules(source, destination, result, caller.Token),
                    "website-theses" => await export.WebsiteTheses(source, destination, result, caller.Token),
                    _ => throw new InvalidOperationException(),
                };
                using var json = JsonDocument.Parse(stdout.ToString());
                Assert.Equal(failure == "cancel" ? 130 : 5, exit);
                Assert.Equal(exit, json.RootElement.GetProperty("exitCode").GetInt32());
                Assert.Equal("failed", json.RootElement.GetProperty("status").GetString());
                Assert.Empty(json.RootElement.GetProperty("outputs").EnumerateArray());
                Assert.True(initializer.Called);
                Assert.Empty(Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories));
            }
        }
        finally { Console.SetOut(original); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task NoCacheAssertionDetectsRootPrefixedCacheFiles()
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures");
        var identity = Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(fixtures))));
        var cache = Path.Combine(fixtures, $"{identity}_schedule_2025_2.json");
        await File.WriteAllTextAsync(cache, "{}");
        try
        {
            await Assert.ThrowsAsync<Xunit.Sdk.EmptyException>(() => Execute(new()));
        }
        finally { File.Delete(cache); }
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
            var exit = await new FixtureQuery().Lessons(query,
                new() { NoCache = true, DataDirectory = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, "fixtures") }, new() { Json = true });
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("query lessons", document.RootElement.GetProperty("command").GetString());
            Assert.Equal(cwd, Environment.CurrentDirectory);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*schedule_*.json", SearchOption.AllDirectories));
            return document.RootElement.Clone();
        }
        finally { Console.SetOut(original); }
    }

    private sealed class FixtureQuery : QueryCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
    }
}
