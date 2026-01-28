using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace MainCli.BuilderNew.Impl;

public sealed class GoogleCalendarConfig : IConfig<GoogleCalendarConfig>
{
    public static LayerConfigKey<GoogleCalendarConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleCalendarConfig>();
    public GoogleCredentialsConfig? Credentials { get; set; }
    public string? Tag { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.RegisterBasicOperationsAndMergers<GoogleCalendarConfig>();
        services.AddConfigProvider(GoogleCalendarConfig.Key);
    }
}

