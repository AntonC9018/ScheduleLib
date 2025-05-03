using System.Net;
using System.Reflection;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class TokenTests
{
    [Fact]
    public async Task TokenGeneratedOnLogIn()
    {
        var credentials = CredentialsHelper.GetCredentials(Assembly.GetExecutingAssembly());
        using var http = HttpClientContext.Create();
        var context = new TokenRetrievalContext(new()
        {
            CookieContainer = http.Cookies,
            Credentials = credentials,
            HttpClient = http.Client,
        });
        var cancellationToken = CancellationToken.None;
        bool loggedIn = await context.LogIn(cancellationToken: cancellationToken);
        Assert.True(loggedIn);
        Assert.NotNull(context.TokenCookie);
    }
}
