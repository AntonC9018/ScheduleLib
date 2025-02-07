using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ReaderApp.OnlineRegistry;

public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}

public static class OnlineRegistryCredentialsHelper
{
    public static Credentials GetCredentials()
    {
        var b = new ConfigurationBuilder();
        b.AddUserSecrets<Program>();
        var config = b.Build();
        var ret = config.GetRequiredSection("Registry").Get<Credentials>();
        if (ret == null)
        {
            throw new InvalidOperationException("Credentials not found.");
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


}
