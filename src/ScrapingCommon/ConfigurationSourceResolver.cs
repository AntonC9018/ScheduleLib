using Microsoft.Extensions.Configuration;

namespace ScheduleLib.Scraping.Common.Config;

public static class ConfigurationSourceResolver
{
    public static IConfigurationSection? GetSourceSection(
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
        return serviceSection;
    }
}
