using System.Text;
using Argon;
using AutoConstructor.Attributes;
using MainCli;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using MainCli.BuilderNew.Retrieval;
using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleFromDoc.Tests;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;
using Tests.ScheduleCommon;

public sealed class IntegrationTest
{
    [Fact]
    public async Task Test()
    {
        var fixture = Fixture();
        var list = new List<LessonTopicsConfig>();
        foreach (var marker in fixture.ConfigBuilder.GetAllMarkers())
        {
            await using var scope = fixture.ServiceProvider.CreateMarkerScope(marker);
            var provider = scope.ServiceProvider.GetRequiredService<ConfigProvider>();
            var config = provider.GetConfig(LessonTopicsConfig.Key);
            list.Add(config);
        }

        await Verify(list)
            .UseStrictJson()
            .AddExtraSettings(x =>
            {
                x.DefaultValueHandling = DefaultValueHandling.Include;
                x.TypeNameHandling = TypeNameHandling.Auto;
            });
    }

    private readonly record struct MarkerConfigs(
        string Marker,
        List<object> Configs);

    [Fact]
    public async Task AllThingsWork()
    {
        var fixture = Fixture();
        var markers = fixture.ConfigBuilder.GetAllMarkers();
        List<MarkerConfigs> configs = new();
        foreach (var m in markers)
        {
            await using var scope = fixture.ServiceProvider.CreateMarkerScope(m);
            var configProvider = scope.ServiceProvider.GetRequiredService<ConfigProvider>();

            var configsOfMarker = new List<object>();
            configs.Add(new(m.TeacherName.ToString(), configsOfMarker));

            var helper = scope.ServiceProvider.GetRequiredService<IMarkerConfigHelper>();
            var currentPath = helper.GetCurrentPath(new(m))!.Value;
            var configKeys = currentPath
                .Path
                .SelectMany(x => x.ConfigKeys)
                .Distinct()
                .Where(x => x != TeacherLayerConfig.Key.Value)
                .OrderBy(x => x.Value)
                .ToArray();

            foreach (var key in configKeys)
            {
                var config = configProvider.GetConfigUntyped(key);
                configsOfMarker.Add(config);
            }
        }
        await Verify(configs)
            .UseStrictJson()
            .AddExtraSettings(x =>
            {
                x.DefaultValueHandling = DefaultValueHandling.Include;
                x.TypeNameHandling = TypeNameHandling.Auto;
            });
    }

    [Fact]
    public async Task TopicsLoaderTest()
    {
        var fixture = Fixture();
        await using var scope = fixture.ServiceProvider.CreateMarkerScope(x =>
        {
            x.TeacherName = NameHelper.Parse("Curmanschii Anton");
        });
        using var cancellationTokenSource = IntegrationTestHelper.CreateCts();
        var cancellationToken = cancellationTokenSource.Token;
        var serviceProvider = scope.ServiceProvider;
        await serviceProvider.InitializeSchedule(cancellationToken);

        var filteredSchedule = serviceProvider.GetRequiredService<LatestPeriodFilteredScheduleProvider>().Get();

        var outputDirectory = new TempOutputDirectoryService("output");
        outputDirectory.Initialize();

        const string outputPath = "output.xlsx";
        var task = serviceProvider.GenerateAllTeachersExcelTask(
            outputDirectory,
            filteredSchedule: filteredSchedule,
            outputFilePath: outputPath);

        await task.Run(cancellationToken);
        outputDirectory.TryOpenFileInExplorer(outputPath);
    }

