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
            "schedulelib-period-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDirectory);
        try
        {
            var sources = Path.Combine(AppContext.BaseDirectory, "data");
            var raw = await Load(
                sources,
                cacheDirectory,
                enabled: false);
            Assert.Equal(
                new[]
                {
                    new DateOnly(
                        2026,
                        9,
                        1),
                    new DateOnly(
                        2026,
                        9,
                        21),
                    new DateOnly(
                        2026,
                        10,
                        11),
                },
                raw.Periods.Select(x => x.Start));
            var cloudLabs = raw.EnumerateWeeklyLessons().Where(x =>
                    x.Lesson.Type == LessonType.Lab &&
                    x.Lesson.Groups.IsSingleGroup &&
                    raw.Get(x.Lesson.Group).Name == "IA2402" &&
                    raw.Get(x.Lesson.Course).Names.Any(name => name.Contains(
                        "Cloud",
                        StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            Assert.Contains(
                cloudLabs,
                x => x.Date.Period == new PeriodId(1) &&
                    x.Date.DayOfWeek == DayOfWeek.Tuesday &&
                    x.Date.Parity == Parity.EveryWeek);
            Assert.DoesNotContain(
                cloudLabs,
                x => x.Date.Period == new PeriodId(2) &&
                    x.Date.DayOfWeek == DayOfWeek.Tuesday &&
                    x.Date.Parity == Parity.EveryWeek);
            var cache = Assert.Single(Directory.EnumerateFiles(cacheDirectory, "*.json"));
            var originalCache = await File.ReadAllBytesAsync(cache);
            var cacheWriteTime = File.GetLastWriteTimeUtc(cache);
            var merged = await Load(
                sources,
                cacheDirectory,
                enabled: true);
            Assert.Equal(originalCache, await File.ReadAllBytesAsync(cache));
            Assert.Equal(cacheWriteTime, File.GetLastWriteTimeUtc(cache));
            var fresh = await Load(
                sources,
                cacheDirectory,
                enabled: true,
                bypass: true);
            Assert.Equal(merged.AutomaticLabSubGroupMerges.ToArray(), fresh.AutomaticLabSubGroupMerges.ToArray());
            Assert.Equal(Describe(merged), Describe(fresh));
            Assert.Equal(
                Describe(raw),
                Describe(
                    await Load(
                        sources,
                        cacheDirectory,
                        enabled: false)));

            var ai = Assert.Single(
                merged.AutomaticLabSubGroupMerges.Where(x =>
                    merged.Get(x.Group).Name == "IA2404" &&
                    merged.Get(x.Course).Names.Any(name => name.StartsWith(
                        "Intel",
                        StringComparison.OrdinalIgnoreCase))));
            var labs = merged.EnumerateWeeklyLessons().Where(x =>
                    x.Lesson.Course == ai.Course &&
                    x.Lesson.Groups.Contains(ai.Group) &&
                    x.Lesson.Type == LessonType.Lab)
                .ToArray();
            Assert.NotEmpty(labs);
            Assert.All(
                labs,
                lesson =>
                {
                    Assert.Equal(SubGroup.All, lesson.Lesson.SubGroup);
                    Assert.True(lesson.Date.Period.IsUnspecified);
                });
            Assert.Contains(
                raw.EnumerateWeeklyLessons(),
                x => x.Lesson.Course == ai.Course &&
                    x.Lesson.Group == ai.Group &&
                    x.Date.Period == new PeriodId(0) &&
                    x.Lesson.SubGroup == new SubGroup("I"));
            Assert.Contains(
                raw.EnumerateWeeklyLessons(),
                x => x.Lesson.Course == ai.Course &&
                    x.Lesson.Group == ai.Group &&
                    x.Date.Period == new PeriodId(0) &&
                    x.Lesson.SubGroup == new SubGroup("II"));
            var weekly = new ManualWeeklyScheduledDateProvider(
                ScheduleLib.ScheduleDefaults.Config.StudyWeeks,
                ScheduleLib.ScheduleDefaults.Config.HolidayPeriods);
            var helper = new ScheduleDateProviderHelper(
                merged,
                LessonTimeConfig.CreateDefault(),
                ScheduleLib.ScheduleDefaults.Config.SemesterIntervalProvider());
            var dates = new ScheduledDateTimeProvider(weekly, helper)
                .Get(new() { Lessons = labs.Select(x => x.Id.AsAny()), Semester = Semester.Sem1 }).ToArray();
            Assert.NotEmpty(dates);
            Assert.True(
                dates.Min(x => x.DateTime) <
                new DateTime(
                    2026,
                    10,
                    11));
        }
        finally
        {
            Directory.Delete(cacheDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task WordPeriodAfterPdfRetainsOriginalElectivePolicyInFreshAndCachedLoads()
    {
        var root = Path.Combine(Path.GetTempPath(), "schedulelib-word-after-pdf-" + Guid.NewGuid().ToString("N"));
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
                    Path.Combine(
                        packaged,
                        sourceDate,
                        file),
                    Path.Combine(destination, file));
            }
            var fresh = await Load(
                sources,
                cache,
                enabled: false);
            Assert.Equal(3, fresh.Periods.Length);
            var older = fresh.EnumerateWeeklyLessons().Where(x =>
                    x.Date.Period == new PeriodId(0) &&
                    x.Lesson.Alternative == new Alternative("Antreprenoriat inovativ"))
                .ToArray();
            Assert.NotEmpty(older);
            var courses = older.Select(x => x.Lesson.Course).ToHashSet();
            var later = fresh.EnumerateWeeklyLessons()
                .Where(x => x.Date.Period == new PeriodId(2) && courses.Contains(x.Lesson.Course)).ToArray();
            Assert.NotEmpty(later);
            Assert.All(later, x => Assert.Equal(new Alternative("Antreprenoriat inovativ"), x.Lesson.Alternative));
            Assert.Contains(
                fresh.EnumerateWeeklyLessons(),
                x => x.Date.Period == new PeriodId(1) &&
                    x.Lesson.Alternative.Value is { } alternative &&
                    alternative.StartsWith("Antreprenoriat Gr.", StringComparison.Ordinal));
            Assert.Equal(
                Describe(fresh),
                Describe(
                    await Load(
                        sources,
                        cache,
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

    private static string[] Describe(Schedule schedule) => schedule.EnumerateWeeklyLessons().Select(x =>
            $"{x.Id}|{x.Lesson.Course}|{x.Lesson.Groups.ToString(schedule)}|{x.Lesson.GroupPartitionKey}|{x.Lesson.Type}|{x.Date}")
        .ToArray();
}
