using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class TokenTests
{
    [Fact]
    public async Task TokenGeneratedOnLogIn()
    {
        var credentials = CredentialsHelper.GetCredentials(
            Assembly.GetExecutingAssembly(),
            RegistryScraping.CredentialsConfigKey);
        var cancellationToken = CancellationToken.None;

        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder);
        builder.TokenAuth(x => x.PasswordLoginCall(credentials));
        using var context = await builder.Build(cancellationToken);

        var cookie = context.Services!.GetRequiredService<CookieContainer>()
            .FindCookie(context.Services!.GetRequiredService<TokenNamesConfig>());

        Assert.NotNull(cookie);
    }
}
