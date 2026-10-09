using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Xunit;

public sealed class FreeHoursTests
{
    [Theory]
    [InlineData(new[] { "query", "free-hours", "--help" }, 0)]
    [InlineData(new[] { "query", "free-hours" }, 2)]
    [InlineData(new[] { "query", "free-hours", "--group" }, 2)]
    [InlineData(new[] { "query", "free-hours", "--group", "IA2301", "--unknown" }, 2)]
    public async Task HelpAndParseErrorsDoNotInitialize(string[] args, int expected)
    {
        // The invocation directory contains no schedule data; these must still succeed/fail as parsing requires.
        Assert.Equal(expected, await CliHost.Run(args));
    }

    [Fact]
    public async Task SuppliedGroupsReportBothParitiesAndBothOccupancyModes()
    {
        var data = (await Execute(For("M2301", "IA2301"))).GetProperty("data");
        Assert.Equal(new[] { "M2301", "IA2301" }, data.GetProperty("groups").EnumerateArray().Select(x => x.GetString()).ToArray());
        var sections = data.GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(8, sections.Length);
        Assert.Equal(
            new[] { "M2301/EvenWeek/whole", "M2301/EvenWeek/every", "IA2301/EvenWeek/whole", "IA2301/EvenWeek/every",
                "M2301/OddWeek/whole", "M2301/OddWeek/every", "IA2301/OddWeek/whole", "IA2301/OddWeek/every" },
            sections.Select(Name).ToArray());
        foreach (var section in sections)
            Assert.Equal(new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
                section.GetProperty("days").EnumerateArray().Select(x => x.GetProperty("day").GetString()).ToArray());

        // The fixture's two parities differ for M2301 on Wednesday.
        Assert.Equal("Wednesday: 08:00-11:15,15:00-20:00 | ", Day(sections[0], "Wednesday"));
        Assert.Equal("Wednesday: 08:00-11:15,16:45-20:00 | ", Day(sections[4], "Wednesday"));
        // Every IA2301 lesson is held every week, so its parities are identical.
        Assert.Equal("Monday: 08:00-13:00 | Tuesday: 08:00-14:45,18:30-20:00 | Wednesday: 08:00-09:30,13:15-20:00 | Thursday: 08:00-20:00 | Friday: 08:00-11:15,15:00-20:00 | ", Days(sections[2]));
        Assert.Equal(Days(sections[2]), Days(sections[6]));
    }

