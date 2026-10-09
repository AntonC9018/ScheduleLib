using System.Net;
using AngleSharp.Dom;
using AngleSharp.Io;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using Xunit;
using HttpMethod = System.Net.Http.HttpMethod;

public sealed class RegistryLessonAdapterTests
{
    private const string LessonForm = "<form method='post' action='/save'><input id='LessonDate' name='LessonDate' value='2026-01-01'></form>";
    private const string DeleteForm = "<form name='deleteLessonForm' method='post' action='/delete'><input name='id' value='1'></form>";

    public static IEnumerable<object[]> FailedResponses()
    {
        foreach (var kind in new[] { "create", "update", "delete" })
        {
            yield return [kind, 200, "<input type='password'>", true, null!];
            yield return [kind, 200, "<div class='validation-summary-errors'>Invalid</div>", true, null!];
            yield return [kind, 400, "bad request", true, null!];
            yield return [kind, 401, "unauthorized", true, null!];
            yield return [kind, 500, "server failure", false, null!];
            yield return [kind, 302, "redirect", true, "/Account/Login"];
            yield return [kind, 302, "redirect", false, "/saved"];
            yield return [kind, 303, "redirect", false, "/saved"];
            yield return [kind, 307, "redirect", false, "/save-again"];
            yield return [kind, 308, "redirect", false, "/save-again"];
            yield return [kind, 307, "redirect", false, "/Account/Login"];
            yield return [kind, 308, "redirect", false, "/Account/Login"];
        }
    }

    [Theory]
    [MemberData(nameof(FailedResponses))]
    public async Task FailedMutationResponseDoesNotCompleteOrReplay(string kind, int status, string body, bool rejected, string? location)
    {
        var auth = new Auth();
        using var handler = new Handler(request =>
        {
            if (request.Method != HttpMethod.Post) return Response(HttpStatusCode.OK, kind == "delete" ? DeleteForm : LessonForm);
            var response = Response((HttpStatusCode) status, body);
            if (location is not null) response.Headers.Location = new Uri(location, UriKind.Relative);
            return response;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/" + kind));
        var started = false;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => Submit(navigator, document, kind, () => started = true));
        Assert.Equal(rejected, error is RegistrySubmissionRejectedException);
        if (!rejected) Assert.IsAssignableFrom<IOException>(error);
        Assert.True(started);
        Assert.Equal(1, handler.Posts);
        Assert.Equal(1, handler.Reads);
        Assert.Equal(0, auth.Calls);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task AcceptedResponseCompletesExactlyOneSubmission(string kind)
    {
        using var handler = new Handler(request => Response(HttpStatusCode.OK,
            request.Method == HttpMethod.Post ? "<div>Saved</div>" : kind == "delete" ? DeleteForm : LessonForm));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/" + kind));
        var started = false;
        await Submit(navigator, document, kind, () => started = true);
        Assert.True(started);
        Assert.Equal(1, handler.Posts);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    public async Task InvalidFormNeverCrossesSubmissionBoundary(string kind)
    {
        using var handler = new Handler(_ => Response(HttpStatusCode.OK,
            (kind == "delete" ? DeleteForm : LessonForm).Replace("method='post'", "method='get'")));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/" + kind));
        var started = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Submit(navigator, document, kind, () => started = true));
        Assert.False(started);
        Assert.Equal(0, handler.Posts);
    }

    private static Task Submit(OnlineRegistryNavigator navigator, IDocument document, string kind, Action started) =>
        kind == "delete" ? navigator.SubmitDelete(document, started) : navigator.SubmitLesson(document, started);
    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html"),
    };
    private sealed class Auth : IAuthHandler
    {
        public int Calls;
        public Task Authenticate(CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        public int Posts;
        public int Reads;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) Posts++;
            else Reads++;
            return Task.FromResult(send(request));
        }
    }
}
