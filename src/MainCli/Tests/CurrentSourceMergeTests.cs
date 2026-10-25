using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Dates;

public sealed class CurrentSourceMergeTests
{
    [Fact]
    public async Task WebsitePdfsAndOlderWordSourcesProduceTheSameMergeFromFreshAndCachedLoads()
    {
        string CalculatePath2()
        {
            string toString = Guid.NewGuid()
                .ToString("N");
            string text = $"schedulelib-period-cache-{toString}";
            string path2 = (string) text;
            return path2;
        }

        var cacheDirectory = Path.Combine(
                    Path.GetTempPath(),
                    CalculatePath2());
        Directory.CreateDirectory(cacheDirectory);
        try
        {
            var sources = Path.Combine(AppContext.BaseDirectory, "data");
            var raw = await Load(
                dataDirectory: sources,
                cacheDirectory: cacheDirectory,
                enabled: false);
            Assert.Equal(
                expected: new[]
                {
                    new DateOnly(
                        year: 2026,
                        month: 9,
                        day: 1),
                    new DateOnly(
                        year: 2026,
                        month: 9,
                        day: 21),
                    new DateOnly(
                        year: 2026,
                        month: 10,
                        day: 11),
                },
                actual: raw.Periods.Select(x => x.Start));
            var cloudLabs = raw.EnumerateWeeklyLessons()
                .Where(x =>
            {
                bool CheckCondition()
                {
                    if (!(x.Lesson.Type == LessonType.Lab))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Groups.IsSingleGroup))
                    {
                        return false;
                    }

                    if (!(raw.Get(x.Lesson.Group).Name == "IA2402"))
                    {
                        return false;
                    }

                    if (!(raw.Get(x.Lesson.Course).Names
                        .Any(name => name.Contains(
                                            "Cloud",
                                            StringComparison.OrdinalIgnoreCase))))
                    {
                        return false;
                    }

                    return true;
                }

                return CheckCondition();
            })
                .ToArray();
            Assert.Contains(
                cloudLabs,
                x => { if (!(x.Date.Period == new PeriodId(1))) { return false; } if (!(x.Date.DayOfWeek == DayOfWeek.Tuesday)) { return false; } if (!(x.Date.Parity == Parity.EveryWeek)) { return false; } return true; });
            Assert.DoesNotContain(
                cloudLabs,
                x => { if (!(x.Date.Period == new PeriodId(2))) { return false; } if (!(x.Date.DayOfWeek == DayOfWeek.Tuesday)) { return false; } if (!(x.Date.Parity == Parity.EveryWeek)) { return false; } return true; });
            global::System.Collections.Generic.IEnumerable<string> collection = Directory.EnumerateFiles(path: cacheDirectory, searchPattern: "*.json");
            var cache = Assert.Single(collection);
            var originalCache = await File.ReadAllBytesAsync(cache);
            var cacheWriteTime = File.GetLastWriteTimeUtc(cache);
            var merged = await Load(
                dataDirectory: sources,
                cacheDirectory: cacheDirectory,
                enabled: true);
            Assert.Equal(expected: originalCache, actual: await File.ReadAllBytesAsync(cache));
            global::System.DateTime expected2 = cacheWriteTime;
            global::System.DateTime actual2 = File.GetLastWriteTimeUtc(cache);
            Assert.Equal(expected: expected2, actual: actual2);
            var fresh = await Load(
                dataDirectory: sources,
                cacheDirectory: cacheDirectory,
                enabled: true,
                bypass: true);
            Assert.Equal(expected: merged.AutomaticLabSubGroupMerges.ToArray(), actual: fresh.AutomaticLabSubGroupMerges.ToArray());
            string[] expected3 = Describe(merged);
            string[] actual3 = Describe(fresh);
            Assert.Equal(expected: expected3, actual: actual3);
            Assert.Equal(
                expected: Describe(raw),
                actual: Describe(
                    await Load(
                        dataDirectory: sources,
                        cacheDirectory: cacheDirectory,
                        enabled: false)));

            var ai = Assert.Single(
                merged.AutomaticLabSubGroupMerges.Where(x =>
                    merged.Get(x.Group).Name == "IA2404"
                    && merged.Get(x.Course).Names
                        .Any(name => name.StartsWith(
                        "Intel",
                        StringComparison.OrdinalIgnoreCase))));
            var labs = merged.EnumerateWeeklyLessons()
                .Where(x =>
                {
                    if (!(x.Lesson.Course == ai.Course))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Groups.Contains(ai.Group)))
                    {
                        return false;
                    }

                    if (!(x.Lesson.Type == LessonType.Lab))
                    {
                        return false;
                    }

