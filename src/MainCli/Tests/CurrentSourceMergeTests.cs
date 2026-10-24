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
        var cacheDirectory = Path.Combine(
            Path.GetTempPath(),
            (string) $"schedulelib-period-cache-{Guid.NewGuid().ToString("N")}");
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
            var cloudLabs = raw.EnumerateWeeklyLessons().Where(x =>
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

                    if (!(raw.Get(x.Lesson.Course).Names.Any(name => name.Contains(
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
                x => x.Date.Period == new PeriodId(1)
                    && x.Date.DayOfWeek == DayOfWeek.Tuesday
                    && x.Date.Parity == Parity.EveryWeek);
            Assert.DoesNotContain(
                cloudLabs,
                x => x.Date.Period == new PeriodId(2)
                    && x.Date.DayOfWeek == DayOfWeek.Tuesday
                    && x.Date.Parity == Parity.EveryWeek);
            var cache = Assert.Single(Directory.EnumerateFiles(path: cacheDirectory, searchPattern: "*.json"));
            var originalCache = await File.ReadAllBytesAsync(cache);
            var cacheWriteTime = File.GetLastWriteTimeUtc(cache);
            var merged = await Load(
                dataDirectory: sources,
                cacheDirectory: cacheDirectory,
                enabled: true);
            Assert.Equal(expected: originalCache, actual: await File.ReadAllBytesAsync(cache));
            Assert.Equal(expected: cacheWriteTime, actual: File.GetLastWriteTimeUtc(cache));
            var fresh = await Load(
                dataDirectory: sources,
                cacheDirectory: cacheDirectory,
                enabled: true,
                bypass: true);
            Assert.Equal(expected: merged.AutomaticLabSubGroupMerges.ToArray(), actual: fresh.AutomaticLabSubGroupMerges.ToArray());
            Assert.Equal(expected: Describe(merged), actual: Describe(fresh));
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
                    && merged.Get(x.Course).Names.Any(name => name.StartsWith(
                        "Intel",
                        StringComparison.OrdinalIgnoreCase))));
            var labs = merged.EnumerateWeeklyLessons().Where(x =>
                    x.Lesson.Course == ai.Course
                    && x.Lesson.Groups.Contains(ai.Group)
                    && x.Lesson.Type == LessonType.Lab)
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
                x => x.Lesson.Course == ai.Course
                    && x.Lesson.Group == ai.Group
                    && x.Date.Period == new PeriodId(0)
                    && x.Lesson.SubGroup == new SubGroup("I"));
            Assert.Contains(
                raw.EnumerateWeeklyLessons(),
                x => x.Lesson.Course == ai.Course
                    && x.Lesson.Group == ai.Group
                    && x.Date.Period == new PeriodId(0)
                    && x.Lesson.SubGroup == new SubGroup("II"));
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
                }).ToArray();
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
        var root = Path.Combine(Path.GetTempPath(), (string) $"schedulelib-word-after-pdf-{Guid.NewGuid().ToString("N")}");
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
                File.Copy(
                    sourceFileName: Path.Combine(
                        packaged,
                        sourceDate,
                        file),
                    destFileName: Path.Combine(destination, file));
            }
            var fresh = await Load(
                dataDirectory: sources,
                cacheDirectory: cache,
                enabled: false);
            Assert.Equal(expected: 3, actual: fresh.Periods.Length);
            var older = fresh.EnumerateWeeklyLessons().Where(x =>
                    x.Date.Period == new PeriodId(0)
                    && x.Lesson.Alternative == new Alternative("Antreprenoriat inovativ"))
                .ToArray();
            Assert.NotEmpty(older);
            var courses = older.Select(x => x.Lesson.Course).ToHashSet();
            var later = fresh.EnumerateWeeklyLessons()
                .Where(x => x.Date.Period == new PeriodId(2) && courses.Contains(x.Lesson.Course)).ToArray();
            Assert.NotEmpty(later);
            Assert.All(later, x => Assert.Equal(expected: new Alternative("Antreprenoriat inovativ"), actual: x.Lesson.Alternative));
            Assert.Contains(
                fresh.EnumerateWeeklyLessons(),
                x => x.Date.Period == new PeriodId(1)
                    && x.Lesson.Alternative.Value is { } alternative
                    && alternative.StartsWith("Antreprenoriat Gr.", StringComparison.Ordinal));
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
        services.Configure<ScheduleDerivationOptions>(options => options.AutoMergeLabSubGroups = enabled);
        using var provider = AppConfiguration.BuildServiceProvider(services);
        AppConfiguration.ConfigureLayeredConfig(provider);
        await provider.InitializeSchedule(CancellationToken.None);
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<Schedule>();
    }

    private static string[] Describe(Schedule schedule)
    {
        return schedule.EnumerateWeeklyLessons().Select(x =>
            $"{x.Id}|{x.Lesson.Course}|{x.Lesson.Groups.ToString(schedule)}|{x.Lesson.GroupPartitionKey}|{x.Lesson.Type}|{x.Date}")
            .ToArray();
    }
}