    [Fact]
    public async Task OccupancyModesReportTheActualSubgroupDifference()
    {
        // MIASD2501 has three Tuesday lessons: a whole-group one at 16:45-18:15, one for
        // subgroup I at 15:00-16:30 and one for subgroup II at 18:30-20:00. Only the first
        // one occupies its slot when only whole-group lessons count, so the two modes must
        // report different free intervals for the very same lessons.
        var mia = (await Execute(For("MIASD2501"))).GetProperty("data").GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "MIASD2501/EvenWeek/whole", "MIASD2501/EvenWeek/every", "MIASD2501/OddWeek/whole", "MIASD2501/OddWeek/every" },
            mia.Select(Name).ToArray());
        Assert.Equal("Tuesday: 08:00-16:30,18:30-20:00 | ", Day(mia[0], "Tuesday"));
        Assert.Equal("Tuesday: 08:00-14:45 | ", Day(mia[1], "Tuesday"));
        Assert.Equal("Tuesday: 08:00-16:30,18:30-20:00 | ", Day(mia[2], "Tuesday"));
        Assert.Equal("Tuesday: 08:00-14:45 | ", Day(mia[3], "Tuesday"));

        // DJ2301 is the same story with subgroup I lessons at 16:45-18:15 and 18:30-20:00.
        var dj = (await Execute(For("DJ2301"))).GetProperty("data").GetProperty("sections").EnumerateArray().ToArray();
        Assert.Equal(
            new[] { "DJ2301/EvenWeek/whole", "DJ2301/EvenWeek/every", "DJ2301/OddWeek/whole", "DJ2301/OddWeek/every" },
            dj.Select(Name).ToArray());
        Assert.Equal("Tuesday: 08:00-11:15,13:15-20:00 | ", Day(dj[2], "Tuesday"));
        Assert.Equal("Tuesday: 08:00-11:15,13:15-16:30 | ", Day(dj[3], "Tuesday"));
    }

    [Fact]
    public async Task TextOutputIsStructured()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new FixtureQuery().FreeHours(new() { Groups = ["IA2301"] }, Source(), new()));
            Assert.Equal(
                new[]
                {
                    "IA2301, EvenWeek week, whole-group lessons and unspecialized optional lessons occupy their slot",
                    "  Monday: 08:00-13:00",
                    "  Tuesday: 08:00-14:45, 18:30-20:00",
                    "  Wednesday: 08:00-09:30, 13:15-20:00",
                    "  Thursday: 08:00-20:00",
                    "  Friday: 08:00-11:15, 15:00-20:00",
                    "IA2301, EvenWeek week, every lesson occupies its slot",
                    "  Monday: 08:00-13:00",
                    "  Tuesday: 08:00-14:45, 18:30-20:00",
                    "  Wednesday: 08:00-09:30, 13:15-20:00",
                    "  Thursday: 08:00-20:00",
                    "  Friday: 08:00-11:15, 15:00-20:00",
                    "IA2301, OddWeek week, whole-group lessons and unspecialized optional lessons occupy their slot",
                    "  Monday: 08:00-13:00",
                    "  Tuesday: 08:00-14:45, 18:30-20:00",
                    "  Wednesday: 08:00-09:30, 13:15-20:00",
                    "  Thursday: 08:00-20:00",
                    "  Friday: 08:00-11:15, 15:00-20:00",
                    "IA2301, OddWeek week, every lesson occupies its slot",
                    "  Monday: 08:00-13:00",
                    "  Tuesday: 08:00-14:45, 18:30-20:00",
                    "  Wednesday: 08:00-09:30, 13:15-20:00",
                    "  Thursday: 08:00-20:00",
                    "  Friday: 08:00-11:15, 15:00-20:00",
                },
                output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task ExistingTaskTextRenderingIsUnchanged()
    {
        var services = CliRuntime.CreateServices(Source(), new FixtureQuery().FixtureServices);
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        var builder = new StringBuilder();
        scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>().Run(new()
        {
            Groups = ["IA2301", "M2301"],
            StringBuilder = builder,
        });
        string[] everyWeek = ["Luni:8:00-13:00", "Marţi:8:00-14:45,18:30-20:00", "Miercuri:8:00-9:30,13:15-20:00", "Joi:8:00-20:00", "Vineri:8:00-11:15,15:00-20:00"];
        string[] m2301Par = ["Luni:8:00-13:00,16:45-20:00", "Marţi:8:00-11:15,16:45-20:00", "Miercuri:8:00-11:15,15:00-20:00", "Joi:8:00-9:30,13:15-20:00", "Vineri:8:00-9:30,16:45-20:00"];
        string[] m2301Impar = ["Luni:8:00-13:00,16:45-20:00", "Marţi:8:00-11:15,16:45-20:00", "Miercuri:8:00-11:15,16:45-20:00", "Joi:8:00-9:30,15:00-20:00", "Vineri:8:00-9:30,16:45-20:00"];
        var expected = string.Concat(
            Block("par", "IA2301", true, everyWeek), Block("par", "IA2301", false, everyWeek),
            Block("par", "M2301", true, m2301Par), Block("par", "M2301", false, m2301Par),
            Block("impar", "IA2301", true, everyWeek), Block("impar", "IA2301", false, everyWeek),
            Block("impar", "M2301", true, m2301Impar), Block("impar", "M2301", false, m2301Impar));
        Assert.Equal(expected, builder.ToString().Replace("\r\n", "\n"));

        static string Block(string parity, string group, bool isOptional, string[] days) =>
            $"paritatea: {parity}, grupa: {group}, optional?: {isOptional}\n{string.Join("\n", days)}\n\n";
    }

    [Fact]
    public async Task UnknownGroupFailsClearlyWithoutWarnings()
    {
        var result = await Execute(For("IA2301", "NOPE"), expected: 2);
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("data").ValueKind);
        Assert.Equal(2, result.GetProperty("exitCode").GetInt32());
        Assert.Equal(new[] { "Unknown group 'NOPE'." }, result.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).ToArray());
        Assert.Empty(result.GetProperty("warnings").EnumerateArray().Select(x => x.GetString()));
        var suggestion = await Execute(For("ia2301"), expected: 2);
        Assert.Equal("Unknown group 'ia2301'. Did you mean 'IA2301'?", suggestion.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task RepeatedGroupIsReportedOnceWithAWarning()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new FixtureQuery().FreeHours(new() { Groups = ["IA2301", "IA2301"] }, Source(), new() { Json = true }));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(new[] { "IA2301" }, result.RootElement.GetProperty("data").GetProperty("groups").EnumerateArray().Select(x => x.GetString()).ToArray());
            Assert.Equal(4, result.RootElement.GetProperty("data").GetProperty("sections").GetArrayLength());
            Assert.Contains("Repeated group names were reported once.",
                result.RootElement.GetProperty("warnings").EnumerateArray().Select(x => x.GetString() ?? ""));
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task ScheduleWithoutLessonsSucceedsWithAFullyFreeWeek()
    {
        using var temp = new TempDirectory();
        var zi = Path.Combine(temp.Path, "2025_sem2", "zi");
        Directory.CreateDirectory(zi);
        using (var workbook = new XLWorkbook(Path.Combine(FixtureDirectory(), "2025_sem2", "zi", "master.xlsx")))
        {
            workbook.Worksheet(1).Rows(4, 18).Delete();
            workbook.SaveAs(Path.Combine(zi, "master.xlsx"));
        }
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(0, await new FixtureQuery().FreeHours(new() { Groups = ["MIA2501"] },
                new() { NoCache = true, DataDirectory = temp.Path }, new() { Json = true }));
            using var result = JsonDocument.Parse(output.ToString());
            var sections = result.RootElement.GetProperty("data").GetProperty("sections").EnumerateArray().ToArray();
            Assert.Equal(4, sections.Length);
            foreach (var section in sections)
                Assert.Equal("Monday: 08:00-20:00 | Tuesday: 08:00-20:00 | Wednesday: 08:00-20:00 | Thursday: 08:00-20:00 | Friday: 08:00-20:00 | ", Days(section));
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task CancelledQueryReturns130BeforeLoading()
    {
        Assert.Equal(130, await new FixtureQuery().FreeHours(new() { Groups = ["IA2301"] }, new(), new(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task CancellationDuringTheCalculationStopsTheHandler()
    {
        var services = CliRuntime.CreateServices(Source(), new FixtureQuery().FixtureServices);
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>();

        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var groupId = schedule.EnumerateGroups().Single(x => x.Item.Name == "IA2301").Id;
        var lessons = schedule.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Groups.Contains(groupId) && x.Date.Parity.IsMatch(Parity.EvenWeek))
            .ToArray();
        Assert.True(lessons.Length > 1);
        using var cancellation = new CancellationTokenSource();
        var enumerated = 0;
        IEnumerable<WeeklyLessonAccessor> CancellingLessons()
        {
            foreach (var lesson in lessons)
            {
                enumerated++;
                cancellation.Cancel();
                yield return lesson;
            }
        }
        Assert.False(cancellation.IsCancellationRequested);
        Assert.Throws<OperationCanceledException>(() =>
            handler.Sections(["IA2301"], CancellingLessons(), cancellation.Token));
        // Entry checks saw an active token; enumeration triggered cancellation. A final
        // guard alone would consume every lesson instead of stopping at the first one.
        Assert.Equal(1, enumerated);
        Assert.Equal(4, handler.Sections(["IA2301"], CancellationToken.None).Length);
    }

    [Fact]
    public async Task CancellationAfterTheCalculationIsNotSuccess()
    {
        using var cancellation = new CancellationTokenSource();
        // Every section is computed by then, so only a check right before the result is
        // reported can still turn this into a cancellation instead of a success.
        var result = await Execute(For("IA2301"), new LateCancellingQuery(cancellation), cancellation.Token, expected: 130);
        Assert.Equal(130, result.GetProperty("exitCode").GetInt32());
        Assert.Equal("failed", result.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("data").ValueKind);
        Assert.Equal(new[] { "Cancelled." }, result.GetProperty("errors").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public async Task MissingSourceIsAConfigurationError()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureQuery().FreeHours(new() { Groups = ["IA2301"] },
                new() { NoCache = true, DataDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()) }, new() { Json = true });
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(3, exit);
            Assert.Equal(3, result.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    private static string Day(JsonElement day) => day.GetProperty("day").GetString() + ": " + string.Join(",", day.GetProperty("intervals").EnumerateArray()
        .Select(x => x.GetProperty("start").GetString() + "-" + x.GetProperty("end").GetString())) + " | ";

    private static string Day(JsonElement section, string day) => Days(section.GetProperty("days").EnumerateArray()
        .Where(x => x.GetProperty("day").GetString() == day));

    private static string Days(JsonElement section) => Days(section.GetProperty("days").EnumerateArray());

    private static string Days(IEnumerable<JsonElement> days) => string.Concat(days.Select(Day));

    private static SourceArguments Source() => new() { NoCache = true, DataDirectory = FixtureDirectory() };

    private static string FixtureDirectory() => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static FreeHoursArguments For(params string[] groups) => new() { Groups = groups };

    private static Task<JsonElement> Execute(FreeHoursArguments groups, int expected = 0) =>
        Execute(groups, new FixtureQuery(), CancellationToken.None, expected);

    private static async Task<JsonElement> Execute(FreeHoursArguments groups, QueryCommands query, CancellationToken cancellationToken, int expected = 0)
    {
        var original = Console.Out;
        var cwd = Environment.CurrentDirectory;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await query.FreeHours(groups, Source(), new() { Json = true }, cancellationToken);
            Assert.Equal(expected, exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("query free-hours", document.RootElement.GetProperty("command").GetString());
            Assert.Equal(cwd, Environment.CurrentDirectory);
            Assert.Empty(Directory.EnumerateFiles(FixtureDirectory(), "schedule_*.json", SearchOption.AllDirectories));
            return document.RootElement.Clone();
        }
        finally { Console.SetOut(original); }
    }

    private static string Name(JsonElement section) => $"{section.GetProperty("group").GetString()}/{section.GetProperty("parity").GetString()}/" +
        (section.GetProperty("mode").GetString() == nameof(FreeHoursOccupancyMode.WholeGroupAndUnspecializedOptionalLessonsOccupy) ? "whole" : "every");

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"schedulelib-free-hours-{Guid.NewGuid():N}");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    private class FixtureQueryBase : QueryCommands
    {
        public void FixtureServices(IServiceCollection services) => ConfigureServices(services);

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
    }

    private sealed class FixtureQuery : FixtureQueryBase;

    /// <summary>Cancels the query after the free hours are computed, which is the last point
    /// at which the command could still report success. The handler is built before the
    /// calculation and takes the lesson times with it; the command asks for them once more
    /// when it formats the computed intervals, and only that resolution cancels.</summary>
    private sealed class LateCancellingQuery(CancellationTokenSource cancellation) : FixtureQueryBase
    {
        private bool _handlerBuilt;

        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            var declaredTime = services.Last(x => x.ServiceType == typeof(LessonTimeConfig));
            services.AddScoped<PrintFreeHoursOfGroupTaskHandler>(sp =>
            {
                var handler = ActivatorUtilities.CreateInstance<PrintFreeHoursOfGroupTaskHandler>(sp);
                _handlerBuilt = true;
                return handler;
            });
            // Registered as transient so that the resolution after the calculation asks the
            // factory again; as a singleton the times would be created only once.
            services.AddTransient<LessonTimeConfig>(sp =>
            {
                var config = (LessonTimeConfig?)declaredTime.ImplementationInstance
                    ?? throw new InvalidOperationException("The lesson times are no longer registered as an instance.");
                if (_handlerBuilt)
                {
                    cancellation.Cancel();
                }
                return config;
            });
        }
    }
}
