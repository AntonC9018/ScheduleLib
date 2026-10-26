using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public sealed class FreeHoursTests
{
    [Theory]
    [InlineData(
        new[]
        {
            "query",
            "free-hours",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "query",
            "free-hours",
        },
        2)]
    [InlineData(
        new[]
        {
            "query",
            "free-hours",
            "--group",
        },
        2)]
    [InlineData(
        new[]
        {
            "query",
            "free-hours",
            "--group",
            "IA2301",
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
    public async Task SuppliedGroupsReportBothParitiesAndBothOccupancyModes()
    {
        FreeHoursArguments CalculateGroups()
        {
            var groups = For("M2301", "IA2301");
            return groups;
        }

        var data = (await Execute(CalculateGroups()))
            .GetProperty("data");
        Assert.Equal(
            expected: new[]
            {
                "M2301",
                "IA2301",
            },
            actual: data.GetProperty("groups")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
        var sections = data.GetProperty("sections")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(expected: 8, actual: sections.Length);
        Assert.Equal(
            expected: new[]
            {
                "M2301/EvenWeek/whole",
                "M2301/EvenWeek/every",
                "IA2301/EvenWeek/whole",
                "IA2301/EvenWeek/every",
                "M2301/OddWeek/whole",
                "M2301/OddWeek/every",
                "IA2301/OddWeek/whole",
                "IA2301/OddWeek/every",
            },
            actual: sections.Select(Name)
                .ToArray());
        foreach (var section in sections)
        {
            Assert.Equal(
                expected: new[]
                {
                    "Monday",
                    "Tuesday",
                    "Wednesday",
                    "Thursday",
                    "Friday",
                },
                actual: section.GetProperty("days")
                    .EnumerateArray()
                    .Select(x => x.GetProperty("day")
                        .GetString())
                    .ToArray());
        }

        // The fixture's two parities differ for M2301 on Wednesday.
        Assert.Equal(expected: "Wednesday: 08:00-11:15,15:00-20:00 | ", actual: Day(sections[0], "Wednesday"));
        string expected2 = "Wednesday: 08:00-11:15,16:45-20:00 | ";
        string actual2 = Day(sections[4], "Wednesday");
        Assert.Equal(expected: expected2, actual: actual2);
        // Every IA2301 lesson is held every week, so its parities are identical.
        Assert.Equal(
            expected:
            "Monday: 08:00-13:00 | Tuesday: 08:00-14:45,18:30-20:00 | Wednesday: 08:00-09:30,13:15-20:00 | Thursday: 08:00-20:00 | Friday: 08:00-11:15,15:00-20:00 | ",
            actual: Days(sections[2]));
        string expected3 = Days(sections[2]);
        string actual3 = Days(sections[6]);
        Assert.Equal(expected: expected3, actual: actual3);
    }

    [Fact]
    public async Task OccupancyModesReportTheActualSubgroupDifference()
    {
        // MIASD2501 has three Tuesday lessons: a whole-group one at 16:45-18:15, one for
        // subgroup I at 15:00-16:30 and one for subgroup II at 18:30-20:00. Only the first
        // one occupies its slot when only whole-group lessons count, so the two modes must
        // report different free intervals for the very same lessons.
        FreeHoursArguments CalculateGroups()
        {
            var groups = For("MIASD2501");
            return groups;
        }

        var mia = (await Execute(CalculateGroups()))
            .GetProperty("data")
            .GetProperty("sections")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            expected: new[]
            {
                "MIASD2501/EvenWeek/whole",
                "MIASD2501/EvenWeek/every",
                "MIASD2501/OddWeek/whole",
                "MIASD2501/OddWeek/every",
            },
            actual: mia.Select(Name)
                .ToArray());
        string expected2 = "Tuesday: 08:00-16:30,18:30-20:00 | ";
        string actual2 = Day(mia[0], "Tuesday");
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "Tuesday: 08:00-14:45 | ";
        string actual3 = Day(mia[1], "Tuesday");
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "Tuesday: 08:00-16:30,18:30-20:00 | ";
        string actual4 = Day(mia[2], "Tuesday");
        Assert.Equal(expected: expected4, actual: actual4);
        string expected5 = "Tuesday: 08:00-14:45 | ";
        string actual5 = Day(mia[3], "Tuesday");
        Assert.Equal(expected: expected5, actual: actual5);

        // DJ2301 is the same story with subgroup I lessons at 16:45-18:15 and 18:30-20:00.
        FreeHoursArguments CalculateGroups2()
        {
            var groups2 = For("DJ2301");
            return groups2;
        }

        var dj = (await Execute(CalculateGroups2()))
            .GetProperty("data")
            .GetProperty("sections")
            .EnumerateArray()
            .ToArray();
        Assert.Equal(
            expected: new[]
            {
                "DJ2301/EvenWeek/whole",
                "DJ2301/EvenWeek/every",
                "DJ2301/OddWeek/whole",
                "DJ2301/OddWeek/every",
            },
            actual: dj.Select(Name)
                .ToArray());
        string expected6 = "Tuesday: 08:00-11:15,13:15-20:00 | ";
        string actual6 = Day(dj[2], "Tuesday");
        Assert.Equal(expected: expected6, actual: actual6);
        string expected7 = "Tuesday: 08:00-11:15,13:15-16:30 | ";
        string actual7 = Day(dj[3], "Tuesday");
        Assert.Equal(expected: expected7, actual: actual7);
    }

    [Fact]
    public async Task TextOutputIsStructured()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 0,
                actual: await new FixtureQuery().FreeHours(
                    new()
                    {
                        Groups = ["IA2301"],
                    },
                    Source(),
                    new()));

            string[] CalculateActual()
            {
                string[] actual2 = output.ToString()
                    .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
                return actual2;
            }

            Assert.Equal(
                expected: new[]
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
                actual: CalculateActual());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task ExistingTaskTextRenderingIsUnchanged()
    {
        var services = CliRuntime.CreateServices(Source(), new FixtureQuery().FixtureServices);
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(CancellationToken.None);
        await using var scope = provider.CreateAsyncScope();
        var builder = new StringBuilder();
        scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>()
            .Run(new()
            {
                Groups = ["IA2301", "M2301"],
                StringBuilder = builder,
            });
        string[] everyWeek =
        [
            "Luni:8:00-13:00", "Marţi:8:00-14:45,18:30-20:00", "Miercuri:8:00-9:30,13:15-20:00", "Joi:8:00-20:00",
            "Vineri:8:00-11:15,15:00-20:00",
        ];
        string[] m2301Par =
        [
            "Luni:8:00-13:00,16:45-20:00", "Marţi:8:00-11:15,16:45-20:00", "Miercuri:8:00-11:15,15:00-20:00",
            "Joi:8:00-9:30,13:15-20:00", "Vineri:8:00-9:30,16:45-20:00",
        ];
        string[] m2301Impar =
        [
            "Luni:8:00-13:00,16:45-20:00", "Marţi:8:00-11:15,16:45-20:00", "Miercuri:8:00-11:15,16:45-20:00",
            "Joi:8:00-9:30,15:00-20:00", "Vineri:8:00-9:30,16:45-20:00",
        ];

        string CalculateValues()
        {
            string values = Block(
                parity: "par",
                @group: "IA2301",
                true,
                everyWeek);
            return values;
        }

        string CalculateBlock()
        {
            string block = Block(
                parity: "par",
                @group: "IA2301",
                false,
                everyWeek);
            return block;
        }

        string CalculateBlock2()
        {
            string block2 = Block(
                parity: "par",
                @group: "M2301",
                true,
                m2301Par);
            return block2;
        }

        string CalculateBlock3()
        {
            string block3 = Block(
                parity: "par",
                @group: "M2301",
                false,
                m2301Par);
            return block3;
        }

        string CalculateBlock4()
        {
            string block4 = Block(
                parity: "impar",
                @group: "IA2301",
                true,
                everyWeek);
            return block4;
        }

        string CalculateBlock5()
        {
            string block5 = Block(
                parity: "impar",
                @group: "IA2301",
                false,
                everyWeek);
            return block5;
        }

        string CalculateBlock6()
        {
            string block6 = Block(
                parity: "impar",
                @group: "M2301",
                true,
                m2301Impar);
            return block6;
        }

        string CalculateBlock7()
        {
            string block7 = Block(
                parity: "impar",
                @group: "M2301",
                false,
                m2301Impar);
            return block7;
        }

        var expected = string.Concat(
            CalculateValues(),
            CalculateBlock(),
            CalculateBlock2(),
            CalculateBlock3(),
            CalculateBlock4(),
            CalculateBlock5(),
            CalculateBlock6(),
            CalculateBlock7());

        string CalculateActual()
        {
            string actual2 = builder.ToString()
                .Replace("\r\n", "\n");
            return actual2;
        }

        Assert.Equal(expected: expected, actual: CalculateActual());

        static string Block(
            string parity,
            string group,
            bool isOptional,
            string[] days) =>
            $"paritatea: {parity}, grupa: {group}, optional?: {isOptional}\n{string.Join("\n", days)}\n\n";
    }

    [Fact]
    public async Task UnknownGroupFailsClearlyWithoutWarnings()
    {
        FreeHoursArguments CalculateGroups()
        {
            var groups = For("IA2301", "NOPE");
            return groups;
        }

        var result = await Execute(CalculateGroups(), expected: 2);
        Assert.Equal(expected: "failed",
            actual: result.GetProperty("status")
                .GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: result.GetProperty("data").ValueKind);
        Assert.Equal(expected: 2,
            actual: result.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(
            expected: new[]
            {
                "Unknown group 'NOPE'.",
            },
            actual: result.GetProperty("errors")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
        Assert.Empty(result.GetProperty("warnings")
            .EnumerateArray()
            .Select(x => x.GetString()));

        FreeHoursArguments CalculateGroups2()
        {
            var groups2 = For("ia2301");
            return groups2;
        }

        var suggestion = await Execute(CalculateGroups2(), expected: 2);
        Assert.Equal(expected: "Unknown group 'ia2301'. Did you mean 'IA2301'?",
            actual: suggestion.GetProperty("errors")[0]
                .GetString());
    }

    [Fact]
    public async Task RepeatedGroupIsReportedOnceWithAWarning()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 0,
                actual: await new FixtureQuery().FreeHours(
                    new()
                    {
                        Groups = ["IA2301", "IA2301"],
                    },
                    Source(),
                    new()
                    {
                        Json = true,
                    }));
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: new[]
                {
                    "IA2301",
                },
                actual: result.RootElement.GetProperty("data")
                    .GetProperty("groups")
                    .EnumerateArray()
                    .Select(x => x.GetString())
                    .ToArray());
            Assert.Equal(expected: 4,
                actual: result.RootElement.GetProperty("data")
                    .GetProperty("sections")
                    .GetArrayLength());
            Assert.Contains(
                "Repeated group names were reported once.",
                result.RootElement.GetProperty("warnings")
                    .EnumerateArray()
                    .Select(x => x.GetString() ?? ""));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task ScheduleWithoutLessonsSucceedsWithAFullyFreeWeek()
    {
        using var temp = new TempDirectory();
        var zi = Path.Combine(
            temp.Path,
            "2025_sem2",
            "zi");
        Directory.CreateDirectory(zi);

        string CalculateFile()
        {
            string file = Path.Combine(
                FixtureDirectory(),
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
            string file2 = Path.Combine(zi, "master.xlsx");
            xLWorkbook.SaveAs(file2);
        }
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 0,
                actual: await new FixtureQuery().FreeHours(
                    new()
                    {
                        Groups = ["MIA2501"],
                    },
                    new()
                    {
                        NoCache = true,
                        DataDirectory = temp.Path,
                    },
                    new()
                    {
                        Json = true,
                    }));
            using var result = JsonDocument.Parse(output.ToString());
            var sections = result.RootElement.GetProperty("data")
                .GetProperty("sections")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(expected: 4, actual: sections.Length);
            foreach (var section in sections)
            {
                string CalculateActual()
                {
                    string actual2 = Days(section);
                    return actual2;
                }

                Assert.Equal(
                    expected:
                    "Monday: 08:00-20:00 | Tuesday: 08:00-20:00 | Wednesday: 08:00-20:00 | Thursday: 08:00-20:00 | Friday: 08:00-20:00 | ",
                    actual: CalculateActual());
            }
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task CancelledQueryReturns130BeforeLoading()
    {
        CancellationToken CalculateCancellationToken()
        {
            var cancellationToken = new CancellationToken(canceled: true);
            return cancellationToken;
        }

        Assert.Equal(
            expected: 130,
            actual: await new FixtureQuery().FreeHours(
                new()
                {
                    Groups = ["IA2301"],
                },
                new(),
                new(),
                CalculateCancellationToken()));
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
        var groupId = schedule.EnumerateGroups()
            .Single(x => x.Item.Name == "IA2301").Id;
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
            handler.Sections(
                ["IA2301"],
                CancellingLessons(),
                cancellation.Token));
        // Entry checks saw an active token; enumeration triggered cancellation. A final
        // guard alone would consume every lesson instead of stopping at the first one.
        Assert.Equal(expected: 1, actual: enumerated);
        int expected2 = 4;
        var sections = handler.Sections(["IA2301"], CancellationToken.None);
        int actual2 = sections.Length;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public async Task CancellationAfterTheCalculationIsNotSuccess()
    {
        using var cancellation = new CancellationTokenSource();

        // Every section is computed by then, so only a check right before the result is
        // reported can still turn this into a cancellation instead of a success.
        FreeHoursArguments CalculateGroups()
        {
            var groups = For("IA2301");
            return groups;
        }

        LateCancellingQuery CalculateQuery()
        {
            var query = new LateCancellingQuery(cancellation);
            return query;
        }

        var result = await Execute(
            CalculateGroups(),
            CalculateQuery(),
            cancellation.Token,
            expected: 130);
        Assert.Equal(expected: 130,
            actual: result.GetProperty("exitCode")
                .GetInt32());
        Assert.Equal(expected: "failed",
            actual: result.GetProperty("status")
                .GetString());
        Assert.Equal(expected: JsonValueKind.Null, actual: result.GetProperty("data").ValueKind);
        Assert.Equal(
            expected: new[]
            {
                "Cancelled.",
            },
            actual: result.GetProperty("errors")
                .EnumerateArray()
                .Select(x => x.GetString())
                .ToArray());
    }

    [Fact]
    public async Task MissingSourceIsAConfigurationError()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await new FixtureQuery().FreeHours(
                new()
                {
                    Groups = ["IA2301"],
                },
                new()
                {
                    NoCache = true,
                    DataDirectory = Path.Combine(Path.GetTempPath(),
                        Guid.NewGuid()
                            .ToString()),
                },
                new()
                {
                    Json = true,
                });
            using var result = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 3, actual: exit);
            Assert.Equal(expected: 3,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private static string Day(JsonElement day)
    {
        return $"{day.GetProperty("day").GetString()}: {string.Join(
            ",",
            day.GetProperty("intervals").EnumerateArray()
                .Select(x => $"{x.GetProperty("start").GetString()}-{x.GetProperty("end").GetString()}"))} | ";
    }

    private static string Day(JsonElement section, string day)
    {
        return Days(
            section.GetProperty("days")
                .EnumerateArray()
                .Where(x => x.GetProperty("day")
                        .GetString()
                    == day));
    }

    private static string Days(JsonElement section)
    {
        return Days(section.GetProperty("days")
            .EnumerateArray());
    }

    private static string Days(IEnumerable<JsonElement> days) => string.Concat(days.Select(Day));

    private static SourceArguments Source()
    {
        return new()
        {
            NoCache = true,
            DataDirectory = FixtureDirectory(),
        };
    }

    private static string FixtureDirectory() => Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static FreeHoursArguments For(params string[] groups)
    {
        return new()
        {
            Groups = groups,
        };
    }

    private static Task<JsonElement> Execute(FreeHoursArguments groups, int expected = 0)
    {
        return Execute(
            groups,
            new FixtureQuery(),
            CancellationToken.None,
            expected);
    }

    private static async Task<JsonElement> Execute(
        FreeHoursArguments groups,
        QueryCommands query,
        CancellationToken cancellationToken,
        int expected = 0)
    {
        var original = Console.Out;
        var cwd = Environment.CurrentDirectory;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            var exit = await query.FreeHours(
                groups,
                Source(),
                new()
                {
                    Json = true,
                },
                cancellationToken);
            Assert.Equal(expected: expected, actual: exit);
            using var document = JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: 1,
                actual: document.RootElement.GetProperty("schemaVersion")
                    .GetInt32());
            Assert.Equal(expected: "query free-hours",
                actual: document.RootElement.GetProperty("command")
                    .GetString());
            Assert.Equal(expected: cwd, actual: Environment.CurrentDirectory);

            IEnumerable<string> CalculateCollection()
            {
                var collection = Directory.EnumerateFiles(
                    path: FixtureDirectory(),
                    searchPattern: "*schedule_*.json",
                    SearchOption.AllDirectories);
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

    private static string Name(JsonElement section)
    {
        return
            $"{$"{section.GetProperty("group").GetString()}/{section.GetProperty("parity").GetString()}/"}{(section.GetProperty("mode").GetString()
                == nameof(FreeHoursOccupancyMode.WholeGroupAndUnspecializedOptionalLessonsOccupy)
                    ? "whole"
                    : "every")}";
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), CalculatePath2());

        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);

        private static string CalculatePath2()
        {
            string path2 = $"schedulelib-free-hours-{Guid.NewGuid():N}";
            return path2;
        }
    }

    private abstract class FixtureQueryBase : QueryCommands
    {
        public void FixtureServices(IServiceCollection services) => ConfigureServices(services);

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
            services.AddScoped(sp =>
            {
                var handler = ActivatorUtilities.CreateInstance<PrintFreeHoursOfGroupTaskHandler>(sp);
                _handlerBuilt = true;
                return handler;
            });
            // Registered as transient so that the resolution after the calculation asks the
            // factory again; as a singleton the times would be created only once.
            services.AddTransient(sp =>
            {
                var config = (LessonTimeConfig?) declaredTime.ImplementationInstance
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
