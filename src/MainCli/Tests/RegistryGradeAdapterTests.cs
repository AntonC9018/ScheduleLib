using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Io;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuizModels;
using ScheduleLib.Application.Config;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;
using HttpMethod = System.Net.Http.HttpMethod;

public sealed class RegistryGradeAdapterTests
{
    private const string GradeForm =
        "<form method='post' action='/grades'><input type='text' name='grade[1]' value='8'></form>";

    private const string LoginForm =
        "<form method='post' action='/login/index.php'><input name='username'><input name='password' type='password'><button type='submit'>Login</button></form>";

    [Theory]
    [InlineData("<input type='text' value='8'>")]
    [InlineData("<input type='text' name='grade[1]' value='8'><input type='text' name='grade[1]' value='5'>")]
    public async Task InvalidGradeFieldsFailBeforeProductionPostBoundary(string inputs)
    {
        using var handler = new Handler((_, _) =>
        {
            return Task.FromResult(
                Response(HttpStatusCode.OK, $"<form method='post' action='/grades'>{inputs}</form>"));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        var started = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return navigator.SubmitGrades(
                document,
                default,
                () =>
                {
                    started = true;
                });
        });
        Assert.False(started);
        Assert.Equal(expected: 0, actual: handler.Posts);
    }

    [Theory]
    [InlineData(
        500,
        "server failure",
        false)]
    [InlineData(
        400,
        "bad request",
        true)]
    [InlineData(
        401,
        "unauthorized",
        true)]
    [InlineData(
        200,
        LoginForm,
        true)]
    [InlineData(
        200,
        "<div class='validation-summary-errors'>Invalid</div>",
        true)]
    [InlineData(
        200,
        "unconfirmed",
        false)]
    public async Task GradeResponseCannotClaimSuccess(
        int status,
        string body,
        bool rejected)
    {
        var auth = new Auth();
        var posts = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return Task.FromResult(Response(HttpStatusCode.OK, GradeForm));
            }

            posts++;

            // A replay would succeed: the production requester must never send it.
            HttpStatusCode status2;
            if (!(status != 401) && (posts > 1))
            {
                status2 = HttpStatusCode.OK;
            }
            else
            {
                status2 = (HttpStatusCode) status;
            }

            string body2;
            if (!(status != 401) && (posts > 1))
            {
                body2 = GradeForm;
            }
            else
            {
                body2 = body;
            }