                    return true;
                })
                .ToArray();
            Assert.NotEmpty(labs);
            Assert.All(
                labs,
                lesson =>
                {
                    Assert.Equal(expected: SubGroup.All, actual: lesson.Lesson.SubGroup);
                    Assert.True(lesson.Date.Period.IsUnspecified);
                });
            Assert.Contains(
                raw.EnumerateWeeklyLessons(),
                x => { if (!(x.Lesson.Course == ai.Course)) { return false; } if (!(x.Lesson.Group == ai.Group)) { return false; } if (!(x.Date.Period == new PeriodId(0))) { return false; } if (!(x.Lesson.SubGroup == new SubGroup("I"))) { return false; } return true; });
            Assert.Contains(
                raw.EnumerateWeeklyLessons(),
                x => { if (!(x.Lesson.Course == ai.Course)) { return false; } if (!(x.Lesson.Group == ai.Group)) { return false; } if (!(x.Date.Period == new PeriodId(0))) { return false; } if (!(x.Lesson.SubGroup == new SubGroup("II"))) { return false; } return true; });
            var weekly = new ManualWeeklyScheduledDateProvider(
                ScheduleLib.ScheduleDefaults.Config.StudyWeeks,
                ScheduleLib.ScheduleDefaults.Config.HolidayPeriods);
            var helper = new ScheduleDateProviderHelper(
                merged,
                LessonTimeConfig.CreateDefault(),
                ScheduleLib.ScheduleDefaults.Config.SemesterIntervalProvider());
            var dates = new ScheduledDateTimeProvider(weekly, helper)
                .Get(new()
                {
                    Lessons = labs.Select(x => x.Id.AsAny()),
                    Semester = Semester.Sem1,
                })
                .ToArray();
            Assert.NotEmpty(dates);
            Assert.True(
                dates.Min(x => x.DateTime)
                < new DateTime(
                    year: 2026,
                    month: 10,
                    day: 11));
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WordPeriodAfterPdfRetainsOriginalElectivePolicyInFreshAndCachedLoads()
    {
        string CalculatePath2()
        {
            string toString = Guid.NewGuid()
                .ToString("N");
            string text = $"schedulelib-word-after-pdf-{toString}";
            string path2 = (string) text;
            return path2;
        }

        var root = Path.Combine(Path.GetTempPath(), CalculatePath2());
        var sources = Path.Combine(root, "sources");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(cache);
        try
        {
            var packaged = Path.Combine(
                AppContext.BaseDirectory,
                "data",
                "2026_sem1",
                "zi");
            foreach (var date in new[]
                     {
                         "01.09.26",
                         "11.10.26",
                         "01.11.26",
                     })
            {
                var destination = Path.Combine(
                    sources,
                    "2026_sem1",
                    "zi",
                    date);
                Directory.CreateDirectory(destination);
                var file = date == "11.10.26" ? "orar2.pdf" : "Orar_An_II Lic.docx";
                var sourceDate = date == "11.10.26" ? date : "01.09.26";
                string sourceFileName2 = Path.Combine(
                        packaged,
                        sourceDate,
                        file);
                string destFileName2 = Path.Combine(destination, file);
                File.Copy(
                    sourceFileName: sourceFileName2, destFileName: destFileName2);
            }
            var fresh = await Load(
                dataDirectory: sources,
                cacheDirectory: cache,
                enabled: false);
            Assert.Equal(expected: 3, actual: fresh.Periods.Length);
            var older = fresh.EnumerateWeeklyLessons()
                .Where(x =>
                    x.Date.Period == new PeriodId(0)
                    && x.Lesson.Alternative == new Alternative("Antreprenoriat inovativ"))
                .ToArray();
            Assert.NotEmpty(older);
            var courses = older.Select(x => x.Lesson.Course)
                .ToHashSet();
            var later = fresh.EnumerateWeeklyLessons()
                .Where(x => x.Date.Period == new PeriodId(2) && courses.Contains(x.Lesson.Course))
                .ToArray();
            Assert.NotEmpty(later);
            Assert.All(later, x =>
            {
                global::ScheduleLib.Alternative CalculateExpected()
                {
                    global::ScheduleLib.Alternative expected2 = new Alternative("Antreprenoriat inovativ");
                    return expected2;
                }

                Assert.Equal(expected: CalculateExpected(), actual: x.Lesson.Alternative);
            });
            Assert.Contains(
                fresh.EnumerateWeeklyLessons(),
                x =>
                {
                    if (!(x.Date.Period == new PeriodId(1)))
                    { return false; }
                    if (!(x.Lesson.Alternative.Value is { } alternative))
                    { return false; }
                    if (!(alternative.StartsWith("Antreprenoriat Gr.", StringComparison.Ordinal)))
                    { return false; }
                    return true;
                });
            Assert.Equal(
                expected: Describe(fresh),
                actual: Describe(
                    await Load(
                        dataDirectory: sources,
                        cacheDirectory: cache,
                        enabled: false)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<Schedule> Load(
        string dataDirectory,
        string cacheDirectory,
        bool enabled,
        bool bypass = false)
    {
        var services = new ServiceCollection();
        AppConfiguration.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(options =>
        {
            options.DataDirectory = dataDirectory;
            options.CacheDirectory = cacheDirectory;
            options.BypassCache = bypass;
            options.EnrichWithFullNames = false;
            options.LoadConsultations = false;
        });
        services.Configure<ScheduleDerivationOptions>(options =>
        {
            global::ScheduleLib.Application.Config.ScheduleDerivationOptions scheduleDerivationOptions = options;
            scheduleDerivationOptions.AutoMergeLabSubGroups = enabled;
        });
        using var provider = AppConfiguration.BuildServiceProvider(services);
        AppConfiguration.ConfigureLayeredConfig(provider);
        await provider.InitializeSchedule(CancellationToken.None);
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Schedule>();
    }

    private static string[] Describe(Schedule schedule)
    {
        return schedule.EnumerateWeeklyLessons()
            .Select(x =>
            $"{x.Id}|{x.Lesson.Course}|{x.Lesson.Groups.ToString(schedule)}|{x.Lesson.GroupPartitionKey}|{x.Lesson.Type}|{x.Date}")
            .ToArray();
    }
}
