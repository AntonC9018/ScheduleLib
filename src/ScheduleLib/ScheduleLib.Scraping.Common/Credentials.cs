using System.Reflection;
using Microsoft.Extensions.Configuration;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Scraping.Common;

public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}

public static class CredentialsHelper
{
    public static Credentials GetCredentials(
        this IConfiguration config,
        string key)
    {
        var ret = config.MaybeGetCredentials(key);
        if (ret == null)
        {
            throw CredentialsMissingException.ForMissingCredentials(key);
        }
        return ret;
    }

    public static Credentials? MaybeGetCredentials(
        this IConfiguration config,
        string key)
    {
        var section = config.GetSection(key);
        if (!section.Exists())
        {
            return null;
        }
        var ret = section.Get<Credentials>();
        if (ret == null)
        {
            return null;
        }
        if (ret.Login == null)
        {
            throw new CredentialsMissingException(
                message: "Login not found.",
                configKey: key,
                missingField: CredentialsField.Login);
        }
        if (ret.Password == null)
        {
            throw new CredentialsMissingException(
                message: "Password not found.",
                configKey: key,
                missingField: CredentialsField.Password);
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
            throw CredentialsMissingException.ForMissingCredentials(key);
        }
        return ret;
    }
}