            var result2 = Response(
                status2,
                body2);
            var fromResult =
                Task.FromResult(
                    result2);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(expected: rejected, actual: error is RegistrySubmissionRejectedException);
        Assert.Equal(expected: 1, actual: handler.Posts);
        Assert.Equal(expected: 0, actual: auth.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GradeSuccessRequiresFreshMatchingValues(bool matches)
    {
        var reads = 0;
        using var handler = new Handler((request, _) =>
        {
            var status = HttpStatusCode.OK;
            string body;
            if (request.Method == HttpMethod.Post)
            {
                body = GradeForm;
            }
            else
            {
                int readAttempt = ++reads;

                bool condition = readAttempt == 1 || matches;


                string text;
                if (condition)
                {
                    text = GradeForm;
                }
                else
                {
                    string replace = GradeForm.Replace("value='8'", "value='5'");
                    text = replace;
                }

                body = text;
            }

            var result = Response(
                status,
                body);
            var fromResult =
                Task.FromResult(
                    result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        if (matches)
        {
            await navigator.SubmitGrades(document, default);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => navigator.SubmitGrades(document, default));
        }

        Assert.Equal(expected: 2, actual: reads);
        Assert.Equal(expected: 1, actual: handler.Posts);
    }

    [Theory]
    [InlineData(200, LoginForm)]
    [InlineData(400, "bad read")]
    [InlineData(500, "failed read")]
    public async Task FailedConfirmationIsUncertainEvenAfterAcceptedPost(int status, string body)
    {
        var reads = 0;
        using var handler = new Handler((request, _) =>
        {
            bool condition;
            if (request.Method == HttpMethod.Post)
            {
                condition = true;
            }
            else
            {
                int readAttempt = ++reads;
                condition = readAttempt == 1;
            }

            HttpResponseMessage result;
            if (condition)
            {
                var response = Response(HttpStatusCode.OK, GradeForm);
                result = response;
            }
            else
            {
                var status2 = (HttpStatusCode) status;
                string body2 = body;
                var response2 = Response(status2, body2);
                result = response2;
            }

            var fromResult =
                Task.FromResult(
                    result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        await Assert.ThrowsAsync<IOException>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(expected: 1, actual: handler.Posts);
        Assert.Equal(expected: 2, actual: reads);
    }

    [Theory]
    [InlineData(
        302,
        200,
        LoginForm)]
    [InlineData(
        303,
        200,
        LoginForm)]
    [InlineData(
        302,
        200,
        "<div class='validation-summary-errors'>Invalid</div>")]
    [InlineData(
        303,
        200,
        "<div class='validation-summary-errors'>Invalid</div>")]
    [InlineData(
        302,
        401,
        "unauthorized")]
    [InlineData(
        303,
        401,
        "unauthorized")]
    [InlineData(
        302,
        403,
        "forbidden")]
    [InlineData(
        303,
        403,
        "forbidden")]
    [InlineData(
        302,
        500,
        "failed read")]
    [InlineData(
        303,
        500,
        "failed read")]
    public async Task FailedConfirmationAfterGradeRedirectIsUncertain(
        int redirectStatus,
        int readStatus,
        string body)
    {
        var reads = 0;
        var started = false;
        var auth = new Auth();
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(expected: "/grades", actual: request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Post)
            {
                var redirect = Response((HttpStatusCode) redirectStatus, "");
                redirect.Headers.Location = new("/evaluation", UriKind.Relative);
                return Task.FromResult(redirect);
            }
            int completedCalls = ++reads;
            if (completedCalls == 1)
            {
                return Task.FromResult(Response(HttpStatusCode.OK, GradeForm));
            }

            // Legacy read authentication may retry the GET, but must never replay the POST.
            bool condition = !(readStatus != 401) && (reads > 2);


            HttpResponseMessage result2;
            if (condition)
            {
                var response = Response(HttpStatusCode.OK, LoginForm);
                result2 = response;
            }
            else
            {
                var status = (HttpStatusCode) readStatus;
                string body2 = body;
                var response2 = Response(status, body2);
                result2 = response2;
            }

            var fromResult =
                Task.FromResult(
                    result2);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));

        await Assert.ThrowsAsync<IOException>(() =>
        {
            return navigator.SubmitGrades(
                document,
                default,
                () =>
                {
                    started = true;
                });
        });

        Assert.True(started);
        Assert.Equal(expected: 1, actual: handler.Posts);
        int expected2;
        if (readStatus == 401)
        {
            expected2 = 3;
        }
        else
        {
            expected2 = 2;
        }

        int actual2 = reads;
        Assert.Equal(expected: expected2, actual: actual2);
        int expected3;
        if (readStatus == 401)
        {
            expected3 = 1;
        }
        else
        {
            expected3 = 0;
        }

        int actual3 = auth.Calls;
        Assert.Equal(expected: expected3, actual: actual3);
    }

    [Fact]
    public async Task GradeReplaySuppressionEndsAfterSubmission()
    {
        var reads = 0;
        var auth = new Auth();
        using var handler = new Handler((request, _) =>
        {
            bool condition;
            if (request.Method == HttpMethod.Post)
            {
                condition = true;
            }
            else
            {
                int readAttempt = ++reads;
                condition = readAttempt == 2;
            }

            HttpStatusCode status;
            if (condition)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            var result = Response(
                status,
                body);
            var fromResult =
                Task.FromResult(
                    result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, auth);
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        var rejection =
            await Assert.ThrowsAsync<RegistryOperationRejectedException>(() =>
                navigator.SubmitGrades(document, default));
        Assert.Equal(expected: "grade submission", actual: rejection.Operation);
        Assert.Equal(expected: "Registry rejected grade submission.", actual: rejection.Message);
        Assert.Equal(expected: 0, actual: auth.Calls);

        await navigator.GetHtml(new Uri("https://registry.test/grades"));
        Assert.Equal(expected: 1, actual: auth.Calls);
        Assert.Equal(expected: 3, actual: reads);
        Assert.Equal(expected: 1, actual: handler.Posts);
    }

    [Theory]
    [InlineData(302, false)]
    [InlineData(303, false)]
    [InlineData(302, true)]
    [InlineData(303, true)]
    public async Task GradeRedirectRequiresConfirmedSavedValuesAndRejectsLogin(int status, bool login)
    {
        var reads = 0;
        using var handler = new Handler((request, _) =>
        {
            Assert.Equal(expected: "/grades", actual: request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Post)
            {
                var redirect = Response((HttpStatusCode) status, "");
                var httpResponseHeaders = redirect.Headers;
                string uriString;
                if (login)
                {
                    uriString = "/login";
                }
                else
                {
                    uriString = "/evaluation";
                }

                var uriKind = UriKind.Relative;
                Uri uri = new(uriString, uriKind);
                httpResponseHeaders.Location = uri;
                return Task.FromResult(redirect);
            }
            reads++;

            return Task.FromResult(Response(HttpStatusCode.OK, GradeForm));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));
        if (login)
        {
            await Assert.ThrowsAsync<RegistrySubmissionRejectedException>(() =>
                navigator.SubmitGrades(document, default));
        }
        else
        {
            await navigator.SubmitGrades(document, default);
        }

        Assert.Equal(expected: 1, actual: handler.Posts);
        int expected2;
        if (login)
        {
            expected2 = 1;
        }
        else
        {
            expected2 = 2;
        }

        int actual2 = reads;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Theory]
    [InlineData(307, "/grades")]
    [InlineData(308, "/grades")]
    [InlineData(307, "/Account/Login")]
    [InlineData(308, "/Account/Login")]
    public async Task GradeRedirectThatPreservesPostIsUncertain(int status, string location)
    {
        var reads = 0;
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post)
            {
                reads++;

                return Task.FromResult(Response(HttpStatusCode.OK, GradeForm));
            }
            var redirect = Response((HttpStatusCode) status, "");
            redirect.Headers.Location = new(location, UriKind.Relative);
            return Task.FromResult(redirect);
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        IRegistryErrorHandler errorHandler = null!;
        RegistryScrapingContext context2 = new(context);
        IServiceProvider serviceProvider = services;
        CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler,
            context2,
            serviceProvider,
            cancellationToken);

        var document = await navigator.GetHtml(new Uri("https://registry.test/grades"));

        await Assert.ThrowsAsync<FormSubmissionRedirectException>(() => navigator.SubmitGrades(document, default));

        Assert.Equal(expected: 1, actual: handler.Posts);
        Assert.Equal(expected: 1, actual: reads);
    }

    [Fact]
    public async Task LegacyRequesterStillReauthenticatesReads()
    {
        var reads = 0;
        var auth = new Auth();
        using var handler = new Handler((_, _) =>
        {
            int readAttempt = ++reads;
            HttpStatusCode status;
            if (readAttempt == 1)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            var result = Response(status, body);
            var fromResult =
                Task.FromResult(result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, auth);
        await context.Browser.OpenAsync("https://registry.test/grades");
        Assert.Equal(expected: 2, actual: reads);
        Assert.Equal(expected: 1, actual: auth.Calls);
    }

    [Fact]
    public async Task LegacyPostRetryContractIsUnchanged()
    {
        var auth = new Auth();
        var posts = 0;
        using var handler = new Handler((request, _) =>
        {
            bool condition;
            if (request.Method == HttpMethod.Post)
            {
                int postAttempt = ++posts;
                condition = postAttempt == 1;
            }
            else
            {
                condition = false;
            }

            HttpStatusCode status;
            if (condition)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            var result = Response(
                status,
                body);
            var fromResult =
                Task.FromResult(
                    result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, auth);
        var document = await context.Browser.OpenAsync("https://registry.test/grades");
        await document.QuerySelector<AngleSharp.Html.Dom.IHtmlFormElement>("form")!
            .SubmitAsync();
        Assert.Equal(expected: 2, actual: posts);
        Assert.Equal(expected: 1, actual: auth.Calls);
    }

    [Theory]
    [InlineData(LoginForm)]
    [InlineData("<p>Unconfirmed login</p>")]
    public async Task RejectedMoodleLoginFailsBeforeScraping(string body)
    {
        using var handler = new Handler((request, _) =>
        {
            var status = HttpStatusCode.OK;
            string body2;
            if (request.Method == HttpMethod.Post)
            {
                body2 = body;
            }
            else
            {
                body2 = LoginForm;
            }

            var result = Response(status, body2);
            var fromResult =
                Task.FromResult(result);
            return fromResult;
        });
        using var services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        await Assert.ThrowsAsync<ScrapingAuthenticationException>(() => MoodleScrapingContext.Create(
            services,
            new()
            {
                Login = "test",
                Password = "test",
            },
            default,
            transport));
        Assert.Equal(expected: 1, actual: handler.Posts);
        Assert.True(handler.Disposed);
    }

    [Fact]
    public async Task MoodleLoginCancellationDisposesSuspendedTransportPromptly()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new Handler((request, _) =>
        {
            if (request.Method != HttpMethod.Post)
            {
                return Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
            }

            entered.SetResult();
            return pending.Task; // Deliberately ignores cancellation, like AngleSharp form submission.
        });
        using var services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        var task = MoodleScrapingContext.Create(
            services,
            new()
            {
                Login = "test",
                Password = "test",
            },
            cancellation.Token,
            transport);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            {
                return task.WaitAsync(TimeSpan.FromSeconds(5));
            });
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
            if (request.Method != HttpMethod.Post)
            {
                return Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
            }

            entered.TrySetResult();
            if (cancel)
            {
                return pending.Task;
            }

            return Task.FromResult(Response(HttpStatusCode.OK, LoginForm));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var settings = await CliSettings.Load(new());
        settings.Tree.Defaults.Moodle()
            .ConfigureValue(x => x.Credentials = new ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "test",
                    Password = "test",
                },
            });
        var collection = CliRuntime.CreateServices(new(), AppConfiguration.ConfigureServices);
        settings.ConfigureServices(collection);
        await using var provider = AppConfiguration.BuildServiceProvider(collection);
        await using var scope = provider.CreateAsyncScope();
        await using var session = new Session(scope.ServiceProvider);
        var accountLock = new AccountLock();
        var planner = new ConfiguredRegistryGradePlanner((
                services, credentials,
                token) =>
            MoodleScrapingContext.Create(
                services,
                credentials,
                token,
                transport));
        var commands = new Commands(
            session,
            planner,
            accountLock);
        using var cancellation = new CancellationTokenSource();
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var task = commands.ImportGrades(
                new()
                {
                    QuizId = "123",
                },
                new(),
                new()
                {
                    Profile = "test",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                },
                cancellation.Token);

            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(accountLock.Held || !cancel);
            if (cancel)
            {
                cancellation.Cancel();
            }

            Assert.Equal(expected: exit, actual: await task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(accountLock.Held);
            Assert.True(session.Disposed);
            Assert.True(handler.Disposed);
            Assert.Equal(expected: 1, actual: handler.Posts);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(
                expected: exit,
                actual: result.RootElement.GetProperty("exitCode")
                    .GetInt32());
            Assert.Empty(result.RootElement.GetProperty("data")
                .GetProperty("outcomes")
                .EnumerateArray());
            if (cancel)
            {
                Assert.False(pending.Task.IsCompleted);
            }
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
        using var handler = new Handler((request, _) =>
        {
            var status = HttpStatusCode.OK;
            string body;
            if (request.Method == HttpMethod.Post)
            {
                body = "<a href='/login/logout.php?sesskey=test'>Logout</a>";
            }
            else
            {
                body = LoginForm;
            }

            var result = Response(
                status,
                body);
            var fromResult =
                Task.FromResult(
                    result);
            return fromResult;
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var services = new ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        using var context = await MoodleScrapingContext.Create(
            services,
            new()
            {
                Login = "test",
                Password = "test",
            },
            default,
            transport);
        Assert.Equal(expected: 1, actual: handler.Posts);
    }

    private sealed class Commands(Session session, IRegistryGradePlanner planner, AccountLock accountLock)
        : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => session;
        protected override IRegistryGradePlanner CreateGradePlanner() => planner;
        protected override IRegistryAccountLock CreateAccountLock() => accountLock;
    }

    [Fact]
    public async Task MoodleTransportFailureIsNotReportedAsAuthenticationFailure()
    {
        var transportFailure = new HttpRequestException("Moodle unreachable");
        using var log = new DebugLogCapture();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            PlanWithFailingMoodle(transportFailure, log));
        Assert.Same(expected: transportFailure, actual: error);
    }

    [Fact]
    public async Task MoodleLoginRejectionChainsTheCause()
    {
        var rejection = new ScrapingAuthenticationException("login form echoed canary-password");
        using var log = new DebugLogCapture();
        var error = await Assert.ThrowsAsync<RegistryAuthenticationException>(() =>
            PlanWithFailingMoodle(rejection, log));
        Assert.Same(expected: rejection, actual: error.InnerException);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        Assert.Contains(
            expectedSubstring: "RegistryAuthenticationException translated from ScrapingAuthenticationException",
            actualString: log.All);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: log.All);
    }

    private static async Task PlanWithFailingMoodle(Exception failure, DebugLogCapture log)
    {
        using var settings = await CliSettings.Load(new());
        settings.Tree.Defaults.Moodle()
            .ConfigureValue(x => x.Credentials = new ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "test",
                    Password = "test",
                },
            });
        var collection = CliRuntime.CreateServices(new(), AppConfiguration.ConfigureServices);
        settings.ConfigureServices(collection);
        collection.AddLogging(logging => logging.AddProvider(log)
            .SetMinimumLevel(LogLevel.Debug));
        await using var provider = AppConfiguration.BuildServiceProvider(collection);
        await using var scope = provider.CreateAsyncScope();
        await using var session = new Session(scope.ServiceProvider);
        var planner = new ConfiguredRegistryGradePlanner((_, _, _) =>
            Task.FromException<MoodleScrapingContext>(failure));
        await planner.Plan(
            session,
            "123",
            default);
    }

    private sealed class Session(IServiceProvider services) : IConfiguredRegistrySession, IRegistryProvider
    {
        public bool Disposed;
        public IServiceProvider Services => services;

        public RegistryDestination Target { get; } =
            new(Account: "adapter-test", Destination: "https://registry.test/");

        public OnlineRegistryNavigator CreateNavigator(CancellationToken token) =>
            throw new InvalidOperationException("Must fail before registry reads.");

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
            throw new InvalidOperationException();

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AccountLock : IRegistryAccountLock
    {
        public bool Held;

        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
        {
            Assert.False(Held);
            Held = true;
            var result = new Lease(this);
            IAsyncDisposable result2 = result;
            var fromResult =
                Task.FromResult(result2);
            return fromResult;
        }

        private sealed class Lease(AccountLock owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                owner.Held = false;
                return ValueTask.CompletedTask;
            }
        }
    }

    private static HttpResponseMessage Response(HttpStatusCode status, string body)
    {
        return new(status)
        {
            Content = new StringContent(
                body,
                System.Text.Encoding.UTF8,
                "text/html"),
        };
    }

    private sealed class Auth : IAuthHandler
    {
        public int Calls;

        public Task Authenticate(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Posts;
        public bool Disposed;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                Posts++;
            }

            return send(request, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
