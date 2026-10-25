using System.Net;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using Microsoft.Extensions.DependencyInjection;
using QuizModels;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using HttpMethod = System.Net.Http.HttpMethod;

public sealed class RegistryGradePreflightTests
{
    [Theory]
    [InlineData(
        "missing",
        false,
        1)]
    [InlineData(
        "duplicate",
        false,
        1)]
    [InlineData(
        "missing",
        true,
        1)]
    [InlineData(
        "duplicate",
        true,
        1)]
    [InlineData(
        "missing",
        true,
        2)]
    [InlineData(
        "duplicate",
        true,
        2)]
    [InlineData(
        "missing",
        false,
        2)]
    [InlineData(
        "duplicate",
        false,
        2)]
    public async Task PlanningRejectsEveryInvalidFormBeforeAnyWrites(
        string defect,
        bool apply,
        int invalidForm)
    {
        await Run(
            defect,
            apply,
            invalidForm: invalidForm,
            exit: 5,
            posts: 0,
            []);
    }

    [Theory]
    [InlineData(
        "mutate-first",
        5,
        0,
        new[]
        {
            "failed",
            "not-attempted",
        })]
    [InlineData(
        "mutate-later",
        6,
        1,
        new[]
        {
            "completed",
            "failed",
        })]
    [InlineData(
        "lost-first",
        6,
        1,
        new[]
        {
            "uncertain",
            "not-attempted",
        })]
    [InlineData(
        "lost-later",
        6,
        2,
        new[]
        {
            "completed",
            "uncertain",
        })]
    [InlineData(
        "cancel-first",
        130,
        1,
        new[]
        {
            "uncertain",
            "not-attempted",
        })]
    [InlineData(
        "cancel-later",
        130,
        2,
        new[]
        {
            "completed",
            "uncertain",
        })]
    public async Task ProductionPostBoundaryClassifiesFailuresAndRetainsPartialResults(
        string defect,
        int exit,
        int posts,
        string[] statuses)
    {
        await Run(
            defect,
            true,
            invalidForm: 0,
            exit: exit,
            posts: posts,
            statuses);
    }

