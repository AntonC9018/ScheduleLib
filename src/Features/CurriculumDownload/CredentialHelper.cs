using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ScheduleLib.Curriculum.Download;

public sealed class MicrosoftAuthConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
}

public static class CredentialHelper
{
    public static IConfiguration CreateDefaultConfiguration(Assembly assembly)
    {
        var config = new ConfigurationBuilder();
        config.AddUserSecrets(assembly);
        var ret = config.Build();
        return ret;
    }

    public static MicrosoftAuthConfig GetMicrosoftGraphAuth(this IConfiguration c)
    {
        var microsoft = c.GetRequiredSection("Microsoft");
        var ret = microsoft.Get<MicrosoftAuthConfig>();
        if (ret is null)
        {
            throw new InvalidOperationException("Microsoft null");
        }
        if (ret.TenantId == null)
        {
            throw new InvalidOperationException("TenantId null");
        }
        if (ret.ClientId == null)
        {
            throw new InvalidOperationException("ClientId null");
        }
        return ret;
    }

}
