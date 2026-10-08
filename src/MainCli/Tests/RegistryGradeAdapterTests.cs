using System.Net;
using Anton.LayeredData;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Io;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuizModels;
using ScheduleLib.Cli;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Config;
using ScheduleLib.Scraping.Common.Config;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using Xunit;
using HttpMethod = System.Net.Http.HttpMethod;

public sealed class RegistryGradeAdapterTests
{
    private const string GradeForm = "<form method='post' action='/grades'><input type='text' name='grade[1]' value='8'></form>";
    private const string LoginForm = "<form method='post' action='/login/index.php'><input name='username'><input name='password' type='password'><button type='submit'>Login</button></form>";

    [Theory]
    [InlineData(500, "server failure", false)]
    [InlineData(400, "bad request", true)]
    [InlineData(401, "unauthorized", true)]
    [InlineData(200, LoginForm, true)]
    [InlineData(200, "<div class='validation-summary-errors'>Invalid</div>", true)]
    [InlineData(200, "unconfirmed", false)]
    public async Task GradeResponseCannotClaimSuccess(int status, string body, bool rejected)
    {
        var auth = new Auth();
        var posts = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post) return Task.FromResult(Response(HttpStatusCode.OK, GradeForm));
            posts++;
            // A replay would succeed: the production requester must never send it.
            return Task.FromResult(Response(status == 401 && posts > 1 ? HttpStatusCode.OK : (HttpStatusCode) status,
                status == 401 && posts > 1 ? GradeForm : body));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/grades"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(rejected, error is RegistrySubmissionRejectedException);
        Assert.Equal(1, handler.Posts);
        Assert.Equal(0, auth.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GradeSuccessRequiresFreshMatchingValues(bool matches)
    {
        var reads = 0;
        using var handler = new Handler((request, _) => Task.FromResult(Response(HttpStatusCode.OK,
            request.Method == HttpMethod.Post ? GradeForm : ++reads == 1 || matches ? GradeForm : GradeForm.Replace("value='8'", "value='5'"))));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/grades"));
        if (matches) await navigator.SubmitGrades(document, default);
        else await Assert.ThrowsAsync<IOException>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(2, reads);
        Assert.Equal(1, handler.Posts);
    }

    [Theory]
    [InlineData(200, LoginForm)]
    [InlineData(400, "bad read")]
    [InlineData(500, "failed read")]
    public async Task FailedConfirmationIsUncertainEvenAfterAcceptedPost(int status, string body)
    {
        var reads = 0;
        using var handler = new Handler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Post || ++reads == 1 ? Response(HttpStatusCode.OK, GradeForm) : Response((HttpStatusCode) status, body)));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/grades"));
        await Assert.ThrowsAsync<IOException>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(1, handler.Posts);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task GradeReplaySuppressionEndsAfterSubmission()
    {
        var reads = 0;
        var auth = new Auth();
        using var handler = new Handler((request, _) => Task.FromResult(Response(
            request.Method == HttpMethod.Post || ++reads == 2 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, GradeForm)));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/grades"));
        await Assert.ThrowsAsync<RegistrySubmissionRejectedException>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(0, auth.Calls);
        await navigator.GetHtml(new("https://registry.test/grades"));
        Assert.Equal(1, auth.Calls);
        Assert.Equal(3, reads);
        Assert.Equal(1, handler.Posts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GradeRedirectRequiresConfirmedSavedValuesAndRejectsLogin(bool login)
    {
        using var handler = new Handler((request, _) =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var redirect = Response(HttpStatusCode.Redirect, "");
                redirect.Headers.Location = new(login ? "/login" : "/evaluation", UriKind.Relative);
                return Task.FromResult(redirect);
            }
            return Task.FromResult(Response(HttpStatusCode.OK,
                request.RequestUri!.AbsolutePath == "/grades" ? GradeForm : login ? LoginForm : "<p>Evaluation</p>"));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        var navigator = new OnlineRegistryNavigator(null!, new(context), services, default);
        var document = await navigator.GetHtml(new("https://registry.test/grades"));
        if (login) await Assert.ThrowsAsync<RegistrySubmissionRejectedException>(() => navigator.SubmitGrades(document, default));
        else await navigator.SubmitGrades(document, default);
        Assert.Equal(1, handler.Posts);
    }

    [Fact]
    public async Task LegacyRequesterStillReauthenticatesReads()
    {
        var reads = 0;
        var auth = new Auth();
        using var handler = new Handler((_, _) => Task.FromResult(Response(++reads == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, GradeForm)));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        await context.Browser.OpenAsync("https://registry.test/grades");
        Assert.Equal(2, reads);
        Assert.Equal(1, auth.Calls);
    }

    [Fact]
    public async Task LegacyPostRetryContractIsUnchanged()
    {
        var auth = new Auth();
        var posts = 0;
        using var handler = new Handler((request, _) => Task.FromResult(Response(
            request.Method == HttpMethod.Post && ++posts == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK, GradeForm)));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var context = ScrapingContext.Create(transport, auth);
        var document = await context.Browser.OpenAsync("https://registry.test/grades");
        await document.QuerySelector<AngleSharp.Html.Dom.IHtmlFormElement>("form")!.SubmitAsync();
        Assert.Equal(2, posts);
        Assert.Equal(1, auth.Calls);
    }

    [Theory]
    [InlineData(LoginForm)]
    [InlineData("<p>Unconfirmed login</p>")]
    public async Task RejectedMoodleLoginFailsBeforeScraping(string body)
    {
        using var handler = new Handler((request, _) => Task.FromResult(Response(HttpStatusCode.OK, request.Method == HttpMethod.Post ? body : LoginForm)));
        using var services = new ServiceCollection().AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => MoodleScrapingContext.Create(services, new() { Login = "test", Password = "test" }, default, transport));
        Assert.Equal(1, handler.Posts);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task MoodleLoginCancellationDisposesSuspendedTransportPromptly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post) return Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
            entered.SetResult();
            return pending.Task; // Deliberately ignores cancellation, like AngleSharp form submission.
        });
        using var services = new ServiceCollection().AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        var task = MoodleScrapingContext.Create(services, new() { Login = "test", Password = "test" }, cancellation.Token, transport);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(handler.Disposed);
            Assert.False(pending.Task.IsCompleted);
        }
        finally
        {
            using var response = Response(HttpStatusCode.OK, LoginForm);
            pending.TrySetResult(response);
        }
    }

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 130)]
    public async Task RealMoodlePlannerReturnsDocumentedExitAndReleasesAccountLock(bool cancel, int exit)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post) return Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
            entered.TrySetResult();
            return cancel ? pending.Task : Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var settings = await CliSettings.Load(new());
        settings.Tree.Defaults.Moodle().ConfigureValue(x => x.Credentials = new ValueCredentialsSource
        {
            Value = new() { Login = "test", Password = "test" },
        });
        var collection = CliRuntime.CreateServices(new(), AppConfiguration.ConfigureServices);
        settings.ConfigureServices(collection);
        await using var provider = AppConfiguration.BuildServiceProvider(collection);
        await using var scope = provider.CreateAsyncScope();
        await using var session = new Session(scope.ServiceProvider);
        var accountLock = new AccountLock();
        var planner = new ConfiguredRegistryGradePlanner((services, credentials, token) =>
            MoodleScrapingContext.Create(services, credentials, token, transport));
        var commands = new Commands(session, planner, accountLock);
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var task = commands.ImportGrades(new() { QuizId = "123" }, new(), new() { Profile = "test" }, new() { Json = true }, new() { Apply = true }, cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(accountLock.Held || !cancel);
            if (cancel) cancellation.Cancel();
            Assert.Equal(exit, await task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(accountLock.Held);
            Assert.True(session.Disposed);
            Assert.True(handler.Disposed);
            Assert.Equal(1, handler.Posts);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(exit, result.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Empty(result.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray());
            if (cancel) Assert.False(pending.Task.IsCompleted);
        }
        finally
        {
            Console.SetOut(original);
            using var response = Response(HttpStatusCode.OK, LoginForm);
            pending.TrySetResult(response);
        }
    }

    [Fact]
    public async Task MoodleAuthenticationRequiresPositiveLoggedInMarker()
    {
        using var handler = new Handler((request, _) => Task.FromResult(Response(HttpStatusCode.OK,
            request.Method == HttpMethod.Post ? "<a href='/login/logout.php?sesskey=test'>Logout</a>" : LoginForm)));
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(new MemoryCookieProvider(), client, handler);
        using var services = new ServiceCollection().AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
        using var context = await MoodleScrapingContext.Create(services, new() { Login = "test", Password = "test" }, default, transport);
        Assert.Equal(1, handler.Posts);
    }

    private sealed class Commands(Session session, IRegistryGradePlanner planner, AccountLock accountLock) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => session;
        protected override IRegistryGradePlanner CreateGradePlanner() => planner;
        protected override IRegistryAccountLock CreateAccountLock() => accountLock;
    }
    private sealed class Session(IServiceProvider services) : IConfiguredRegistrySession, IRegistryProvider
    {
        public bool Disposed;
        public IServiceProvider Services => services;
        public RegistryDestination Target { get; } = new("adapter-test", "https://registry.test/");
        public OnlineRegistryNavigator CreateNavigator(CancellationToken token) => throw new InvalidOperationException("Must fail before registry reads.");
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) => throw new InvalidOperationException();
        public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token) => Task.FromResult<IRegistrySession>(this);
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
    private sealed class AccountLock : IRegistryAccountLock
    {
        public bool Held;
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
        {
            Assert.False(Held);
            Held = true;
            return Task.FromResult<IAsyncDisposable>(new Lease(this));
        }
        private sealed class Lease(AccountLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { owner.Held = false; return ValueTask.CompletedTask; }
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };
    private sealed class Auth : IAuthHandler
    {
        public int Calls;
        public Task Authenticate(CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Posts;
        public bool Disposed;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post) Posts++;
            return send(request, cancellationToken);
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
