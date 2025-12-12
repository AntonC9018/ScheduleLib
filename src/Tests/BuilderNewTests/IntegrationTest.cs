using System.Reflection;
using Argon;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Parsing;

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

    private (ServiceProvider ServiceProvider, ApplicationConfigBuilder ConfigBuilder) Fixture()
    {
        var services = new ServiceCollection();
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
