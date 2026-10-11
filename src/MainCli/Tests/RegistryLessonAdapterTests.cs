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

    [Theory]
    [InlineData(302, "absent")]
    [InlineData(303, "absent")]
    [InlineData(302, "present")]
    [InlineData(303, "present")]
    [InlineData(302, "unexpected")]
    [InlineData(303, "unexpected")]
    [InlineData(302, "identity")]
    [InlineData(303, "identity")]
    [InlineData(302, "login")]
    [InlineData(303, "login")]
    [InlineData(302, "read-failure")]
    [InlineData(303, "read-failure")]
    [InlineData(302, "unrecognized")]
    [InlineData(303, "unrecognized")]
    public async Task DeletionRedirectRequiresAReadConfirmingTheKnownLessonIsAbsent(int status, string scenario)
    {
        const string list = "/studregistry/LessonAttendance/LessonsList/1?groupId=2";
        const string detail = "/studregistry/LessonAttendance/LessonDetail/42";
        var details = $"<a href='{list}'>Back</a><form name='deleteLessonForm' method='post' action='/delete'><input name='lessonId' value='{(scenario == "identity" ? "43" : "42")}'></form>";
        var saved = $"<a href='/studregistry/LessonAttendance/CreateLesson/1'>Adaugare</a><table><tr><th>lesson</th></tr>{(scenario == "present" ? $"<tr><td><a href='{detail}'>lesson</a></td></tr>" : "")}</table>";
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var redirect = Response((HttpStatusCode)status, "redirect");
                redirect.Headers.Location = new(scenario == "unexpected" ? "/other" : list, UriKind.Relative);
                return redirect;
            }
            if (request.RequestUri!.AbsolutePath == detail) return Response(HttpStatusCode.OK, details);
            return Response(scenario == "read-failure" ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
                scenario == "login" ? "<input type='password'>" : scenario == "unrecognized" ? "<table><tr><th>other</th></tr></table>" : saved);
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test" + detail));
        if (scenario == "absent") await navigator.SubmitDelete(document);
        else await Assert.ThrowsAsync<IOException>(() => navigator.SubmitDelete(document));
        Assert.Equal(1, handler.Posts);
        Assert.Equal(scenario is "unexpected" or "identity" ? 1 : 2, handler.Reads);
    }

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

    [Theory]
    [InlineData(302, "match")]
    [InlineData(303, "match")]
    [InlineData(302, "date")]
    [InlineData(302, "mode")]
    [InlineData(302, "topic")]
    [InlineData(302, "topic-line-endings")]
    [InlineData(302, "topic-different-multiline")]
    [InlineData(302, "attendance")]
    [InlineData(302, "missing")]
    [InlineData(302, "duplicate")]
    [InlineData(302, "foreign")]
    [InlineData(302, "list-login")]
    [InlineData(302, "edit-login")]
    [InlineData(302, "edit-failure")]
    public async Task SavedLessonRedirectRequiresUniqueMatchingValuesWithoutReplayingPost(int status, string scenario)
    {
        const string list = "/studregistry/LessonAttendance/LessonsList/1?groupId=2";
        const string edit = "/studregistry/LessonAttendance/Edit/3";
        const string fields = "<input id='LessonDate' name='LessonDate' value='2026-09-03T13:15'>"
            + "<select id='LessonMode' name='LessonMode'><option selected value='curs'>curs</option></select>"
            + "<textarea id='LessonTopic' name='LessonTopic'>Synthetic topic</textarea>"
            + "<input name='StudentList[1].NotaStr' value='a'>";
        var form = $"<a href='{list}'>Back</a><form method='post' action='/save'>{fields}</form>";
        if (scenario is "topic-line-endings" or "topic-different-multiline")
            form = form.Replace("Synthetic topic", "Synthetic first line\nsecond line");
        var auth = new Auth();
        using var handler = new Handler(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var response = Response((HttpStatusCode)status, "");
                response.Headers.Location = scenario == "foreign" ? new Uri("https://elsewhere.test" + list) : new Uri(list, UriKind.Relative);
                return response;
            }
            if (request.RequestUri!.AbsolutePath.Contains("LessonsList"))
            {
                if (scenario == "list-login") return Response(HttpStatusCode.OK, "<input type='password'>");
                var row = $"<tr><td>03.09.2026 13:15 curs</td><td>Synthetic topic</td><td><a href='{edit}'>Edit</a></td></tr>";
                return Response(HttpStatusCode.OK, "<table><tr><th>Date</th><th>Topic</th><th>Edit</th></tr>"
                    + (scenario == "missing" ? "" : row)
                    + (scenario == "duplicate" ? row.Replace("Edit/3", "Edit/4") : "") + "</table>");
            }
            if (request.RequestUri.AbsolutePath.Contains("/Edit/"))
            {
                if (scenario == "edit-login") return Response(HttpStatusCode.OK, "<input type='password'>");
                if (scenario == "edit-failure") return Response(HttpStatusCode.InternalServerError, "failed");
                return Response(HttpStatusCode.OK, scenario switch
                {
                    "date" => form.Replace("2026-09-03T13:15", "2026-09-03T15:00"),
                    "mode" => form.Replace("value='curs'", "value='laborator'"),
                    "topic" => form.Replace("Synthetic topic", "Different topic"),
                    "topic-line-endings" => form.Replace("\n", "&#13;&#10;"),
                    "topic-different-multiline" => form.Replace("second line", "Different line").Replace("\n", "&#13;&#10;"),
                    "attendance" => form.Replace("value='a'", "value='am'"),
                    _ => form,
                });
            }
            return Response(HttpStatusCode.OK, form);
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/create"));
        if (scenario is "match" or "topic-line-endings") await navigator.SubmitLesson(document);
        else await Assert.ThrowsAsync<IOException>(() => navigator.SubmitLesson(document));
        Assert.Equal(1, handler.Posts);
        Assert.Equal(scenario == "foreign" ? 1 : scenario is "missing" or "list-login" ? 2 : scenario == "duplicate" ? 4 : 3, handler.Reads);
        Assert.Equal(0, auth.Calls);
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