    private static async Task Run(
        string defect,
        bool apply,
        int invalidForm,
        int exit,
        int posts,
        string[] statuses)
    {
        using var settings = await CliSettings.Load(new() { Profile = "Curmanschii Anton" });
        var collection = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(collection);
        collection.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            global::ScheduleLib.Application.Core.ScheduleBuilderInitializerOptions scheduleBuilderInitializerOptions = x;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
        collection.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        await using var services = AppConfiguration.BuildServiceProvider(collection);
        await services.InitializeSchedule(default);
        await using var scope = services.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons()
            .First(x =>
            schedule.Get(x.Lesson.Groups[0]).QualificationType == QualificationType.Licenta);
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var saved = new HashSet<string>();
        using var handler = new Transport((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            var number = path.EndsWith('1') ? 1 : 2;
            if (path.StartsWith("/evaluation", StringComparison.Ordinal))
            {
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    string html = $"<table><tr><td><a href='https://registry.test/grades{number}'>Testarea 1</a></td></tr></table>";
                    global::System.Net.Http.HttpResponseMessage result = Response(
                    html);
                    return result;
                }

                return Task.FromResult(
                                    CalculateResult());
            }

            if (request.Method == HttpMethod.Post)
            {
                bool CheckCondition()
                {
                    bool CalculateConditionResult()
                    {
                        if (defect == "lost-first")
                        {
                            return true;
                        }
                        else
                        {
                            if (defect == "lost-later")
                            {
                                if (number == 2)
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
                    }

                    bool conditionResult = CalculateConditionResult();


                    if (conditionResult)
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition())
                {
                    throw new IOException("In-flight response lost");
                }

                bool CheckCondition2()
                {
                    bool CalculateConditionResult2()
                    {
                        if (defect == "cancel-first")
                        {
                            return true;
                        }
                        else
                        {
                            if (defect == "cancel-later")
                            {
                                if (number == 2)
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
                    }

                    bool conditionResult2 = CalculateConditionResult2();


                    if (conditionResult2)
                    {
                        return true;
                    }

                    return false;
                }

                if (CheckCondition2())
                {
                    entered.TrySetResult();
                    return pending.Task;
                }
                saved.Add(path);
            }
            var name = number == invalidForm && defect == "missing" ? "" : "name='grade[1]'";
            var extra = number == invalidForm && defect == "duplicate"
                ? "<input type='text' name='grade[1]' value='5'>"
                : "";
            string CalculateText()
            {
                if (number == 1)
                {
                    return "Popescu Ion";
                }

                return "Ionescu Ana";
            }

            return Task.FromResult(
                            Response(
                                $"<form method='post' action='/grades{number}'><table><tr><th>Numele</th><th>Nota</th></tr><tr><td>{(CalculateText())}</td><td><input type='text' {name} value='{(saved.Contains(path) ? "8" : "5")}'></td></tr></table>{extra}</form>"));
        });
        using var client = new HttpClient(handler);
        using var transport = new HttpClientContext(
            new MemoryCookieProvider(),
            client,
            handler);
        using var context = ScrapingContext.Create(transport, new Auth());
        global::ScheduleLib.OnlineRegistry.IRegistryErrorHandler errorHandler = null!;
        global::ScheduleLib.OnlineRegistry.RegistryScrapingContext context2 = new(context);
        global::System.IServiceProvider serviceProvider = scope.ServiceProvider;
        global::System.Threading.CancellationToken cancellationToken = cancellation.Token;
        var adapter = new OnlineRegistryNavigator(
            errorHandler, context2, serviceProvider, cancellationToken);
        var navigator = new Navigator(
            adapter,
            lesson.Lesson.Course,
            lesson.Lesson.Groups,
            lesson.Lesson.GroupPartitionKey);
        var quiz = new QuizAttemptsPage
        {
            Path = new[]
            {
                "Cursuri USM",
                "Matematică și Informatică",
                "Ciclul I Licență",
                "Informatică aplicată",
                $"Anul {schedule.Get(lesson.Lesson.Groups[0]).Grade.Value}",
                schedule.Get(lesson.Lesson.Course).FullName,
                "Atestare 1",
            }.Select(x => (Name: x, Href: (string?) null))
                .ToList(),
            Attempts =
            [
                new()
                {
                    UserName = "Ion Popescu",
                    Grade = 8.5f,
                },
                new()
                {
                    UserName = "Ana Ionescu",
                    Grade = 7.5f,
                },
            ],
        };
        var planner = new Planner(
            scope.ServiceProvider.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>(),
            navigator,
            quiz,
            defect);
        await using var session = new Session();
        var accountLock = new AccountLock();
        var commands = new Commands(
            session,
            planner,
            accountLock);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var task = commands.ImportGrades(
                new() { QuizId = "123" },
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = apply },
                cancellation.Token);
            if (defect.StartsWith("cancel", StringComparison.Ordinal))
            {
                global::System.TimeSpan CalculateTimeout()
                {
                    global::System.TimeSpan timeout = TimeSpan.FromSeconds(5);
                    return timeout;
                }

                await entered.Task.WaitAsync(CalculateTimeout());
                Assert.True(accountLock.Held);
                cancellation.Cancel();
            }
            global::System.TimeSpan CalculateTimeout2()
            {
                global::System.TimeSpan timeout2 = TimeSpan.FromSeconds(5);
                return timeout2;
            }

            Assert.Equal(expected: exit, actual: await task.WaitAsync(CalculateTimeout2()));
            Assert.Equal(expected: posts, actual: handler.Posts);
            Assert.False(accountLock.Held);
            Assert.True(session.Disposed);
            using var result = JsonDocument.Parse(output.ToString());
            var root = result.RootElement;
            Assert.Equal(expected: exit, actual: root.GetProperty("exitCode")
                .GetInt32());
            string CalculateExpected()
            {
                if (exit == 130)
                {
                    return "cancelled";
                }

                if (posts == 0)
                {
                    return "failed";
                }

                return "partial";
            }

            Assert.Equal(
                            expected: CalculateExpected(),
                            actual: root.GetProperty("status")
                                .GetString());
            Assert.Equal(
                expected: statuses,
                actual: root.GetProperty("data")
                    .GetProperty("outcomes")
                    .EnumerateArray()
                    .Select(x => x.GetProperty("status")
                        .GetString()));
            if (invalidForm != 0)
            {
                Assert.Equal(expected: invalidForm, actual: navigator.Documents.Count);
                Assert.All(
                    navigator.Documents.SelectMany(x => x.QuerySelectorAll<IHtmlInputElement>("input")),
                    x => Assert.Equal(expected: "5", actual: x.Value));
            }
        }
        finally
        {
            Console.SetOut(original);
            using var response = Response("<p>Late response</p>");
            pending.TrySetResult(response);
        }
    }

    private sealed class Navigator(
        OnlineRegistryNavigator adapter,
        CourseId course,
        LessonGroups groups,
        GroupPartitionKey partition) : IRegistryGradeNavigator
    {
        public List<IDocument> Documents { get; } = [];

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
        {
            Assert.Equal(expected: Semester.Sem2, actual: semester);
            global::System.Uri CalculateUrl()
            {
                global::System.Uri url = new("https://registry.test/course");
                return url;
            }

            return Task.FromResult<IEnumerable<CourseLink>>([new(course, CalculateUrl())]);
        }

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link)
        {
            return Task.FromResult<IEnumerable<GroupLink>>(
                Enumerable.Range(start: 1, count: 2)
                    .Select(i =>
                {
                    global::System.Uri CalculateUri()
                    {
                        string uriString = $"https://registry.test/group{i}";
                        global::System.Uri uri2 = new(uriString);
                        return uri2;
                    }

                    global::System.Uri CalculateEvaluationUri()
                    {
                        string uriString2 = $"https://registry.test/evaluation{i}";
                        global::System.Uri evaluationUri2 = new(uriString2);
                        return evaluationUri2;
                    }

                    return new GroupLink(
                                                            new FoundGroups
                                                            {
                                                                IsWildcard = false,
                                                                Value = groups,
                                                            },
                                                            partition,
                                                            uri: CalculateUri(),
                                                            evaluationUri: CalculateEvaluationUri());
                }));
        }

        public async Task<IDocument> GetHtml(Uri uri)
        {
            var document = await adapter.GetHtml(uri);
            if (uri.AbsolutePath.StartsWith("/grades", StringComparison.Ordinal))
            {
                Documents.Add(document);
            }

            return document;
        }

        public Task SubmitGrades(
            IDocument document,
            CancellationToken token,
            Action? onSubmissionStarted = null)
        {
            return adapter.SubmitGrades(
            document,
            token,
            onSubmissionStarted);
        }
    }

