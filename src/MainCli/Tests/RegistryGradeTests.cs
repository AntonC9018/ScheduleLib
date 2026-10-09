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
using Xunit;

public sealed class RegistryGradeTests
{
    [Theory]
    [InlineData(new[] { "registry", "import-grades", "--help" }, 0)]
    [InlineData(new[] { "registry", "import-grades", "--json" }, 2)]
    [InlineData(new[] { "registry", "import-grades", "--quiz-id", "0", "--json" }, 2)]
    [InlineData(new[] { "registry", "import-grades", "--quiz-id", "1&x=2", "--json" }, 2)]
    [InlineData(new[] { "registry", "import-grades", "--quiz-id", "1", "--output", "/tmp/unused" }, 2)]
    public async Task RoutingValidatesWithoutProviders(string[] args, int exit) => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task PreviewAndApplyReadFreshPlansUnderActualAccountLock()
    {
        await using var fake = new Fake();
        var commands = new TestCommands(fake);
        using (var capture = new Capture())
        {
            Assert.Equal(0, await commands.ImportGrades(new() { QuizId = "123" }, new(), new() { Profile = "first-alias" }, new() { Json = true }, new()));
            Assert.Equal("preview", capture.Result().GetProperty("status").GetString());
            Assert.Equal(0, fake.Submissions);
            Assert.False(fake.Held);
            Assert.Single(capture.Result().GetProperty("data").GetProperty("notices").EnumerateArray());
        }
        using (var capture = new Capture())
        {
            Assert.Equal(0, await commands.ImportGrades(new() { QuizId = "123" }, new(), new() { Profile = "other-alias" }, new() { Json = true }, new() { Apply = true }));
            Assert.Equal(2, fake.Plans);
            Assert.Equal(3, fake.Submissions);
            Assert.True(fake.PlannedUnderLock);
            Assert.Equal(fake.Target, fake.LockTarget);
            Assert.Equal(2, capture.Result().GetProperty("data").GetProperty("outcomes")[0].GetProperty("grades")[0].GetProperty("roundedGrade").GetInt32());
            Assert.All(capture.Result().GetProperty("data").GetProperty("outcomes").EnumerateArray(), x => Assert.Equal("completed", x.GetProperty("status").GetString()));
            Assert.True(fake.Disposed);
            Assert.False(fake.Held);
        }
    }

