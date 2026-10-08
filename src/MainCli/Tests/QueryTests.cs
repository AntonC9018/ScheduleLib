using System.Text.Json;
using System.Security.Cryptography;
using ConvertDocToDocx;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
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

    private static async Task<JsonElement> Execute(QueryArguments query, string? sourceDirectory = null)
    {
        var original = Console.Out;
        var cwd = Environment.CurrentDirectory;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureQuery().Lessons(query,
                new() { DataDirectory = sourceDirectory ?? Path.Combine(AppContext.BaseDirectory, "fixtures") }, new() { Json = true });
            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("query lessons", document.RootElement.GetProperty("command").GetString());
            Assert.Equal(cwd, Environment.CurrentDirectory);
            Assert.Empty(Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "schedule_*.json", SearchOption.AllDirectories));
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
