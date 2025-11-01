using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ScheduleLib.Scraping.Common;

public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}

public static class CredentialsHelper
{
    public static Credentials? MaybeGetCredentials(
        this IConfiguration config,
        string key)
    {
        var ret = config.GetRequiredSection(key).Get<Credentials>();
        if (ret == null)
        {
            return null;
        }
        if (ret.Login == null)
        {
            throw new InvalidOperationException("Login not found.");
        }
        if (ret.Password == null)
        {
            throw new InvalidOperationException("Password not found.");
        }
        return ret;
    }

    public static Credentials? MaybeGetCredentials(Assembly userSecretsAssembly, string key)
    {
        var b = new ConfigurationBuilder();
        b.AddUserSecrets(userSecretsAssembly);
        var config = b.Build();
        var ret = config.MaybeGetCredentials(key);
        return ret;
    }

    public static Credentials GetCredentials(Assembly userSecretsAssembly, string key)
    {
        var ret = MaybeGetCredentials(userSecretsAssembly, key);
        if (ret == null)
        {
            throw new InvalidOperationException("Credentials not found.");
        }
        return ret;
    }
}
