using Anton.LayeredConfig.Options;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Application.Config;

[AutoConstructor]
public sealed partial class MarkedConfigurationSectionResolver : IMarkedConfigurationSectionResolver
{
    private readonly CurrentUserNameProvider _userNameProvider;

    public IConfigurationSection? Get(IConfiguration c, string serviceKey)
    {
        var userName = _userNameProvider.Get();
        var credentials = ConfigurationSourceResolver.GetSourceSection(
            c,
            serviceKey: serviceKey,
            // May want this to be scoped too.
            nameKey: userName);
        return credentials;
    }
}

