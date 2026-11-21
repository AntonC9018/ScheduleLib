using Microsoft.Extensions.Configuration;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.Tests;

public sealed class CredentialsHelperTests
{
    [Fact]
    public void GetCredentialsIncludesMissingKeyInMessage()
    {
        var config = new ConfigurationBuilder().Build();
        var ex = Assert.Throws<CredentialsMissingException>(() => config.GetCredentials("Missing:Section"));
        Assert.Equal(expected: "Missing:Section", actual: ex.ConfigKey);
        Assert.Contains(expectedSubstring: "Missing:Section", actualString: ex.Message);
    }
}
