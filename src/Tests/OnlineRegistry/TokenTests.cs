using System.Net;
using System.Reflection;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class TokenTests
{
    [Fact]
    public async Task TokenGeneratedOnLogIn()
    {
        var cookies = new CookieContainer();
        var credentials = CredentialsHelper.GetCredentials(Assembly.GetExecutingAssembly());
        using var http = RegistryScrapingContext.CreateHttpClient(cookies);
        var context = new TokenRetrievalContext(new()
        {
            CookieContainer = cookies,
            Credentials = credentials,
            HttpClient = http.Client,
        });
        var cancellationToken = CancellationToken.None;
        bool loggedIn = await context.LogIn(cancellationToken: cancellationToken);
        Assert.True(loggedIn);
        Assert.NotNull(context.TokenCookie);
    }
}
