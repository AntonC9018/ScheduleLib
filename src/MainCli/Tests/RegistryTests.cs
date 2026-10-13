using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;

public sealed class RegistryTests
{
    [Fact]
    public async Task MissingDestinationDateRangeIsReportedWithoutAbortingPlan()
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
        services.Configure<ScheduleBuilderInitializerOptions>(options => options.EnrichWithFullNames = false);
        services.Configure<StudyYearOptions>(options =>
        {
            options.StudyYear = new(2025);
            options.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons().First(item => item.Lesson.Groups.IsSingleGroup);
        schedule.Get(lesson.Lesson.Groups[0]).AttendanceMode = AttendanceMode.DualFr;
        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    Value = lesson.Lesson.Groups,
                    IsWildcard = false,
                },
                lesson.Lesson.GroupPartitionKey,
                new("https://registry.test/group"),
                new("https://registry.test/evaluation")));
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().Plan(
            new()
            {
                Navigator = navigator,
                Attendance = new([]),
                LessonTopics = NoLessonTopics.Instance,
                Semester = Semester.Sem2,
                Diagnostics = diagnostics,
            });
        Assert.Empty(plan);
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-schedule-dates");
        Assert.Equal(0, navigator.Submissions);
    }

    [Theory]
    [InlineData(
        new[]
        {
            "registry",
            "sync",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "registry",
            "sync",
            "--quiz-id",
            "1",
        },
        2)]
    [InlineData(
        new[]
        {
            "registry",
            "sync",
            "--output",
            "/tmp/unused",
        },
        2)]
    public async Task RoutingRejectsIrrelevantArguments(string[] args, int exit) =>
        Assert.Equal(exit, await CliHost.Run(args));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealReconciliationPlanDoesNotSubmitThroughFakeNavigator(
        bool singleDestination,
        bool hasAttendanceSources)
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
        var lesson = singleDestination
            ? schedule.EnumerateWeeklyLessons().First(item => item.Lesson.Groups.Count > 1)
            : schedule.EnumerateWeeklyLessons().First();
        var destinationGroups = singleDestination ? new LessonGroups { lesson.Lesson.Groups[0] } : lesson.Lesson.Groups;
        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !singleDestination && !destinationGroups.IsSingleGroup,
                    Value = destinationGroups,
                },
                lesson.Lesson.GroupPartitionKey,
                new("https://registry.test/group"),
                new("https://registry.test/evaluation")));
        var diagnostics = new RegistrySyncDiagnostics();
        var topics = new RecordingTopics();
        var plan = await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().Plan(
            new()
            {
                Navigator = navigator,
                Attendance = new([]),
                LessonTopics = topics,
                Semester = Semester.Sem2,
                Diagnostics = diagnostics,
                HasAttendanceSources = hasAttendanceSources,
            },
            explicitApply: true);
        Assert.NotEmpty(plan);
        Assert.Contains(plan, action => action.Kind == "create");
        Assert.Equal(0, navigator.Submissions);
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-registry-course");
        Assert.Equal(hasAttendanceSources, diagnostics.Items.Any(item => item.Kind == "missing-attendance-sheet"));
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-lesson-topics");
        Assert.NotEmpty(topics.Keys);
        Assert.All(topics.Keys, key => Assert.True(key.Groups.Value.IsSubSetOf(destinationGroups)));
        if (singleDestination)
        {
            Assert.Contains(
                diagnostics.Items,
                item => item.Kind == "missing-registry-group"
                    && item.Course == schedule.Get(lesson.Lesson.Course).FullName
                    && item.Group == schedule.Get(lesson.Lesson.Groups[1]).Name);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGroupPlanIsDeliveredBeforeNextGroupScan(bool continueScanning)
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
        var lesson = scope.ServiceProvider.GetRequiredService<Schedule>().EnumerateWeeklyLessons().First();
        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                new("https://registry.test/group"),
                new("https://registry.test/evaluation"))) { TwoGroups = true };
        var delivered = 0;
        await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().PlanGroups(
            new()
            {
                Navigator = navigator,
                Attendance = new([]),
                LessonTopics = NoLessonTopics.Instance,
                Semester = Semester.Sem2,
            },
            actions =>
            {
                Assert.NotEmpty(actions);
                Assert.Equal(++delivered, navigator.GroupReads);
                return Task.FromResult(continueScanning);
            },
            explicitApply: true);
        Assert.Equal(continueScanning ? 2 : 1, delivered);
        Assert.Equal(delivered, navigator.GroupReads);
        Assert.Equal(0, navigator.Submissions);
    }

    [Theory]
    [InlineData(
        false,
        false,
        3,
        "preview")]
    [InlineData(
        true,
        false,
        6,
        "completed")]
    [InlineData(
        true,
        true,
        6,
        "uncertain")]
    public async Task IncrementalApplyRetainsWritesAndStopsScanningOnFailure(
        bool apply,
        bool failSubmission,
        int exit,
        string actionStatus)
    {
        await using var fake = new IncrementalProvider { FailSubmission = failSubmission };
        using var capture = new Capture();
        Assert.Equal(
            exit,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = apply }));
        Assert.Equal(apply ? 1 : 0, fake.Submissions);
        Assert.Equal(!failSubmission, fake.LaterScanStarted);
        var result = capture.Result();
        Assert.Equal(apply ? "partial" : "failed", result.GetProperty("status").GetString());
        var outcomes = result.GetProperty("data").GetProperty("outcomes");
        Assert.Equal(1, outcomes.GetArrayLength());
        Assert.Equal(
            actionStatus == "preview" ? "planned" : actionStatus,
            outcomes[0].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyContinuesOnlyLocalInvalidFormsWhileStrictApplyStops(bool strict)
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
        var lesson = schedule.EnumerateWeeklyLessons().First();
        var navigator = new PreparationNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                new("https://registry.test/group"),
                new("https://registry.test/evaluation")));
        var handler = scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>();
        var parameters = new AddLessonsToOnlineRegistryTaskHandler.RunParams
        {
            Navigator = navigator,
            Attendance = new([]),
            LessonTopics = NoLessonTopics.Instance,
            Semester = Semester.Sem2,
        };
        if (strict)
        {
            var plan = await handler.Plan(parameters, explicitApply: true);
            Assert.True(plan.Count > 1);
            using var capture = new Capture();
            await using var session = new PlannedProvider(plan);
            Assert.Equal(
                5,
                await new TestCommands(session).Sync(
                    new(),
                    new() { Profile = "test" },
                    new() { Json = true },
                    new() { Apply = true }));
            var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
            Assert.Equal("failed", outcomes[0].GetProperty("status").GetString());
            Assert.Equal("not-attempted", outcomes[1].GetProperty("status").GetString());
            Assert.Equal(0, navigator.Submissions);
            Assert.Equal(1, navigator.FormReads);
        }
        else
        {
            await handler.Run(parameters);
            Assert.True(navigator.FormReads > 1);
            Assert.Equal(navigator.FormReads - 1, navigator.Submissions);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyDoesNotSwallowReadOrSubmissionInvalidOperation(bool submitted)
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
        var lesson = scope.ServiceProvider.GetRequiredService<Schedule>().EnumerateWeeklyLessons().First();
        var navigator = new PreparationNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                new("https://registry.test/group"),
                new("https://registry.test/evaluation")))
        {
            InvalidFirstForm = false,
            ReadFailure = !submitted,
            SubmissionFailure = submitted,
        };
        var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(() =>
            scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>().Run(
                new()
                {
                    Navigator = navigator,
                    Attendance = new([]),
                    LessonTopics = NoLessonTopics.Instance,
                    Semester = Semester.Sem2,
                }));
        Assert.Equal(submitted, error.SubmissionStarted);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(1, navigator.FormReads);
        Assert.Equal(submitted ? 1 : 0, navigator.Submissions);
    }

    [Fact]
    public async Task PreviewMakesZeroNavigatorWritesAndReportsAllKinds()
    {
        await using var fake = new FakeProvider();
        using var capture = new Capture();
        Assert.Equal(
            0,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher-alias" },
                new() { Json = true },
                new()));
        Assert.Equal(0, fake.Navigator.Submissions);
        Assert.Equal(1, fake.Plans);
        var result = capture.Result();
        Assert.Equal("preview", result.GetProperty("status").GetString());
        Assert.Equal(
            "actual-account",
            result.GetProperty("data").GetProperty("target").GetProperty("account").GetString());
        Assert.Equal(3, result.GetProperty("data").GetProperty("outcomes").GetArrayLength());
    }

    [Fact]
    public async Task ApplyRecomputesAndRecordsCompletedActions()
    {
        await using var fake = new FakeProvider();
        await using var locks = new FakeLock();
        var commands = new TestCommands(fake, locks);
        Assert.Equal(
            0,
            await commands.Sync(
                new(),
                new() { Profile = "first-alias" },
                new(),
                new()));
        using var capture = new Capture();
        Assert.Equal(
            0,
            await commands.Sync(
                new(),
                new() { Profile = "other-alias" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal(2, fake.Plans);
        Assert.Equal(3, fake.Navigator.Submissions);
        Assert.Equal(fake.Target, locks.Target);
        Assert.All(
            capture.Result().GetProperty("data").GetProperty("outcomes").EnumerateArray(),
            x => Assert.Equal("completed", x.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task FailedCreateIsUncertainAndNeverRetried()
    {
        await using var fake = new FakeProvider { FailSubmission = true };
        using var capture = new Capture();
        Assert.Equal(
            6,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal(1, fake.Navigator.Submissions);
        var outcome = capture.Result().GetProperty("data").GetProperty("outcomes")[0];
        Assert.Equal("uncertain", outcome.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CancellationRetainsCompletedActionsAndUncertainCurrentAction()
    {
        await using var fake = new FakeProvider { CancelSecond = true };
        using var capture = new Capture();
        Assert.Equal(
            130,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal("completed", outcomes[0].GetProperty("status").GetString());
        Assert.Equal("uncertain", outcomes[1].GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(
        false,
        5,
        "failed")]
    [InlineData(
        true,
        6,
        "failed")]
    public async Task FormPreparationAndKnownRejectionReportWriteBoundary(
        bool submitted,
        int exit,
        string status)
    {
        await using var fake = new FakeProvider
        {
            ActionFailure = new RegistryActionExecutionException(
                submitted,
                submitted ? new RegistrySubmissionRejectedException("rejected") : new IOException("form read failed")),
        };
        using var capture = new Capture();
        Assert.Equal(
            exit,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        var outcomes = capture.Result().GetProperty("data").GetProperty("outcomes");
        Assert.Equal(status, outcomes[0].GetProperty("status").GetString());
        Assert.Equal("not-attempted", outcomes[1].GetProperty("status").GetString());
    }

    [Fact]
    public async Task DisposalFailureStillEmitsOneResultAfterCompletedActions()
    {
        await using var fake = new FakeProvider { DisposeFailure = true };
        using var capture = new Capture();
        Assert.Equal(
            6,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new() { Json = true },
                new() { Apply = true }));
        Assert.Equal("partial", capture.Result().GetProperty("status").GetString());
    }

    [Theory]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    public async Task FailureBeforeWritesUsesAuthenticationOrOperationExit(bool auth, int expected)
    {
        await using var fake = new FakeProvider
        {
            BeforePlanFailure =
                auth
                    ? new RegistryAuthenticationException("Missing configured credentials.")
                    : new IOException("read failed"),
        };
        Assert.Equal(
            expected,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new(),
                new() { Apply = true }));
        Assert.Equal(0, fake.Navigator.Submissions);
    }

    [Fact]
    public async Task AccountLockConflictsAcrossProfileAliases()
    {
        var account = new RegistryDestination("test-" + Guid.NewGuid(), "https://registry.test/");
        var locks = new RegistryAccountLock();
        await using var first = await locks.Acquire(account, default);
        await Assert.ThrowsAsync<ScheduleLib.Application.Core.LocalOperationBusyException>(() =>
            locks.Acquire(account, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTargetsAreIncludedInFinalJsonAndJsonl(bool jsonl)
    {
        await using var fake = new FakeProvider();
        fake.DiagnosticItems.Add(
            new(
                "missing-registry-group",
                "Synthetic course",
                "IA2502",
                "Ask the registry administrator to add the English subgroup."));
        using var capture = new Capture();
        Assert.Equal(
            0,
            await new TestCommands(fake).Sync(
                new(),
                new() { Profile = "teacher" },
                new()
                {
                    Json = !jsonl,
                    Jsonl = jsonl,
                },
                new()));
        var final = jsonl ? capture.FinalRecord() : capture.Result();
        var data = final.GetProperty("data");
        Assert.Equal("IA2502", data.GetProperty("diagnostics")[0].GetProperty("group").GetString());
        Assert.Contains("English subgroup", final.GetProperty("warnings")[1].GetString());
        Assert.Equal(0, fake.Navigator.Submissions);
    }

    private sealed class TestCommands(IRegistryProvider provider, FakeLock? accountLock = null) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => accountLock ?? new FakeLock();
    }

    private sealed class FakeLock : IRegistryAccountLock, IAsyncDisposable
    {
        public RegistryDestination? Target;

        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
        {
            Target = target;
            return Task.FromResult<IAsyncDisposable>(this);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class IncrementalProvider : IRegistryProvider, IRegistrySession
    {
        public bool FailSubmission;
        public int Submissions;
        public bool LaterScanStarted;

        public RegistryDestination Target
        {
            get;
        } = new("test", "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) =>
            throw new InvalidOperationException("Whole-account planning must not be used.");

        public async Task PlanGroups(
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
            CancellationToken token)
        {
            var action = new RegistrySyncAction(
                "create",
                Target.Destination,
                "course",
                "group",
                new(
                    2026,
                    1,
                    1),
                "topic",
                0,
                true,
                null,
                _ =>
                {
                    Submissions++;
                    if (FailSubmission)
                    {
                        throw new RegistryActionExecutionException(true, new IOException("lost response"));
                    }

                    return Task.CompletedTask;
                });
            if (!await onGroupPlanned([action]))
            {
                return;
            }

            LaterScanStarted = true;
            throw new JsonException("Later group planning failed.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class PlannedProvider(IReadOnlyList<RegistrySyncAction> actions)
        : IRegistryProvider, IRegistrySession
    {
        public RegistryDestination Target
        {
            get;
        } = new("test", "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token) => Task.FromResult(actions);
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

        public List<RegistrySyncDiagnostic> DiagnosticItems
        {
            get;
        } = [];

        public IReadOnlyList<RegistrySyncDiagnostic> Diagnostics => DiagnosticItems;

        public RegistryDestination Target
        {
            get;
        } = new("actual-account", "https://registry.test/");

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token) => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            if (BeforePlanFailure is not null)
            {
                throw BeforePlanFailure;
            }

            Plans++;
            IReadOnlyList<RegistrySyncAction> actions = new[]
            {
                "create",
                "update",
                "delete",
            }.Select(kind => new RegistrySyncAction(
                kind,
                Target.Destination,
                "course",
                "group",
                new(
                    2026,
                    1,
                    Plans),
                "topic",
                2,
                true,
                null,
                async ct =>
                {
                    if (ActionFailure is not null)
                    {
                        throw ActionFailure;
                    }

                    if (kind == "delete")
                    {
                        await Navigator.SubmitDelete(null!);
                    }
                    else
                    {
                        await Navigator.SubmitLesson(null!);
                    }

                    if (FailSubmission)
                    {
                        throw new InvalidOperationException("validation failure");
                    }

                    if (CancelSecond && kind == "update")
                    {
                        throw new OperationCanceledException();
                    }
                })).ToArray();
            return Task.FromResult(actions);
        }

        public ValueTask DisposeAsync()
        {
            if (DisposeFailure)
            {
                DisposeFailure = false;
                throw new IOException("dispose failed");
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeNavigator : IRegistrySyncNavigator
    {
        public int Submissions;

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) =>
            Task.FromResult<IEnumerable<CourseLink>>([]);

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink course) => Task.FromResult<IEnumerable<GroupLink>>([]);
        public Task<IDocument> GetHtml(Uri uri) => throw new NotSupportedException();

        public Task SubmitLesson(IDocument document)
        {
            Submissions++;
            return Task.CompletedTask;
        }

        public Task SubmitDelete(IDocument document)
        {
            Submissions++;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingTopics : ILessonTopics
    {
        public List<AttendanceLookupKey> Keys
        {
            get;
        } = [];

        public string? Get(in AttendanceLookupKey key)
        {
            Keys.Add(key);
            return null;
        }
    }

    private sealed class FixtureNavigator(CourseId course, GroupLink group) : IRegistrySyncNavigator
    {
        public int Submissions;
        public int GroupReads;
        public bool TwoGroups;

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) =>
            Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) => Task.FromResult<IEnumerable<GroupLink>>(
            TwoGroups
                ?
                [
                    group, new(
                        group.Groups,
                        group.GroupPartition,
                        new("https://registry.test/group2"),
                        group.EvaluationUri),
                ]
                : [group]);

        public async Task<IDocument> GetHtml(Uri uri)
        {
            var isGroup = uri.AbsolutePath.StartsWith("/group", StringComparison.Ordinal);
            if (isGroup)
            {
                GroupReads++;
            }

            return await new HtmlParser().ParseDocumentAsync(
                isGroup
                    ? "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>"
                    : "<html></html>");
        }

        public Task SubmitLesson(IDocument document)
        {
            Submissions++;
            throw new InvalidOperationException("Preview submitted a lesson.");
        }

        public Task SubmitDelete(IDocument document)
        {
            Submissions++;
            throw new InvalidOperationException("Preview submitted a deletion.");
        }
    }

    private sealed class PreparationNavigator(CourseId course, GroupLink group) : IRegistrySyncNavigator
    {
        public int Submissions;
        public int FormReads;
        public bool InvalidFirstForm = true;
        public bool ReadFailure;
        public bool SubmissionFailure;
        private bool _initialFormRead;

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) =>
            Task.FromResult<IEnumerable<CourseLink>>([new(course, new("https://registry.test/course"))]);

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link) =>
            Task.FromResult<IEnumerable<GroupLink>>([group]);

        public async Task<IDocument> GetHtml(Uri uri)
        {
            if (uri.AbsolutePath == "/group")
            {
                return await new HtmlParser().ParseDocumentAsync(
                    "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>");
            }

            if (!_initialFormRead)
            {
                _initialFormRead = true;
                return await new HtmlParser().ParseDocumentAsync("<html></html>");
            }
            FormReads++;
            if (ReadFailure)
            {
                throw new InvalidOperationException("read failure");
            }

            var extraStudent = InvalidFirstForm && FormReads == 1 ? "<tr><td>Extra student</td></tr>" : "";
            return await new HtmlParser().ParseDocumentAsync(
                "<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option></select><table><tr><th>frecvența/nota</th></tr>"
                + extraStudent
                + "</table></form>");
        }

        public Task SubmitLesson(IDocument document)
        {
            Submissions++;
            if (SubmissionFailure)
            {
                throw new InvalidOperationException("submission failure");
            }

            return Task.CompletedTask;
        }

        public Task SubmitDelete(IDocument document) => throw new NotSupportedException();
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

        public JsonElement FinalRecord()
        {
            using var doc =
                JsonDocument.Parse(_text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            Console.SetOut(_original);
            _text.Dispose();
        }
    }
}
