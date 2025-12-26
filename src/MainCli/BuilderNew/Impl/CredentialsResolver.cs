using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

[AutoConstructor]
public sealed partial class CredentialsFromConfigurationResolver : ICredentialsFromConfigurationResolver
{
    private readonly IConfiguration _configuration;
    private readonly ConfigProvider _configProvider;

    public Credentials? Get(string serviceKey)
    {
        var teacher = _configProvider.Get(TeacherLayerConfig.Key);
        var credentials = CredentialsSourceResolver.GetCredentials(
            _configuration,
            serviceKey: serviceKey,
            // May want this to be scoped too.
            nameKey: teacher.TeacherName.ToString());
        return credentials;
    }
}

