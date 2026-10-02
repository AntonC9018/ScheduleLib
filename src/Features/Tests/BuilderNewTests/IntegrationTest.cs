using Argon;
using AutoConstructor.Attributes;
using ScheduleLib.Application.Core;
using Anton.LayeredData;
using ScheduleLib.Application.Config;
using Anton.LayeredData.Retrieval;
using ScheduleLib.Application.Core.Helper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleFromDoc.Tests;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using Tests.ScheduleCommon;

public sealed class IntegrationTest
{
    [Fact]
    public async Task LessonTopicsMergeTest()
    {
        var fixture = Fixture();
        var list = new List<LessonTopicsConfig?>();
        foreach (var marker in fixture.ConfigBuilder
                     .GetAllMarkers()
                     .OrderBy(x => x.TeacherName.ToString()))
        {
            await using var scope = fixture.ServiceProvider.CreateMarkerScope(marker);
            var provider = scope.ServiceProvider.GetRequiredService<DataProvider>();
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
        await using var outputFile = outputDirectory.OpenFile(outputPath, FileMode.Create, FileAccess.ReadWrite);

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

    private (ServiceProvider ServiceProvider, TreeBuilder ConfigBuilder) Fixture()
    {
        var services = new ServiceCollection();
        services.AddAllServices();

        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        services.Configure<RegularSeminarDateConfig>(x =>
        {
            x.Day = DayOfWeek.Wednesday;
            x.Time = new(hour: 15, minute: 00);
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
        var builder = serviceProvider.GetRequiredService<TreeBuilder>();
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
