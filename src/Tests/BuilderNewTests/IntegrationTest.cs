using System.Diagnostics;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Parsing;

public sealed class IntegrationTest
{
    [Fact]
    public async Task Test()
    {
        var builder = Builder.Build();
        var services = new ServiceCollection();
        services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
        services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
        services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();

        var serviceProvider = services.BuildServiceProvider();
        var teacherConfigs = builder.BaseLayer
            .GetPathsOfDescendantsOrSelf(x => x.Get(TeacherLayerConfig.Key).Exists);
        using var scope = serviceProvider.CreateScope();

        var list = new List<RegistryConfig>();
        foreach (var path in teacherConfigs)
        {
            Debug.Assert(path.Path[^1].ChildLayers.Count == 0);
            var registryConfig = path.ConstructConfig<RegistryConfig>(scope.ServiceProvider);
            Debug.Assert(registryConfig != null);
            list.Add(registryConfig);
        }

        await Verify(list);
    }

}
