using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace ScheduleLib.OnlineRegistry;

public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}

public static class CredentialsHelper
{
    public static Credentials? MaybeGetCredentials(this IConfiguration config)
    {
        var ret = config.GetRequiredSection("Registry").Get<Credentials>();
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

    public static Credentials? MaybeGetCredentials(Assembly userSecretsAssembly)
    {
        var b = new ConfigurationBuilder();
        b.AddUserSecrets(userSecretsAssembly);
        var config = b.Build();
        var ret = config.MaybeGetCredentials();
        return ret;
    }

    public static Credentials GetCredentials(Assembly userSecretsAssembly)
    {
        var ret = MaybeGetCredentials(userSecretsAssembly);
        if (ret == null)
        {
            throw new InvalidOperationException("Credentials not found.");
        }
        return ret;
    }
}
