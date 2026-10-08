using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using Xunit;

public sealed class RegistryTests
{
    [Theory]
    [InlineData(new[] { "registry", "sync", "--help" }, 0)]
    [InlineData(new[] { "registry", "sync", "--quiz-id", "1" }, 2)]
    [InlineData(new[] { "registry", "sync", "--output", "/tmp/unused" }, 2)]
    public async Task RoutingRejectsIrrelevantArguments(string[] args, int exit) => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task RealReconciliationPlanDoesNotSubmitThroughFakeNavigator()
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
        var lesson = schedule.EnumerateWeeklyLessons().First();
        var navigator = new FixtureNavigator(lesson.Lesson.Course, new GroupLink(new FoundGroups
        {
            IsWildcard = true, Value = lesson.Lesson.Groups,
        }, lesson.Lesson.GroupPartitionKey, new("https://registry.test/group"), new("https://registry.test/evaluation")));
        var plan = await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().Plan(new()
        {
            Navigator = navigator, Attendance = new([]), LessonTopics = NoLessonTopics.Instance, Semester = Semester.Sem2,
        }, explicitApply: true);
        Assert.NotEmpty(plan);
        Assert.Contains(plan, action => action.Kind == "create");
        Assert.Equal(0, navigator.Submissions);
    }

    [Fact]
    public async Task PreviewMakesZeroNavigatorWritesAndReportsAllKinds()
    {
        await using var fake = new FakeProvider();
        using var capture = new Capture();
        Assert.Equal(0, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher-alias" }, new() { Json = true }, new()));
        Assert.Equal(0, fake.Navigator.Submissions);
        Assert.Equal(1, fake.Plans);
        var result = capture.Result();
        Assert.Equal("preview", result.GetProperty("status").GetString());
        Assert.Equal("actual-account", result.GetProperty("data").GetProperty("target").GetProperty("account").GetString());
        Assert.Equal(3, result.GetProperty("data").GetProperty("outcomes").GetArrayLength());
    }

    [Fact]
    public async Task ApplyRecomputesAndRecordsCompletedActions()
    {
        await using var fake = new FakeProvider();
        await using var locks = new FakeLock();
        var commands = new TestCommands(fake, locks);
        Assert.Equal(0, await commands.Sync(new(), new() { Profile = "first-alias" }, new(), new()));
        using var capture = new Capture();
        Assert.Equal(0, await commands.Sync(new(), new() { Profile = "other-alias" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal(2, fake.Plans);
        Assert.Equal(3, fake.Navigator.Submissions);
        Assert.Equal(fake.Target, locks.Target);
        Assert.All(capture.Result().GetProperty("data").GetProperty("outcomes").EnumerateArray(), x => Assert.Equal("completed", x.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task FailedCreateIsUncertainAndNeverRetried()
    {
        await using var fake = new FakeProvider { FailSubmission = true };
        using var capture = new Capture();
        Assert.Equal(6, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal(1, fake.Navigator.Submissions);
        var outcome = capture.Result().GetProperty("data").GetProperty("outcomes")[0];
        Assert.Equal("uncertain", outcome.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CancellationRetainsCompletedActionsAndUncertainCurrentAction()
    {
        await using var fake = new FakeProvider { CancelSecond = true };
        using var capture = new Capture();
        Assert.Equal(130, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal("completed", outcomes[0].GetProperty("status").GetString());
        Assert.Equal("uncertain", outcomes[1].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false, 5, "failed")]
    [InlineData(true, 6, "failed")]
    public async Task FormPreparationAndKnownRejectionReportWriteBoundary(bool submitted, int exit, string status)
    {
        await using var fake = new FakeProvider { ActionFailure = new RegistryActionExecutionException(submitted,
            submitted ? new RegistrySubmissionRejectedException("rejected") : new IOException("form read failed")) };
        using var capture = new Capture();
        Assert.Equal(exit, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal(status, outcomes[0].GetProperty("status").GetString());
        Assert.Equal("not-attempted", outcomes[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task DisposalFailureStillEmitsOneResultAfterCompletedActions()
    {
        await using var fake = new FakeProvider { DisposeFailure = true };
        using var capture = new Capture();
        Assert.Equal(6, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher" }, new() { Json = true }, new() { Apply = true }));
        Assert.Equal("partial", capture.Result().GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    public async Task FailureBeforeWritesUsesAuthenticationOrOperationExit(bool auth, int expected)
    {
        await using var fake = new FakeProvider { BeforePlanFailure = auth ? new RegistryAuthenticationException("Missing configured credentials.") : new IOException("read failed") };
        Assert.Equal(expected, await new TestCommands(fake).Sync(new(), new() { Profile = "teacher" }, new(), new() { Apply = true }));
        Assert.Equal(0, fake.Navigator.Submissions);
    }

    [Fact]
    public async Task AccountLockConflictsAcrossProfileAliases()
    {
        var account = new RegistryDestination("test-" + Guid.NewGuid(), "https://registry.test/");
        var locks = new RegistryAccountLock();
        await using var first = await locks.Acquire(account, default);
        await Assert.ThrowsAsync<ScheduleLib.Application.Core.LocalOperationBusyException>(() => locks.Acquire(account, default));
    }

    private sealed class TestCommands(FakeProvider provider, FakeLock? accountLock = null) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => accountLock ?? new FakeLock();
    }
    private sealed class FakeLock : IRegistryAccountLock, IAsyncDisposable
    {
        public RegistryDestination? Target;
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token) { Target = target; return Task.FromResult<IAsyncDisposable>(this); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeProvider : IRegistryProvider, IRegistrySession
    {
        public FakeNavigator Navigator = new();
        public int Plans;
        public bool FailSubmission;
        public bool CancelSecond;
        public bool DisposeFailure;
        public Exception? BeforePlanFailure;
        public Exception? ActionFailure;
        public RegistryDestination Target { get; } = new("actual-account", "https://registry.test/");
        public Task<IRegistrySession> Open(SourceArguments source, SettingsArguments settings, CancellationToken token) => Task.FromResult<IRegistrySession>(this);
        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            if (BeforePlanFailure is not null) throw BeforePlanFailure;
            Plans++;
            IReadOnlyList<RegistrySyncAction> actions = new[] { "create", "update", "delete" }.Select(kind => new RegistrySyncAction(kind,
                Target.Destination, "course", "group", new(2026, 1, Plans), "topic", 2, true, null, async ct =>
                {
                    if (ActionFailure is not null) throw ActionFailure;
                    if (kind == "delete") await Navigator.SubmitDelete(null!); else await Navigator.SubmitLesson(null!);
                    if (FailSubmission) throw new InvalidOperationException("validation failure");
                    if (CancelSecond && kind == "update") throw new OperationCanceledException();
                })).ToArray();
            return Task.FromResult(actions);
        }
        public ValueTask DisposeAsync()
        {
            if (DisposeFailure) { DisposeFailure = false; throw new IOException("dispose failed"); }
            return ValueTask.CompletedTask;
        }
    }
    private sealed class FakeNavigator : IRegistrySyncNavigator
    {
        public int Submissions;
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) => Task.FromResult<IEnumerable<CourseLink>>([]);
        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink course) => Task.FromResult<IEnumerable<GroupLink>>([]);
        public Task<IDocument> GetHtml(Uri uri) => throw new NotSupportedException();
        public Task SubmitLesson(IDocument document) { Submissions++; return Task.CompletedTask; }
        public Task SubmitDelete(IDocument document) { Submissions++; return Task.CompletedTask; }
    }
    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistrySyncNavigator
    {
        public int Submissions;
        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) => Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);
        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) => Task.FromResult<IEnumerable<GroupLink>>([group]);
        public async Task<IDocument> GetHtml(Uri uri) => await new HtmlParser().ParseDocumentAsync(uri.AbsolutePath == "/group"
            ? "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>"
            : "<html></html>");
        public Task SubmitLesson(IDocument document) { Submissions++; throw new InvalidOperationException("Preview submitted a lesson."); }
        public Task SubmitDelete(IDocument document) { Submissions++; throw new InvalidOperationException("Preview submitted a deletion."); }
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
