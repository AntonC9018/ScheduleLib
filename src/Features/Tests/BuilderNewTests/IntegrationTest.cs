using Argon;
using AutoConstructor.Attributes;
using ScheduleLib.Application.Core;
using Anton.LayeredData;
using ScheduleLib.Application.Config;
using Anton.LayeredData.Retrieval;
using ScheduleLib.Application.Core.Helper;
using Microsoft.Extensions.DependencyInjection;
using ScheduleFromDoc.Tests;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.DependencyInjection.ScopePlans;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Dates;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using Tests.ScheduleCommon;

public sealed class IntegrationTest
{
    /// <summary>The service provider and layered tree builder shared by one integration fixture.</summary>
    private readonly record struct FixtureSetup(ServiceProvider ServiceProvider, TreeBuilder ConfigBuilder);

    [Fact]
    public async Task LessonTopicsMergeTest()
    {
        var fixture = Fixture();
        var list = new List<LessonTopicsConfig?>();
        foreach (var marker in fixture.ConfigBuilder
                     .GetAllMarkers()
                     .OrderBy(x => x.TeacherName.ToString()))
        {
            await using var scope = await fixture.ServiceProvider.OpenMarkerScope(marker, CancellationToken.None);
            var config = scope.Handler.Get(LessonTopicsConfig.Key);
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
    public async Task MarkerScopeRejectsAMissingMarker()
    {
        var fixture = Fixture();
        var marker = ScopeNode.Unkeyed(typeof(TeacherLayerConfig));
        var plan = fixture.ServiceProvider.GetRequiredService<ScopePlanGraph>()
            .GetPlan(typeof(DataProvider));
        Assert.Contains(expected: marker, collection: plan.Seeds);

        static void SupplyNothing(ScopeInputs inputs)
        {
        }

        Task<HostScope<DataProvider>> OpenWithoutMarker() =>
            HostScope<DataProvider>.Open(fixture.ServiceProvider, SupplyNothing, CancellationToken.None);

        var error = await Assert.ThrowsAsync<MissingSeedsException>(OpenWithoutMarker);
        Assert.Equal(expected: marker, actual: Assert.Single(error.Seeds));
    }

    [Fact]
    public async Task TopicsLoaderTest()
    {
        var fixture = Fixture();
        using var cancellationTokenSource = IntegrationTestHelper.CreateCts();
        var cancellationToken = cancellationTokenSource.Token;
        var source = new ScheduleSource(DataDirectory: "data");
        var teacher = new TeacherLayerConfig
        {
            TeacherName = NameHelper.Parse("Curmanschii Anton"),
        };
        await using var scope = await HostScope<LoadedSchedule>.Open(
            fixture.ServiceProvider,
            inputs => inputs.Add(source)
                .Add(teacher),
            cancellationToken);
        var serviceProvider = scope.ServiceProvider;

        var filteredSchedule = serviceProvider.GetRequiredService<LatestPeriodFilteredScheduleProvider>()
            .Get();

        var outputDirectory = new OutputDirectory("output");
        outputDirectory.Initialize();

        const string outputPath = "output.xlsx";
        await using var outputFile = outputDirectory.OpenFile(
            outputPath,
            FileMode.Create,
            FileAccess.ReadWrite);

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

    private FixtureSetup Fixture()
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

        services.AddInitialized<LoadedSchedule, TestScheduleInitializer>();

        var serviceProvider = services.BuildServiceProvider(options: new()
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        var builder = serviceProvider.GetRequiredService<TreeBuilder>();
        TestBuilderHelper.Configure(builder);

        return new(serviceProvider, builder);
    }
}

public sealed class GoogleDriveUploadHandler
{
    // private readonly RegularSeminarDateProvider _seminarDateProvider;
}

/// <summary>Loads the saved test schedule instead of the schedule sources.</summary>
[AutoConstructor]
public sealed partial class TestScheduleInitializer : IInitializer<LoadedSchedule>
{
    private readonly CourseNameUnifierConfig _courseNames;

    public async ValueTask<LoadedSchedule> InitializeAsync(CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(ScheduleTestHelper.TestSchedulePath);
        var model = await ScheduleSerializer.Deserialize(input, cancellationToken);
        var schedule = SerializationModels.ConvertToSchedule(model);
        return LoadedSchedule.Create(schedule, _courseNames);
    }
}