    private (ServiceProvider ServiceProvider, ApplicationConfigBuilder ConfigBuilder) Fixture()
    {
        var services = new ServiceCollection();
        services
            .AddOptions<ManifestDirectoriesOptions>()
            .Configure(x =>
            {
                x.Directories.Add("data/topics");
            });
        services
            .AddOptions<StudyYearOptions>()
            .Configure(x =>
            {
                x.StudyYear = 2025;
                x.Semester = Semester.Sem2;
            });
        services.AddSingleton<LookupFacade>(sp =>
            sp.GetRequiredService<ScheduleBuilder>().Lookup());
        services.AddSingleton<GroupParseContext>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StudyYearOptions>>().Value;
            return GroupParseContext.Create(new()
            {
                CurrentStudyYear = options.StudyYear,
            });
        });
        services.AddSingleton<ScheduleBuilder>(sp =>
        {
            var builder = new ScheduleBuilder();
            builder.GroupParseContext = sp.GetRequiredService<GroupParseContext>();
            return builder;
        });

        services.AddSingleton(Config.CourseNameParser);
        services.AddSingleton<CourseNameUnifierConfig>(sp =>
        {
            var parserConfig = sp.GetRequiredService<CourseNameParserConfig>();
            var unificationConfig = Config.CourseNameUnificationConfig;
            var ret = CourseNameUnifierConfig.Create(parserConfig, unificationConfig);
            return ret;
        });
        services.AddSingleton<CourseNameUnifierModule>();
        services.AddSingleton<ProcessSpaces>(Config.WhiteSpaceActionCourseName);
        services.AddSingleton<SemesterIntervalProvider>(sp =>
        {
            _ = sp;
            return Config.SemesterIntervalProvider();
        });
        services.AddSingleton<ScheduleBuilderInitializer>();

        // These don't seem necessary?
        // I'm not sure how to set up the schedule in DI.
        services.AddSingleton<ScheduleProvider>();
        services.AddScoped<Schedule>(x =>
        {
            var provider = x.GetRequiredService<ScheduleProvider>();
            return provider.Get();
        });
        services.AddScoped<ScopeFilteredScheduleProvider>();
        services.AddScoped<FilteredSchedule>(x => x.GetRequiredService<ScopeFilteredScheduleProvider>().Get());
        services.AddSingleton<LatestPeriodFilteredScheduleProvider>();

        services.AddSingleton<LessonTimeConfig>(
            LessonTimeConfig.CreateDefault());
        services.AddSingleton<RegularSeminarDateProvider>();
        services.AddSingleton<LessonTypeDisplayHandler>();
        services.AddSingleton<ParityDisplayHandler>();
        services.AddSingleton<TimeSlotDisplayHandler>();
        services.AddSingleton<DayNameProvider>();
        services.AddTransient<StringBuilder>();

        services.AddMarkerServices();
        services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
        services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
        services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();
        services.RegisterBasicOperationsAndMergers<MoodleConfig>();
        services.RegisterBasicOperationsAndMergers<GoogleDriveConfig>();
        services.AddKeyEqualityComparer((LessonAttendanceSource s) => s.FilePath);
        services.RegisterBasicOperationsAndMergers<LessonAttendanceConfig>();

        var serviceProvider = services.BuildServiceProvider();

        var builder = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
        TestBuilderHelper.Configure(builder);

        return (serviceProvider, builder);
    }
}

public sealed class StudyYearOptions
{
    public required int StudyYear { get; set; } = -1;
    public required Semester Semester { get; set; } = Semester.Invalid;
}

public sealed class ScheduleProvider
{
    private readonly ScheduleBuilder _builder;

    public ScheduleProvider(ScheduleBuilder builder)
    {
        _builder = builder;
    }

    public Schedule Get()
    {
        var schedule = _builder.Build();
        return schedule;
    }
}

[AutoConstructor]
public sealed partial class LatestPeriodFilteredScheduleProvider
{
    private readonly Schedule _schedule;

    public FilteredSchedule Get()
    {
        var schedule = _schedule;
        var filter = FilterHelper.Builder()
            .WithLatestPeriod(schedule);
        var filtered = schedule.Filter(filter);
        return filtered;
    }
}

[AutoConstructor]
public sealed partial class ScopeFilteredScheduleProvider
{
    private readonly ConfigProvider _configProvider;
    private readonly Schedule _schedule;
    private readonly LookupFacade _lookup;

    public FilteredSchedule Get()
    {
        var teacherConfig = _configProvider.GetConfig(TeacherLayerConfig.Key);
        var teacherName = teacherConfig.TeacherName;
        var schedule = _schedule;
        var teacherId = _lookup.Teacher(teacherName.ToNameModel())!.Value;

        var filter = FilterHelper.Builder()
            .WithLatestPeriod(schedule)
            .WithTeacher(teacherId);
        var filtered = schedule.Filter(filter);
        return filtered;
    }
}

[AutoConstructor]
public sealed partial class ScheduleBuilderInitializer
{
    private readonly ScheduleBuilder _builder;
    private readonly CourseNameUnifierModule? _unifier;

    public async Task Initialize(CancellationToken cancellationToken)
    {
        _builder.EnableLookupModule();
        await IntegrationTestHelper.AddScheduleToBuilder(
            _builder,
            ScheduleTestHelper.TestSchedulePath,
            cancellationToken);

        if (_unifier is { } unifier)
        {
            unifier.Refresh(_builder);
        }
    }
}

public static class InitializationHelper
{
    public static async Task InitializeSchedule(
        this IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        var i = sp.GetRequiredService<ScheduleBuilderInitializer>();
        await i.Initialize(cancellationToken);
    }
}

public sealed class GoogleDriveUploadHandler
{
    // private readonly RegularSeminarDateProvider _seminarDateProvider;
}
