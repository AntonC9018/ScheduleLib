using Anton.LayeredConfig;
using AutoConstructor.Attributes;
using Anton.LayeredConfig.Retrieval;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

public sealed class MoodleConfig : IConfig<MoodleConfig>, ICredentialsConfig
{
    public static LayerConfigKey<MoodleConfig> Key { get; } = LayerConfigKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }
}

[AutoConstructor]
public sealed partial class MoodleConfigHelper
{
    private readonly ICredentialsResolver _resolver;
    private readonly ConfigProvider _configProvider;

    public Credentials? GetCredentials()
    {
        var config = _configProvider.Get(MoodleConfig.Key);
        if (config is null)
        {
            return null;
        }
        if (config.Credentials == null)
        {
            return null;
        }
        var ret = _resolver.Resolve(serviceKey: "Moodle", config.Credentials);
        return ret;
    }
}
