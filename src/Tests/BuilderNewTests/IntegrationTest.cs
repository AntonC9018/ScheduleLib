using System.Diagnostics;
using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Parsing;

public sealed class IntegrationTest
{
    [Fact]
    public async Task Test()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
        services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
        services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();

        var serviceProvider = services.BuildServiceProvider();

        var builder = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
        TestBuilderHelper.Configure(builder);

        var list = new List<LessonTopicsConfig>();
        foreach (var marker in builder.GetAllMarkers())
        {
            await using var scope = serviceProvider.CreateMarkerScope(marker);
            var provider = scope.ServiceProvider.GetRequiredService<ConfigProvider>();
            var config = provider.GetConfig<LessonTopicsConfig>();
            list.Add(config);
        }

        await Verify(list);
    }

}
