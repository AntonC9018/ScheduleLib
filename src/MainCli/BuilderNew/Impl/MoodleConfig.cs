using AutoConstructor.Attributes;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Scraping.Common;

namespace MainCli.BuilderNew.Impl;

public sealed class MoodleConfig : IConfig<MoodleConfig>, ICredentialsConfig
{
    public static LayerConfigKey<MoodleConfig> Key { get; } = LayerConfigKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }
}

public static class CredentialsSourceResolver
{
    public static Credentials? GetCredentials(
        IConfiguration config,
        string serviceKey,
        string nameKey)
    {
        if (config.GetSection(nameKey) is not { } nameSection)
        {
            return null;
        }
        if (nameSection.GetSection(serviceKey) is not { } serviceSection)
        {
            return null;
        }
        var ret = serviceSection.Get<Credentials>();
        return ret;
    }
}

[AutoConstructor]
public sealed partial class CredentialsResolver
{
    private readonly ConfigProvider _configProvider;
    private readonly IConfiguration _configuration;

    public Credentials? Resolve(
        string serviceKey,
        CredentialsSource source)
    {
        var teacher = _configProvider.Get(TeacherLayerConfig.Key);
        var credentials = CredentialsSourceResolver.GetCredentials(
            _configuration,
            serviceKey: serviceKey,
            // May want this to be scoped too.
            nameKey: teacher.TeacherName.ToString());
        if (credentials != null)
        {
            return credentials;
        }
        if (source.IsRequired == false)
        {
            throw new InvalidOperationException("Credentials must be given as per configuration");
        }
        return null;
    }
}
[AutoConstructor]
public sealed partial class MoodleConfigHelper
{
    private readonly CredentialsResolver _resolver;
    private readonly ConfigProvider _configProvider;

    public Credentials? GetCredentials()
    {
        var config = _configProvider.Get(MoodleConfig.Key);
        if (config.Credentials == null)
        {
            return null;
        }
        var ret = _resolver.Resolve(serviceKey: "Moodle", config.Credentials);
        return ret;
    }
}
