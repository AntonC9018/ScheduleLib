using Argon;
using AutoConstructor.Attributes;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleFromDoc.Tests;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.ScheduleDefaults;

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
        var serviceProvider = scope.ServiceProvider;
        var scheduleBuilder = serviceProvider.GetRequiredService<ScheduleBuilder>();
        await IntegrationTestHelper.AddScheduleToBuilder(scheduleBuilder,
        serviceProvider.GetRequiredService<FilteredScheduleProvider>();
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
        services.AddScoped<FilteredScheduleProvider>();
        services.AddScoped<ScheduleProvider>();
        services.AddSingleton<ProcessSpaces>(Config.WhiteSpaceActionCourseName);
        services.AddSingleton<SemesterIntervalProvider>(sp =>
        {
            _ = sp;
            return Config.SemesterIntervalProvider();
        });

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
    private Schedule? _schedule;

    public ScheduleProvider(ScheduleBuilder builder)
    {
        _builder = builder;
    }

    public ValueTask<Schedule> Get()
    {
        if (_schedule is null)
        {
            _schedule = _builder.Build();
        }
        return ValueTask.FromResult(_schedule);
    }
}

[AutoConstructor]
public sealed partial class FilteredScheduleProvider
{
    private readonly ConfigProvider _configProvider;
    private readonly ScheduleProvider _scheduleProvider;
    private readonly LookupFacade _lookup;

    public async ValueTask<FilteredSchedule> Get()
    {
        var teacherConfig = _configProvider.GetConfig(TeacherLayerConfig.Key);
        var teacherName = teacherConfig.TeacherName;
        var schedule = await _scheduleProvider.Get();
        var teacherId = _lookup.Teacher(teacherName.ToNameModel())!.Value;

        var filter = FilterHelper.Builder()
            .WithLatestPeriod(schedule)
            .WithTeacher(teacherId);
        var filtered = schedule.Filter(filter);
        return filtered;
    }
}
