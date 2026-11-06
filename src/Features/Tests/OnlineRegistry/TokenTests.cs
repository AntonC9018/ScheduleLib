using System.Net;
using System.Reflection;
using MemoryCookieProvider = AngleSharp.Io.MemoryCookieProvider;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.ScheduleDefaults;
using ScheduleLib.Scraping.Common;
using Tests.Common;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class TokenTests
{
    [RequiresLiveService(ExternalServices.UsmStudentRegistry)]
    public async Task TokenGeneratedOnLogIn()
    {
        // Throws, naming the key, when the user secrets are missing.
        var credentials = CredentialsHelper.GetCredentials(
            Assembly.GetExecutingAssembly(),
            Config.RegistryCredentialsConfigKey);

        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder);
        builder.TokenAuth(x => x.PasswordLoginCall(credentials));
        using var context = await builder.Build(CancellationToken.None);

        Assert.NotNull(FindToken(context));
    }

    [Fact]
    public async Task LoginPostsConfiguredFieldsAndKeepsTheTokenCookie()
    {
        var cookies = new MemoryCookieProvider();
        using var handler = new RecordedLoginHandler(cookies.Container);
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(cookies, client, handler);

        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder, TimeSpan.Zero);
        builder.TokenAuth(x => x.PasswordLoginCall(new()
        {
            Login = "test-login",
            Password = "test-password",
        }));
        using var context = await builder.Build(CancellationToken.None, transport);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(expected: HttpMethod.Post, actual: request.Method);
        Assert.Equal(expected: RecordedLoginHandler.LoginUrl, actual: request.Uri);
        Assert.Contains(expectedSubstring: "test-login", actualString: request.Body);
        Assert.Contains(expectedSubstring: "test-password", actualString: request.Body);
        Assert.NotNull(FindToken(context));
    }

    private static Cookie? FindToken(ScrapingContext context)
    {
        var services = context.BuilderServices!;
        return services.GetRequiredService<CookieContainer>()
            .FindCookie(services.GetRequiredService<TokenNamesConfig>());
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string Body);

    /// <summary>Replays the registry's anonymized login response: a redirect that sets the token cookie.</summary>
    private sealed class RecordedLoginHandler(CookieContainer cookies) : HttpMessageHandler
    {
        public static readonly Uri LoginUrl = new("http://crd.usm.md/studregistry/Account/Login");

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(request.Method, request.RequestUri!, body));
            cookies.Add(LoginUrl, new Cookie(name: "ForDecanat", value: "anonymized-token"));
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("/studregistry/", UriKind.Relative);
            return response;
        }
    }
}