    [Theory]
    [InlineData(false, false, 5, "failed")]
    [InlineData(true, false, 6, "uncertain")]
    [InlineData(true, true, 6, "failed")]
    public async Task FailedFormStopsWithoutRetry(bool started, bool rejected, int exit, string status)
    {
        await using var fake = new Fake { FailAt = 1, Failure = new RegistryActionExecutionException(started,
            rejected ? new RegistrySubmissionRejectedException("rejected") : new IOException("lost response")) };
        using var capture = new Capture();
        Assert.Equal(exit, await new TestCommands(fake).ImportGrades(new() { QuizId = "1" }, new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal(1, fake.Executions);
        Assert.Equal(started ? 1 : 0, fake.Submissions);
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal(status, outcomes[0].GetProperty("status").GetString());
        Assert.Equal("not-attempted", outcomes[1].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false, "cancelled")]
    [InlineData(true, "uncertain")]
    public async Task CancellationRetainsCompletedAndUnattemptedForms(bool started, string status)
    {
        await using var fake = new Fake { FailAt = 2, Failure = new RegistryActionCancelledException(started, new OperationCanceledException()) };
        using var capture = new Capture();
        Assert.Equal(130, await new TestCommands(fake).ImportGrades(new() { QuizId = "1" }, new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal("completed", outcomes[0].GetProperty("status").GetString());
        Assert.Equal(status, outcomes[1].GetProperty("status").GetString());
        Assert.Equal("not-attempted", outcomes[2].GetProperty("status").GetString());
        Assert.Equal(2, fake.Executions);
        Assert.Equal(started ? 2 : 1, fake.Submissions);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task BeforeSubmissionFailuresHaveDocumentedExits(int exit)
    {
        await using var fake = new Fake { PlanFailure = exit == 1 ? new Exception("unexpected") : exit == 4 ? new RegistryAuthenticationException("Moodle credentials missing") : new IOException("read failed"), Busy = exit == 7 };
        using var capture = new Capture();
        Assert.Equal(exit, await new TestCommands(fake).ImportGrades(new() { QuizId = "1" }, new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal(0, fake.Submissions);
        if (exit == 7) Assert.Equal(0, fake.Plans);
        Assert.Equal("failed", capture.Result().GetProperty("status").GetString());
    }

    [Fact]
    public async Task DisposalFailureEmitsSinglePartialResult()
    {
        await using var fake = new Fake { DisposeFailure = true };
        using var capture = new Capture();
        Assert.Equal(6, await new TestCommands(fake).ImportGrades(new() { QuizId = "1" }, new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal("partial", capture.Result().GetProperty("status").GetString());
        Assert.Equal(3, fake.Submissions);
    }

    [Fact]
    public async Task RealPlannerMapsAndRoundsWithoutChangingOrSubmittingForm()
    {
        using var settings = await CliSettings.Load(new() { Profile = "Curmanschii Anton" });
        var services = CliRuntime.CreateServices(new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") }, AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
        services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons().First(x => schedule.Get(x.Lesson.Groups[0]).QualificationType == QualificationType.Licenta);
        var groupInfo = schedule.Get(lesson.Lesson.Groups[0]);
        var navigator = new FixtureNavigator(lesson.Lesson.Course, new GroupLink(new FoundGroups
        {
            IsWildcard = false, Value = lesson.Lesson.Groups,
        }, lesson.Lesson.GroupPartitionKey, new("https://registry.test/group"), new("https://registry.test/evaluation")));
        var quiz = new QuizAttemptsPage
        {
            Path = new[] { "Cursuri USM", "Matematică și Informatică", "Ciclul I Licență", "Informatică aplicată", $"Anul {groupInfo.Grade.Value}", schedule.Get(lesson.Lesson.Course).FullName, "Atestare 1" }.Select(x => (Name: x, Href: (string?) null)).ToList(),
            Attempts = [new() { UserName = "Ion Popescu", Grade = 6.5f }, new() { UserName = "Ana Ionescu", Grade = 7.5f },
                new() { UserName = "Ion Popescu", Grade = 8.5f }, new() { UserName = "Pavel Rusu", Grade = null },
                new() { UserName = "Mihai Munteanu", Grade = 9 }, new() { UserName = "Alexandru Avram", Grade = float.NaN },
                new() { UserName = "Vasile Vasile", Grade = 10 }],
        };
        var handler = scope.ServiceProvider.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>();
        var plan = await handler.Plan(new() { Quiz = quiz, RegistryNavigator = navigator, Semester = Semester.Sem2, CancellationToken = default });
        var action = Assert.Single(plan.Actions);
        Assert.Equal(new[] { 8, 8 }, action.Grades.Select(x => x.RoundedGrade));
        Assert.Equal(8.5f, action.Grades[0].SourceGrade);
        Assert.All(action.Grades, x => Assert.Equal("5", x.PreviousGrade));
        Assert.Equal(0, navigator.Submissions);
        Assert.All(navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input"), x => Assert.Equal("5", x.Value));
        Assert.Contains(plan.Notices, x => x.Kind == "omitted" && x.Detail.Contains("Earlier attempt"));
        Assert.Contains(plan.Notices, x => x.Kind == "omitted" && x.Detail.Contains("no grade"));
        Assert.Contains(plan.Notices, x => x.Kind == "omitted" && x.Detail.Contains("Expelled"));
        Assert.Contains(plan.Notices, x => x.Kind == "unmatched" && x.Detail.Contains("Registry student"));
        Assert.Contains(plan.Notices, x => x.Kind == "unmatched" && x.Detail.Contains("Moodle student"));
        Assert.Contains(plan.Notices, x => x.Kind == "unsupported");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            var error = await Assert.ThrowsAsync<RegistryActionCancelledException>(() => action.Execute(cancelled.Token));
            Assert.False(error.SubmissionStarted);
            Assert.Equal(0, navigator.Submissions);
            Assert.All(navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input"), x => Assert.Equal("5", x.Value));
        }
        await action.Execute(default);
        Assert.Equal(1, navigator.Submissions);
        Assert.Equal(new[] { "8", "8", "5", "5" }, navigator.Form!.QuerySelectorAll<IHtmlInputElement>("input").Select(x => x.Value));

        quiz.Attempts.Clear();
        var empty = await handler.Plan(new() { Quiz = quiz, RegistryNavigator = navigator, Semester = Semester.Sem2, CancellationToken = default });
        Assert.Empty(empty.Actions);
        Assert.Contains(empty.Notices, x => x.Kind == "omitted" && x.Detail.Contains("No mapped grades"));
        Assert.Equal(1, navigator.Submissions);

        quiz.Path.Clear();
        var unsupported = await handler.Plan(new() { Quiz = quiz, RegistryNavigator = navigator, Semester = Semester.Sem2, CancellationToken = default });
        Assert.Empty(unsupported.Actions);
        Assert.Contains(unsupported.Notices, x => x.Kind == "unsupported");
        Assert.Equal(1, navigator.Submissions);
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
        public RegistryDestination Target { get; } = new("actual-account", "https://registry.test/");
        public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token) { Disposed = false; return Task.FromResult<IRegistrySession>(this); }
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) => throw new InvalidOperationException("Wrong handler");
        public Task<RegistryGradePlan> Plan(IRegistrySession session, string quizId, CancellationToken token)
        {
            Assert.Same(this, session);
            Plans++;
            PlannedUnderLock = Held;
            if (PlanFailure is not null) throw PlanFailure;
            RegistryGradeAction[] actions = Enumerable.Range(1, 3).Select(i => new RegistryGradeAction($"https://registry.test/test/{i}", "course", "group", 1,
                [new("Student", Plans, Plans, "5")], ct =>
                {
                    Assert.True(Held);
                    Executions++;
                    var failure = Executions == FailAt ? Failure : null;
                    var beforeSubmit = failure is RegistryActionExecutionException { SubmissionStarted: false }
                        or RegistryActionCancelledException { SubmissionStarted: false };
                    if (!beforeSubmit) Submissions++;
                    if (failure is not null) throw failure;
                    return Task.CompletedTask;
                })).ToArray();
            return Task.FromResult(new RegistryGradePlan(actions, [new("omitted", "Ungraded student")]));
        }
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
        {
            if (Busy) throw new LocalOperationBusyException("registry");
            LockTarget = target; Held = true;
            return Task.FromResult<IAsyncDisposable>(new Lease(this));
        }
        private sealed class Lease(Fake fake) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { fake.Held = false; return ValueTask.CompletedTask; }
        }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            if (DisposeFailure) { DisposeFailure = false; throw new IOException("dispose failed"); }
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistryGradeNavigator
    {
        public int Submissions;
        public IDocument? Form;
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) { Assert.Equal(Semester.Sem2, semester); return Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]); }
        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) => Task.FromResult<IEnumerable<GroupLink>>([group]);
        public async Task<IDocument> GetHtml(Uri uri)
        {
            if (uri.AbsolutePath == "/evaluation") return await new HtmlParser().ParseDocumentAsync("<table><tr><td><a href='https://registry.test/test'>Testarea 1</a></td></tr></table>");
            Form = await new HtmlParser().ParseDocumentAsync("<form><table><tr><th>Numele</th><th>Nota</th></tr><tr><td>Popescu Ion</td><td><input type='text' name='grade[1]' value='5'></td></tr><tr><td>Ionescu Ana</td><td><input type='text' name='grade[2]' value='5'></td></tr><tr><td>Rusu Pavel</td><td><input type='text' name='grade[3]' value='5'></td></tr><tr><td>Munteanu Mihai exmatr</td><td><input type='text' name='grade[4]' value='5'></td></tr></table></form>");
            return Form;
        }
        public Task SubmitGrades(IDocument document, CancellationToken token, Action? onSubmissionStarted = null) { onSubmissionStarted?.Invoke(); Submissions++; Assert.Same(Form, document); return Task.CompletedTask; }
    }
    private sealed class Capture : IDisposable
    {
        private readonly TextWriter _original = Console.Out;
        private readonly StringWriter _text = new();
        public Capture() => Console.SetOut(_text);
        public JsonElement Result() { using var doc = JsonDocument.Parse(_text.ToString()); return doc.RootElement.Clone(); }
        public void Dispose() { Console.SetOut(_original); _text.Dispose(); }
    }
}
