using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Io;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
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
            global::System.Net.Http.HttpResponseMessage CalculateResult()
            {
                global::System.Net.HttpStatusCode status = HttpStatusCode.OK;
                string body = $"<form method='post' action='/grades'>{inputs}</form>";
                global::System.Net.Http.HttpResponseMessage result = Response(
                status, body);
                return result;
            }

            return Task.FromResult(
                        CalculateResult());
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
        var started = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => navigator.SubmitGrades(
            document,
            default,
            () =>
            {
                started = true;
            }));
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
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, GradeForm);
                    return result;
                }

                return Task.FromResult(CalculateResult());
            }

            posts++;
            // A replay would succeed: the production requester must never send it.
            bool CheckCondition()
            {
                if (!(status == 401))
                {
                    return false;
                }

                if (!(posts > 1))
                {
                    return false;
                }

                return true;
            }

            bool CheckCondition2()
            {
                if (!(status == 401))
                {
                    return false;
                }

                if (!(posts > 1))
                {
                    return false;
                }

                return true;
            }

            global::System.Net.HttpStatusCode status2;
            if (CheckCondition())
            {
                status2 = HttpStatusCode.OK;
            }
            else
            {
                status2 = (HttpStatusCode) status;
            }

            string body2;
            if (CheckCondition2())
            {
                body2 = GradeForm;
            }
            else
            {
                body2 = body;
            }

            global::System.Net.Http.HttpResponseMessage result2 = Response(
            status2, body2);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
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
            global::System.Net.HttpStatusCode status = HttpStatusCode.OK;
            string body;
            if (request.Method == HttpMethod.Post)
            {
                body = GradeForm;
            }
            else
            {
                int int32 = ++reads;
                bool CalculateCondition()
                {
                    if (int32 == 1)
                    {
                        return true;
                    }
                    else
                    {
                        return matches;
                    }
                }

                bool condition = CalculateCondition();


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

            global::System.Net.Http.HttpResponseMessage result = Response(
            status, body);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
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
                int int32 = ++reads;
                condition = int32 == 1;
            }

            global::System.Net.Http.HttpResponseMessage result;
            if (condition)
            {
                global::System.Net.Http.HttpResponseMessage response = Response(HttpStatusCode.OK, GradeForm);
                result = response;
            }
            else
            {
                global::System.Net.HttpStatusCode status2 = (HttpStatusCode) status;
                string body2 = body;
                global::System.Net.Http.HttpResponseMessage response2 = Response(status2, body2);
                result = response2;
            }

            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
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
            bool conditionResult;
            int conditionValue = ++reads;
            conditionResult = conditionValue == 1;
            if (conditionResult)
            {
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, GradeForm);
                    return result;
                }

                return Task.FromResult(CalculateResult());
            }
            // Legacy read authentication may retry the GET, but must never replay the POST.
            bool CalculateCondition()
            {
                if (readStatus == 401)
                {
                    if (reads > 2)
                    {
                        return true;
                    }

                    return false;
                }
                else
                {
                    return false;
                }
            }

            bool condition = CalculateCondition();


            global::System.Net.Http.HttpResponseMessage result2;
            if (condition)
            {
                global::System.Net.Http.HttpResponseMessage response = Response(HttpStatusCode.OK, LoginForm);
                result2 = response;
            }
            else
            {
                global::System.Net.HttpStatusCode status = (HttpStatusCode) readStatus;
                string body2 = body;
                global::System.Net.Http.HttpResponseMessage response2 = Response(status, body2);
                result2 = response2;
            }

            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());

        await Assert.ThrowsAsync<IOException>(() => navigator.SubmitGrades(
            document,
            default,
            () =>
            {
                started = true;
            }));

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
                int int32 = ++reads;
                condition = int32 == 2;
            }

            global::System.Net.HttpStatusCode status;
            if (condition)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            global::System.Net.Http.HttpResponseMessage result = Response(
            status, body);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
        await Assert.ThrowsAsync<RegistrySubmissionRejectedException>(() => navigator.SubmitGrades(document, default));
        Assert.Equal(expected: 0, actual: auth.Calls);
        global::System.Uri CalculateUri2()
        {
            global::System.Uri uri2 = new("https://registry.test/grades");
            return uri2;
        }

        await navigator.GetHtml(CalculateUri2());
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
                global::System.Net.Http.Headers.HttpResponseHeaders httpResponseHeaders = redirect.Headers;
                string uriString;
                if (login)
                {
                    uriString = "/login";
                }
                else
                {
                    uriString = "/evaluation";
                }

                global::System.UriKind uriKind = UriKind.Relative;
                global::System.Uri uri = new(uriString, uriKind);
                httpResponseHeaders.Location = uri;
                return Task.FromResult(redirect);
            }
            reads++;
            global::System.Net.Http.HttpResponseMessage CalculateResult()
            {
                global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, GradeForm);
                return result;
            }

            return Task.FromResult(CalculateResult());
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        using var services = new ServiceCollection().BuildServiceProvider();
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());
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
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, GradeForm);
                    return result;
                }

                return Task.FromResult(CalculateResult());
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
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = services;
        global::System.Threading.CancellationToken cancellationToken = default;
        var navigator = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        global::System.Uri CalculateUri()
        {
            global::System.Uri uri = new("https://registry.test/grades");
            return uri;
        }

        var document = await navigator.GetHtml(CalculateUri());

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
            int int32 = ++reads;
            global::System.Net.HttpStatusCode status;
            if (int32 == 1)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            global::System.Net.Http.HttpResponseMessage result = Response(status, body);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(result);
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
                int int32 = ++posts;
                condition = int32 == 1;
            }
            else
            {
                condition = false;
            }

            global::System.Net.HttpStatusCode status;
            if (condition)
            {
                status = HttpStatusCode.Unauthorized;
            }
            else
            {
                status = HttpStatusCode.OK;
            }

            string body = GradeForm;
            global::System.Net.Http.HttpResponseMessage result = Response(
            status, body);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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
            global::System.Net.HttpStatusCode status = HttpStatusCode.OK;
            string body2;
            if (request.Method == HttpMethod.Post)
            {
                body2 = body;
            }
            else
            {
                body2 = LoginForm;
            }

            global::System.Net.Http.HttpResponseMessage result = Response(status, body2);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(result);
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => MoodleScrapingContext.Create(
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
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, LoginForm);
                    return result;
                }

                return Task.FromResult(CalculateResult());
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
        global::System.TimeSpan CalculateTimeout()
        {
            global::System.TimeSpan timeout = TimeSpan.FromSeconds(5);
            return timeout;
        }

        await entered.Task.WaitAsync(CalculateTimeout());
        cancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            {
                global::System.TimeSpan CalculateTimeout()
                {
                    global::System.TimeSpan timeout = TimeSpan.FromSeconds(5);
                    return timeout;
                }

                return task.WaitAsync(CalculateTimeout());
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
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Response(HttpStatusCode.OK, LoginForm);
                    return result;
                }

                return Task.FromResult(CalculateResult());
            }

            entered.TrySetResult();
            if (cancel)
            {
                return pending.Task;
            }

            global::System.Net.Http.HttpResponseMessage CalculateResult2()
            {
                global::System.Net.Http.HttpResponseMessage result2 = Response(HttpStatusCode.OK, LoginForm);
                return result2;
            }

            return Task.FromResult(CalculateResult2());
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
                new() { QuizId = "123" },
                new(),
                new() { Profile = "test" },
                new() { Json = true },
                new() { Apply = true },
                cancellation.Token);
            global::System.TimeSpan CalculateTimeout()
            {
                global::System.TimeSpan timeout = TimeSpan.FromSeconds(5);
                return timeout;
            }

            await entered.Task.WaitAsync(CalculateTimeout());
            bool conditionResult;
            if (accountLock.Held)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = cancel;
                conditionResult = !(conditionResult);
            }

            Assert.True(conditionResult);
            if (cancel)
            {
                cancellation.Cancel();
            }

            global::System.TimeSpan CalculateTimeout2()
            {
                global::System.TimeSpan timeout2 = TimeSpan.FromSeconds(5);
                return timeout2;
            }

            Assert.Equal(expected: exit, actual: await task.WaitAsync(CalculateTimeout2()));
            Assert.False(accountLock.Held);
            Assert.True(session.Disposed);
            Assert.True(handler.Disposed);
            Assert.Equal(expected: 1, actual: handler.Posts);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: exit, actual: result.RootElement.GetProperty("exitCode")
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
            global::System.Net.HttpStatusCode status = HttpStatusCode.OK;
            string body;
            if (request.Method == HttpMethod.Post)
            {
                body = "<a href='/login/logout.php?sesskey=test'>Logout</a>";
            }
            else
            {
                body = LoginForm;
            }

            global::System.Net.Http.HttpResponseMessage result = Response(
            status, body);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
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

    private sealed class Session(IServiceProvider services) : IConfiguredRegistrySession, IRegistryProvider
    {
        public bool Disposed;
        public IServiceProvider Services => services;

        public RegistryDestination Target { get; } = new(Account: "adapter-test", Destination: "https://registry.test/");

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
            global::RegistryGradeAdapterTests.AccountLock.Lease result = new Lease(this);
            global::System.IAsyncDisposable result2 = result;
            global::System.Threading.Tasks.Task<global::System.IAsyncDisposable> fromResult = Task.FromResult<IAsyncDisposable>(result2);
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
