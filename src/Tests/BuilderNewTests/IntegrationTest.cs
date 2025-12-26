using Argon;
using AutoConstructor.Attributes;
using MainCli;
using Anton.LayeredConfig;
using MainCli.BuilderNew.Impl;
using Anton.LayeredConfig.Retrieval;
using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleFromDoc.Tests;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
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
            var config = provider.Get(LessonTopicsConfig.Key);
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
                var config = configProvider.GetUntyped(key);
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

        var outputDirectory = new OutputDirectory("output");
        outputDirectory.Initialize();

        const string outputPath = "output.xlsx";
        await using var outputFile = outputDirectory.OpenFile(outputPath, FileMode.Create, FileAccess.Write);

        var task = ActivatorUtilities.GetServiceOrCreateInstance<GenerateAllTeachersExcelTaskHandler>(serviceProvider);

        await task.Run(new()
        {
            Schedule = filteredSchedule,
            CancellationToken = cancellationToken,
            OutputDirectory = outputFile,
            StringBuilder = new(),
        });
        outputDirectory.TryOpenFileInExplorer(outputPath);
    }

    private (ServiceProvider ServiceProvider, ApplicationConfigBuilder ConfigBuilder) Fixture()
    {
        var services = new ServiceCollection();
        services.AddScheduleServices();
        services.AddTaskHandlers();

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

        services.Replace(new(
            typeof(IScheduleInitializer),
            typeof(ScheduleBuilderInitializer),
            ServiceLifetime.Singleton));

        var serviceProvider = services.BuildServiceProvider(options: new()
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var builder = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
        TestBuilderHelper.Configure(builder);

        return (serviceProvider, builder);
    }
}

public sealed class GoogleDriveUploadHandler
{
    // private readonly RegularSeminarDateProvider _seminarDateProvider;
}

[AutoConstructor]
public sealed partial class ScheduleBuilderInitializer : IScheduleInitializer
{
    private readonly CourseNameUnifierModule? _unifier;

    public async Task Initialize(
        ScheduleBuilder builder,
        CancellationToken cancellationToken)
    {
        builder.EnableLookupModule();
        await IntegrationTestHelper.AddScheduleToBuilder(
            builder,
            ScheduleTestHelper.TestSchedulePath,
            cancellationToken);

        if (_unifier is { } unifier)
        {
            unifier.Refresh(builder);
        }
    }
}
