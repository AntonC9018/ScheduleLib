using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using QuizModels;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;

public sealed class RegistryGradeTests
{
    [Theory]
    [InlineData(
        new[]
        {
            "registry",
            "import-grades",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "registry",
            "import-grades",
            "--json",
        },
        2)]
    [InlineData(
        new[]
        {
            "registry",
            "import-grades",
            "--quiz-id",
            "0",
            "--json",
        },
        2)]
    [InlineData(
        new[]
        {
            "registry",
            "import-grades",
            "--quiz-id",
            "1&x=2",
            "--json",
        },
        2)]
    [InlineData(
        new[]
        {
            "registry",
            "import-grades",
            "--quiz-id",
            "1",
            "--output",
            "/tmp/unused",
        },
        2)]
    public async Task RoutingValidatesWithoutProviders(string[] args, int exit) =>
        Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Fact]
    public async Task PreviewAndApplyReadFreshPlansUnderActualAccountLock()
    {
        await using var fake = new Fake();
        var commands = new TestCommands(fake);
        using (var capture = new Capture())
        {
            Assert.Equal(
                expected: 0,
                actual: await commands.ImportGrades(
                    new() { QuizId = "123" },
                    new(),
                    new() { Profile = "first-alias" },
                    new() { Json = true },
                    new()));
            Assert.Equal(expected: "preview", actual: capture.Result().GetProperty("status").GetString());
            Assert.Equal(expected: 0, actual: fake.Submissions);
            Assert.False(fake.Held);
            Assert.Single(capture.Result().GetProperty("data").GetProperty("notices").EnumerateArray());
        }
        using (var capture = new Capture())
        {
            Assert.Equal(
                expected: 0,
                actual: await commands.ImportGrades(
                    new() { QuizId = "123" },
                    new(),
                    new() { Profile = "other-alias" },
                    new() { Json = true },
                    new() { Apply = true }));
            Assert.Equal(expected: 2, actual: fake.Plans);
            Assert.Equal(expected: 3, actual: fake.Submissions);
            Assert.True(fake.PlannedUnderLock);
            Assert.Equal(expected: fake.Target, actual: fake.LockTarget);
            Assert.Equal(
                expected: 2,
                actual: capture.Result().GetProperty("data").GetProperty("outcomes")[0].GetProperty("grades")[0]
                    .GetProperty("roundedGrade").GetInt32());
            Assert.All(
                capture.Result().GetProperty("data").GetProperty("outcomes").EnumerateArray(),
                x => Assert.Equal(expected: "completed", actual: x.GetProperty("status").GetString()));
            Assert.True(fake.Disposed);
            Assert.False(fake.Held);
        }
    }

    [Theory]
    [InlineData(
        false,
        false,
        5,
        "failed")]
    [InlineData(
        true,
        false,
        6,
        "uncertain")]
    [InlineData(
        true,
        true,
        6,
        "failed")]
    public async Task FailedFormStopsWithoutRetry(
        bool started,
        bool rejected,
        int exit,
        string status)
    {
        await using var fake = new Fake
        {
            FailAt = 1,
            Failure = new RegistryActionExecutionException(
                started,
                rejected ? new RegistrySubmissionRejectedException("rejected") : new IOException("lost response")),
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: exit,
            actual: await new TestCommands(fake).ImportGrades(
                new() { QuizId = "1" },
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal(expected: 1, actual: fake.Executions);
        Assert.Equal(expected: started ? 1 : 0, actual: fake.Submissions);
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal(expected: status, actual: outcomes[0].GetProperty("status").GetString());
        Assert.Equal(expected: "not-attempted", actual: outcomes[1].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false, "cancelled")]
    [InlineData(true, "uncertain")]
    public async Task CancellationRetainsCompletedAndUnattemptedForms(bool started, string status)
    {
        await using var fake = new Fake
        {
            FailAt = 2,
            Failure = new RegistryActionCancelledException(started, new OperationCanceledException()),
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: 130,
            actual: await new TestCommands(fake).ImportGrades(
                new() { QuizId = "1" },
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal(expected: "completed", actual: outcomes[0].GetProperty("status").GetString());
        Assert.Equal(expected: status, actual: outcomes[1].GetProperty("status").GetString());
        Assert.Equal(expected: "not-attempted", actual: outcomes[2].GetProperty("status").GetString());
        Assert.Equal(expected: 2, actual: fake.Executions);
        Assert.Equal(expected: started ? 2 : 1, actual: fake.Submissions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task BeforeSubmissionFailuresHaveDocumentedExits(int exit)
    {
        await using var fake = new Fake
        {
            PlanFailure =
                exit == 1 ? new Exception("unexpected") :
                exit == 4 ? new RegistryAuthenticationException("Moodle credentials missing") :
                new IOException("read failed"),
            Busy = exit == 7,
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: exit,
            actual: await new TestCommands(fake).ImportGrades(
                new() { QuizId = "1" },
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal(expected: 0, actual: fake.Submissions);
        if (exit == 7)
        {
            Assert.Equal(expected: 0, actual: fake.Plans);
        }

        Assert.Equal(expected: "failed", actual: capture.Result().GetProperty("status").GetString());
    }

    [Fact]
    public async Task DisposalFailureEmitsSinglePartialResult()
    {
        await using var fake = new Fake { DisposeFailure = true };
        using var capture = new Capture();
        Assert.Equal(
            expected: 6,
            actual: await new TestCommands(fake).ImportGrades(
                new() { QuizId = "1" },
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal(expected: "partial", actual: capture.Result().GetProperty("status").GetString());
        Assert.Equal(expected: 3, actual: fake.Submissions);
    }

    [Fact]
    public async Task RealPlannerMapsAndRoundsWithoutChangingOrSubmittingForm()
    {
        using var settings = await CliSettings.Load(new() { Profile = "Curmanschii Anton" });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons().First(x =>
            schedule.Get(x.Lesson.Groups[0]).QualificationType == QualificationType.Licenta);
        var groupInfo = schedule.Get(lesson.Lesson.Groups[0]);
        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = false,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: new("https://registry.test/group"),
                evaluationUri: new("https://registry.test/evaluation")));
        var quiz = new QuizAttemptsPage
        {
            Path = new[]
            {
                "Cursuri USM",
                "Matematică și Informatică",
                "Ciclul I Licență",
                "Informatică aplicată",
                $"Anul {groupInfo.Grade.Value}",
                schedule.Get(lesson.Lesson.Course).FullName,
                "Atestare 1",
            }.Select(x => (Name: x, Href: (string?) null)).ToList(),
            Attempts =
            [
                new()
                {
                    UserName = "Ion Popescu",
                    Grade = 6.5f,
                },
                new()
                {
                    UserName = "Ana Ionescu",
                    Grade = 7.5f,
                },
                new()
                {
                    UserName = "Ion Popescu",
                    Grade = 8.5f,
                },
                new()
                {
                    UserName = "Pavel Rusu",
                    Grade = null,
                },
                new()
                {
                    UserName = "Mihai Munteanu",
                    Grade = 9,
                },
                new()
                {
                    UserName = "Alexandru Avram",
                    Grade = float.NaN,
                },
                new()
                {
                    UserName = "Vasile Vasile",
                    Grade = 10,
                },
            ],
        };
        var handler = scope.ServiceProvider.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>();
        var plan = await handler.Plan(new()
        {
            Quiz = quiz,
            RegistryNavigator = navigator,
            Semester = Semester.Sem2,
            CancellationToken = default,
        });
        var action = Assert.Single(plan.Actions);
        Assert.Equal(
            expected: new[]
            {
                8,
                8,
            },
            actual: action.Grades.Select(x => x.RoundedGrade));
        Assert.Equal(expected: 8.5f, actual: action.Grades[0].SourceGrade);
        Assert.All(action.Grades, x => Assert.Equal(expected: "5", actual: x.PreviousGrade));
        Assert.Equal(expected: 0, actual: navigator.Submissions);
        Assert.All(navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input"), x => Assert.Equal(expected: "5", actual: x.Value));
        Assert.Contains(plan.Notices, x =>
        {
            bool CheckCondition()
            {
                if (!(x.Kind == "omitted"))
                {
                    return false;
                }

                if (!(x.Detail.Contains("Earlier attempt")))
                {
                    return false;
                }

                return true;
            }

            return CheckCondition();
        });
        Assert.Contains(plan.Notices, x => x.Kind == "omitted" && x.Detail.Contains("no grade"));
        Assert.Contains(plan.Notices, x => x.Kind == "omitted" && x.Detail.Contains("Expelled"));
        Assert.Contains(plan.Notices, x => x.Kind == "unmatched" && x.Detail.Contains("Registry student"));
        Assert.Contains(plan.Notices, x => x.Kind == "unmatched" && x.Detail.Contains("Moodle student"));
        Assert.Contains(plan.Notices, x => x.Kind == "unsupported");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var error = await Assert.ThrowsAsync<RegistryActionCancelledException>(() =>
                action.Execute(cancelled.Token));
            Assert.False(error.SubmissionStarted);
            Assert.Equal(expected: 0, actual: navigator.Submissions);
            Assert.All(navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input"), x => Assert.Equal(expected: "5", actual: x.Value));
        }
        await action.Execute(default);
        Assert.Equal(expected: 1, actual: navigator.Submissions);
        Assert.Equal(
            expected: new[]
            {
                "8",
                "8",
                "5",
                "5",
            },
            actual: navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input").Select(x => x.Value));

        quiz.Attempts.Clear();
        var empty = await handler.Plan(new()
        {
            Quiz = quiz,
            RegistryNavigator = navigator,
            Semester = Semester.Sem2,
            CancellationToken = default,
        });
        Assert.Empty(empty.Actions);
        Assert.Contains(empty.Notices, x => x.Kind == "omitted" && x.Detail.Contains("No mapped grades"));
        Assert.Equal(expected: 1, actual: navigator.Submissions);

        quiz.Path.Clear();
        var unsupported = await handler.Plan(new()
        {
            Quiz = quiz,
            RegistryNavigator = navigator,
            Semester = Semester.Sem2,
            CancellationToken = default,
        });
        Assert.Empty(unsupported.Actions);
        Assert.Contains(unsupported.Notices, x => x.Kind == "unsupported");
        Assert.Equal(expected: 1, actual: navigator.Submissions);
    }

    private sealed class TestCommands(Fake fake) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => fake;
        protected override IRegistryGradePlanner CreateGradePlanner() => fake;
        protected override IRegistryAccountLock CreateAccountLock() => fake;
    }

    private sealed class Fake : IRegistryProvider, IRegistrySession, IRegistryGradePlanner, IRegistryAccountLock
    {
        public int Plans;
        public int Submissions;
        public int Executions;
        public int FailAt;
        public Exception? Failure;
        public Exception? PlanFailure;
        public bool Held;
        public bool PlannedUnderLock;
        public bool Disposed;
        public bool DisposeFailure;
        public bool Busy;
        public RegistryDestination? LockTarget;

        public RegistryDestination Target { get; } = new(Account: "actual-account", Destination: "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token)
        {
            Disposed = false;
            return Task.FromResult<IRegistrySession>(this);
        }

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
            throw new InvalidOperationException("Wrong handler");

        public Task<RegistryGradePlan> Plan(
            IRegistrySession session,
            string quizId,
            CancellationToken token)
        {
            Assert.Same(expected: this, actual: session);
            Plans++;
            PlannedUnderLock = Held;
            if (PlanFailure is not null)
            {
                throw PlanFailure;
            }

            RegistryGradeAction[] actions = Enumerable.Range(start: 1, count: 3).Select(i => new RegistryGradeAction(
                destination: $"https://registry.test/test/{i}",
                course: "course",
                groups: "group",
                1,
                [
                    new(
                        Student: "Student",
                        Plans,
                        Plans,
                        PreviousGrade: "5"),
                ],
                ct =>
                {
                    Assert.True(Held);
                    Executions++;
                    var failure = Executions == FailAt ? Failure : null;
                    var beforeSubmit = failure is RegistryActionExecutionException { SubmissionStarted: false }
                        or RegistryActionCancelledException { SubmissionStarted: false };
                    if (!beforeSubmit)
                    {
                        Submissions++;
                    }

                    if (failure is not null)
                    {
                        throw failure;
                    }

                    return Task.CompletedTask;
                })).ToArray();
            return Task.FromResult(new RegistryGradePlan(actions, [new(Kind: "omitted", Detail: "Ungraded student")]));
        }

        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
        {
            if (Busy)
            {
                throw new LocalOperationBusyException("registry");
            }

            LockTarget = target;
            Held = true;
            return Task.FromResult<IAsyncDisposable>(new Lease(this));
        }

        private sealed class Lease(Fake fake) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                fake.Held = false;
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (DisposeFailure)
            {
                DisposeFailure = false;
                throw new IOException("dispose failed");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistryGradeNavigator
    {
        public int Submissions;
        public IDocument? Form;

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
        {
            Assert.Equal(expected: Semester.Sem2, actual: semester);
            return Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);
        }

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) =>
            Task.FromResult<IEnumerable<GroupLink>>([group]);

        public async Task<IDocument> GetHtml(Uri uri)
        {
            if (uri.AbsolutePath == "/evaluation")
            {
                return await new HtmlParser().ParseDocumentAsync(
                    "<table><tr><td><a href='https://registry.test/test'>Testarea 1</a></td></tr></table>");
            }

            Form = await new HtmlParser().ParseDocumentAsync(
                "<form><table><tr><th>Numele</th><th>Nota</th></tr><tr><td>Popescu Ion</td><td><input type='text' name='grade[1]' value='5'></td></tr><tr><td>Ionescu Ana</td><td><input type='text' name='grade[2]' value='5'></td></tr><tr><td>Rusu Pavel</td><td><input type='text' name='grade[3]' value='5'></td></tr><tr><td>Munteanu Mihai exmatr</td><td><input type='text' name='grade[4]' value='5'></td></tr></table></form>");
            return Form;
        }

        public Task SubmitGrades(
            IDocument document,
            CancellationToken token,
            Action? onSubmissionStarted = null)
        {
            onSubmissionStarted?.Invoke();
            Submissions++;
            Assert.Same(expected: Form, actual: document);
            return Task.CompletedTask;
        }
    }

    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _text = new();
        public Capture() => Console.SetOut(_text);

        public JsonElement Result()
        {
            using var doc = JsonDocument.Parse(_text.ToString());
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            Console.SetOut(_original);
            _text.Dispose();
        }
    }
}
