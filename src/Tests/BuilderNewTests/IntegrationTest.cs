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
        var services = new ServiceCollection();
        services.AddMarkerServices();
        services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
        services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
        services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();

        var serviceProvider = services.BuildServiceProvider();

        var builder = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
        TestBuilderHelper.Configure(builder);

        var list = new List<object>();
        foreach (var marker in builder.GetAllMarkers())
        {
            await using var scope = serviceProvider.CreateMarkerScope(marker);
            var provider = scope.ServiceProvider.GetRequiredService<ConfigProvider>();
            var config = provider.GetConfig<LessonTopicsConfig>();
            list.Add(new
            {
                FallbackProviders = config.FallbackProviders.Select(x => new
                {
                    x.LessonType,
                    Provider = new
                    {
                        Data = x.Provider,
                        Type = x.Provider.GetType(),
                    },
                }),
                Sources = config.Sources.Select(x => new
                {
                    Data = x,
                    Type = x.GetType(),
                }),
            });
        }

        await Verify(list)
            .UseStrictJson()
            .AddExtraSettings(x => x.DefaultValueHandling = DefaultValueHandling.Include);
    }

}