    private sealed class Planner(
        CopyGradesFromMoodleForTestTaskHandler handler,
        Navigator navigator,
        QuizAttemptsPage quiz,
        string defect) : IRegistryGradePlanner
    {
        public async Task<RegistryGradePlan> Plan(
            IRegistrySession session,
            string quizId,
            CancellationToken token)
        {
            var plan = await handler.Plan(new()
            {
                Quiz = quiz,
                RegistryNavigator = navigator,
                Semester = Semester.Sem2,
                CancellationToken = token,
            });
            Assert.Equal(expected: 2, actual: plan.Actions.Count);
            if (defect.StartsWith("mutate", StringComparison.Ordinal))
            {
                var list = navigator.Documents;
                int index;
                if (defect == "mutate-first")
                {
                    index = 0;
                }
                else
                {
                    index = 1;
                }

                var iDocument = list[index];
                string selectors = "input";
                var querySelector = iDocument.QuerySelector<IHtmlInputElement>(selectors);
                var iHtmlInputElement = querySelector!;
                string name = "name";
                iHtmlInputElement.RemoveAttribute(name);
            }

            return plan;
        }
    }

    private sealed class Commands(Session session, Planner planner, AccountLock accountLock) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => session;
        protected override IRegistryGradePlanner CreateGradePlanner() => planner;
        protected override IRegistryAccountLock CreateAccountLock() => accountLock;
    }

    private sealed class Session : IRegistryProvider, IRegistrySession
    {
        public bool Disposed;

        public RegistryDestination Target { get; } = new(Account: "test-account", Destination: "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
            throw new InvalidOperationException();

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
            Held = true;
            global::RegistryGradePreflightTests.AccountLock.Lease result = new Lease(this);
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

    private sealed class Auth : IAuthHandler
    {
        public Task Authenticate(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Unexpected authentication");
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public int Posts;

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
    }

    private static HttpResponseMessage Response(string html)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                html,
                System.Text.Encoding.UTF8,
                "text/html"),
        };
    }
}
