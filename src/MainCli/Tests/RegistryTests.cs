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
        using var settings = await CliSettings.Load(new()
        {
            Profile = "Curmanschii Anton",
        });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(options =>
        {
            var scheduleBuilderInitializerOptions = options;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
        services.Configure<StudyYearOptions>(options =>
        {
            options.StudyYear = new(2025);
            options.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons()
            .First(item => item.Lesson.Groups.IsSingleGroup);
        schedule.Get(lesson.Lesson.Groups[0]).AttendanceMode = AttendanceMode.DualFr;

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/group");
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            Uri evaluationUri2 = new("https://registry.test/evaluation");
            return evaluationUri2;
        }

        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    Value = lesson.Lesson.Groups,
                    IsWildcard = false,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri()));
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
            .Plan(new()
            {
                Navigator = navigator,
                Attendance = new([]),
                LessonTopics = NoLessonTopics.Instance,
                Semester = Semester.Sem2,
                Diagnostics = diagnostics,
            });
        Assert.Empty(plan);
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-schedule-dates");
        Assert.Equal(expected: 0, actual: navigator.Submissions);
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
        Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RealReconciliationPlanDoesNotSubmitThroughFakeNavigator(
        bool singleDestination,
        bool hasAttendanceSources)
    {
        using var settings = await CliSettings.Load(new()
        {
            Profile = "Curmanschii Anton",
        });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            var scheduleBuilderInitializerOptions = x;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
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
            ? schedule.EnumerateWeeklyLessons()
                .First(item => item.Lesson.Groups.Count > 1)
            : schedule.EnumerateWeeklyLessons()
                .First();
        var destinationGroups = singleDestination ? new LessonGroups { lesson.Lesson.Groups[0] } : lesson.Lesson.Groups;

        bool CheckCondition2()
        {
            if (singleDestination)
            {
                return false;
            }

            if (!destinationGroups.IsSingleGroup)
            {
                return true;
            }

            return false;
        }

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/group");
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            Uri evaluationUri2 = new("https://registry.test/evaluation");
            return evaluationUri2;
        }

        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = CheckCondition2(),
                    Value = destinationGroups,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri()));
        var diagnostics = new RegistrySyncDiagnostics();
        var topics = new RecordingTopics();
        var plan = await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
            .Plan(
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
        Assert.Equal(expected: 0, actual: navigator.Submissions);
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-registry-course");
        Assert.Equal(expected: hasAttendanceSources,
            actual: diagnostics.Items.Any(item => item.Kind == "missing-attendance-sheet"));
        Assert.Contains(diagnostics.Items, item => item.Kind == "missing-lesson-topics");
        Assert.NotEmpty(topics.Keys);
        Assert.All(topics.Keys,
            key =>
            {
                bool CalculateCondition()
                {
                    bool condition = key.Groups.Value.IsSubSetOf(destinationGroups);
                    return condition;
                }

                Assert.True(CalculateCondition());
            });
        if (singleDestination)
        {
            Assert.Contains(
                diagnostics.Items,
                item =>
                {
                    bool CheckCondition()
                    {
                        if (item.Kind != "missing-registry-group")
                        {
                            return false;
                        }

                        if (item.Course != schedule.Get(lesson.Lesson.Course).FullName)
                        {
                            return false;
                        }

                        if (item.Group == schedule.Get(lesson.Lesson.Groups[1]).Name)
                        {
                            return true;
                        }

                        return false;
                    }

                    return CheckCondition();
                });
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealGroupPlanIsDeliveredBeforeNextGroupScan(bool continueScanning)
    {
        using var settings = await CliSettings.Load(new()
        {
            Profile = "Curmanschii Anton",
        });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            var scheduleBuilderInitializerOptions = x;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var lesson = scope.ServiceProvider.GetRequiredService<Schedule>()
            .EnumerateWeeklyLessons()
            .First();

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/group");
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            Uri evaluationUri2 = new("https://registry.test/evaluation");
            return evaluationUri2;
        }

        var navigator = new FixtureNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri()))
        {
            TwoGroups = true,
        };
        var delivered = 0;
        await scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
            .PlanGroups(
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
                    int expected2 = ++delivered;
                    int actual2 = navigator.GroupReads;
                    Assert.Equal(expected: expected2, actual: actual2);
                    return Task.FromResult(continueScanning);
                },
                explicitApply: true);
        int expected2;
        if (continueScanning)
        {
            expected2 = 2;
        }
        else
        {
            expected2 = 1;
        }

        int actual2 = delivered;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: delivered, actual: navigator.GroupReads);
        Assert.Equal(expected: 0, actual: navigator.Submissions);
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
        await using var fake = new IncrementalProvider
        {
            FailSubmission = failSubmission,
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: exit,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = apply,
                }));
        int expected2;
        if (apply)
        {
            expected2 = 1;
        }
        else
        {
            expected2 = 0;
        }

        int actual2 = fake.Submissions;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: !failSubmission, actual: fake.LaterScanStarted);
        var result = capture.Result();

        string CalculateExpected()
        {
            if (apply)
            {
                return "partial";
            }

            return "failed";
        }

        Assert.Equal(expected: CalculateExpected(),
            actual: result.GetProperty("status")
                .GetString());
        var outcomes = result.GetProperty("data")
            .GetProperty("outcomes");
        Assert.Equal(expected: 1, actual: outcomes.GetArrayLength());

        string CalculateExpected2()
        {
            if (actionStatus == "preview")
            {
                return "planned";
            }

            return actionStatus;
        }

        Assert.Equal(
            expected: CalculateExpected2(),
            actual: outcomes[0].GetProperty("status")
                .GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyContinuesOnlyLocalInvalidFormsWhileStrictApplyStops(bool strict)
    {
        using var settings = await CliSettings.Load(new()
        {
            Profile = "Curmanschii Anton",
        });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            var scheduleBuilderInitializerOptions = x;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
        var lesson = schedule.EnumerateWeeklyLessons()
            .First();

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/group");
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            Uri evaluationUri2 = new("https://registry.test/evaluation");
            return evaluationUri2;
        }

        var navigator = new PreparationNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri()));
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
                expected: 5,
                actual: await new TestCommands(session).Sync(
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
                    }));
            var outcomes = capture.Result()
                .GetProperty("data")
                .GetProperty("outcomes");
            Assert.Equal(expected: "failed",
                actual: outcomes[0].GetProperty("status")
                    .GetString());
            Assert.Equal(expected: "not-attempted",
                actual: outcomes[1].GetProperty("status")
                    .GetString());
            Assert.Equal(expected: 0, actual: navigator.Submissions);
            Assert.Equal(expected: 1, actual: navigator.FormReads);
        }
        else
        {
            await handler.Run(parameters);
            Assert.True(navigator.FormReads > 1);
            Assert.Equal(expected: navigator.FormReads - 1, actual: navigator.Submissions);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyDoesNotSwallowReadOrSubmissionInvalidOperation(bool submitted)
    {
        using var settings = await CliSettings.Load(new()
        {
            Profile = "Curmanschii Anton",
        });
        var services = CliRuntime.CreateServices(
            new()
            {
                NoCache = true,
                DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
            },
            AppConfiguration.ConfigureServices);
        settings.ConfigureServices(services);
        services.Configure<ScheduleBuilderInitializerOptions>(x =>
        {
            var scheduleBuilderInitializerOptions = x;
            scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
        });
        services.Configure<StudyYearOptions>(x =>
        {
            x.StudyYear = new(2025);
            x.Semester = Semester.Sem2;
        });
        await using var provider = AppConfiguration.BuildServiceProvider(services);
        await provider.InitializeSchedule(default);
        await using var scope = provider.CreateAsyncScope();
        var lesson = scope.ServiceProvider.GetRequiredService<Schedule>()
            .EnumerateWeeklyLessons()
            .First();

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/group");
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            Uri evaluationUri2 = new("https://registry.test/evaluation");
            return evaluationUri2;
        }

        var navigator = new PreparationNavigator(
            lesson.Lesson.Course,
            new GroupLink(
                new FoundGroups
                {
                    IsWildcard = !lesson.Lesson.Groups.IsSingleGroup,
                    Value = lesson.Lesson.Groups,
                },
                lesson.Lesson.GroupPartitionKey,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri()))
        {
            InvalidFirstForm = false,
            ReadFailure = !submitted,
            SubmissionFailure = submitted,
        };
        var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(() =>
            scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
                .Run(new()
                {
                    Navigator = navigator,
                    Attendance = new([]),
                    LessonTopics = NoLessonTopics.Instance,
                    Semester = Semester.Sem2,
                }));
        Assert.Equal(expected: submitted, actual: error.SubmissionStarted);
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Equal(expected: 1, actual: navigator.FormReads);
        int expected2;
        if (submitted)
        {
            expected2 = 1;
        }
        else
        {
            expected2 = 0;
        }

        int actual2 = navigator.Submissions;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public async Task PreviewMakesZeroNavigatorWritesAndReportsAllKinds()
    {
        await using var fake = new FakeProvider();
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher-alias",
                },
                new()
                {
                    Json = true,
                },
                new()));
        Assert.Equal(expected: 0, actual: fake.Navigator.Submissions);
        Assert.Equal(expected: 1, actual: fake.Plans);
        var result = capture.Result();
        Assert.Equal(expected: "preview",
            actual: result.GetProperty("status")
                .GetString());
        Assert.Equal(
            expected: "actual-account",
            actual: result.GetProperty("data")
                .GetProperty("target")
                .GetProperty("account")
                .GetString());
        Assert.Equal(expected: 3,
            actual: result.GetProperty("data")
                .GetProperty("outcomes")
                .GetArrayLength());
    }

    [Fact]
    public async Task ApplyRecomputesAndRecordsCompletedActions()
    {
        await using var fake = new FakeProvider();
        await using var locks = new FakeLock();
        var commands = new TestCommands(fake, locks);
        Assert.Equal(
            expected: 0,
            actual: await commands.Sync(
                new(),
                new()
                {
                    Profile = "first-alias",
                },
                new(),
                new()));
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await commands.Sync(
                new(),
                new()
                {
                    Profile = "other-alias",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                }));
        Assert.Equal(expected: 2, actual: fake.Plans);
        Assert.Equal(expected: 3, actual: fake.Navigator.Submissions);
        Assert.Equal(expected: fake.Target, actual: locks.Target);
        Assert.All(
            capture.Result()
                .GetProperty("data")
                .GetProperty("outcomes")
                .EnumerateArray(),
            x => Assert.Equal(expected: "completed",
                actual: x.GetProperty("status")
                    .GetString()));
    }

    [Fact]
    public async Task FailedCreateIsUncertainAndNeverRetried()
    {
        await using var fake = new FakeProvider
        {
            FailSubmission = true,
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: 6,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                }));
        Assert.Equal(expected: 1, actual: fake.Navigator.Submissions);
        var outcome = capture.Result()
            .GetProperty("data")
            .GetProperty("outcomes")[0];
        Assert.Equal(expected: "uncertain",
            actual: outcome.GetProperty("status")
                .GetString());
    }

    [Fact]
    public async Task CancellationRetainsCompletedActionsAndUncertainCurrentAction()
    {
        await using var fake = new FakeProvider
        {
            CancelSecond = true,
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: 130,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                }));
        var outcomes = capture.Result()
            .GetProperty("data")
            .GetProperty("outcomes");
        Assert.Equal(expected: "completed",
            actual: outcomes[0].GetProperty("status")
                .GetString());
        Assert.Equal(expected: "uncertain",
            actual: outcomes[1].GetProperty("status")
                .GetString());
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
        Exception CalculateInner()
        {
            if (submitted)
            {
                return new RegistrySubmissionRejectedException("rejected");
            }

            return new IOException("form read failed");
        }

        await using var fake = new FakeProvider
        {
            ActionFailure = new RegistryActionExecutionException(
                submitted,
                CalculateInner()),
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: exit,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                }));
        var outcomes = capture.Result()
            .GetProperty("data")
            .GetProperty("outcomes");
        Assert.Equal(expected: status,
            actual: outcomes[0].GetProperty("status")
                .GetString());
        Assert.Equal(expected: "not-attempted",
            actual: outcomes[1].GetProperty("status")
                .GetString());
    }

    [Fact]
    public async Task DisposalFailureStillEmitsOneResultAfterCompletedActions()
    {
        await using var fake = new FakeProvider
        {
            DisposeFailure = true,
        };
        using var capture = new Capture();
        Assert.Equal(
            expected: 6,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = true,
                },
                new()
                {
                    Apply = true,
                }));
        Assert.Equal(expected: "partial",
            actual: capture.Result()
                .GetProperty("status")
                .GetString());
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
            expected: expected,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new(),
                new()
                {
                    Apply = true,
                }));
        Assert.Equal(expected: 0, actual: fake.Navigator.Submissions);
    }

    [Fact]
    public async Task AccountLockConflictsAcrossProfileAliases()
    {
        var account = new RegistryDestination(Account: "test-" + Guid.NewGuid(), Destination: "https://registry.test/");
        var locks = new RegistryAccountLock();
        await using var first = await locks.Acquire(account, default);
        await Assert.ThrowsAsync<LocalOperationBusyException>(() =>
            locks.Acquire(account, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingTargetsAreIncludedInFinalJsonAndJsonl(bool jsonl)
    {
        await using var fake = new FakeProvider();

        RegistrySyncDiagnostic CalculateItem()
        {
            RegistrySyncDiagnostic item = new(
                Kind: "missing-registry-group",
                Course: "Synthetic course",
                Group: "IA2502",
                Detail: "Ask the registry administrator to add the English subgroup.");
            return item;
        }

        fake.DiagnosticItems.Add(CalculateItem());
        using var capture = new Capture();
        Assert.Equal(
            expected: 0,
            actual: await new TestCommands(fake).Sync(
                new(),
                new()
                {
                    Profile = "teacher",
                },
                new()
                {
                    Json = !jsonl,
                    Jsonl = jsonl,
                },
                new()));
        var final = jsonl ? capture.FinalRecord() : capture.Result();
        var data = final.GetProperty("data");
        Assert.Equal(expected: "IA2502",
            actual: data.GetProperty("diagnostics")[0]
                .GetProperty("group")
                .GetString());
        Assert.Contains(expectedSubstring: "English subgroup",
            actualString: final.GetProperty("warnings")[1]
                .GetString());
        Assert.Equal(expected: 0, actual: fake.Navigator.Submissions);
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

        public RegistryDestination Target { get; } = new(Account: "test", Destination: "https://registry.test/");

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
            DateTime CalculateDate()
            {
                DateTime date = new(
                    year: 2026,
                    month: 1,
                    day: 1);
                return date;
            }

            var action = new RegistrySyncAction(
                kind: "create",
                destination: Target.Destination,
                course: "course",
                groups: "group",
                CalculateDate(),
                topic: "topic",
                0,
                true,
                omission: null,
                _ =>
                {
                    Submissions++;
                    if (FailSubmission)
                    {
                        IOException CalculateInner()
                        {
                            var inner = new IOException("lost response");
                            return inner;
                        }

                        throw new RegistryActionExecutionException(true, CalculateInner());
                    }

                    return Task.CompletedTask;
                });
            if (!(await onGroupPlanned([action])))
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
        public RegistryDestination Target { get; } = new(Account: "test", Destination: "https://registry.test/");

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

        public List<RegistrySyncDiagnostic> DiagnosticItems { get; } = [];

        public IReadOnlyList<RegistrySyncDiagnostic> Diagnostics => DiagnosticItems;

        public RegistryDestination Target { get; } =
            new(Account: "actual-account", Destination: "https://registry.test/");

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
                }.Select(kind =>
                {
                    RegistrySyncAction ProjectValue()
                    {
                        string operationValue = kind;
                        string operationValue2 = Target.Destination;
                        string operationValue3 = "course";
                        string operationValue4 = "group";
                        DateTime operationValue5 = new(
                            year: 2026,
                            month: 1,
                            day: Plans);
                        var operationValue6 = operationValue5;
                        string operationValue7 = "topic";
                        int? operationValue8 = 2;
                        bool operationValue9 = true;
                        string? operationValue10 = null;
                        Func<CancellationToken, Task> operationValue11 = async ct =>
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

                            bool CalculateConditionResult()
                            {
                                if (!CancelSecond)
                                {
                                    return false;
                                }

                                if (kind == "update")
                                {
                                    return true;
                                }

                                return false;
                            }

                            bool conditionResult = CalculateConditionResult();


                            if (conditionResult)
                            {
                                throw new OperationCanceledException();
                            }
                        };
                        var operationValue12 = new RegistrySyncAction(
                            kind: operationValue,
                            destination: operationValue2,
                            course: operationValue3,
                            groups: operationValue4,
                            operationValue6,
                            topic: operationValue7,
                            operationValue8,
                            operationValue9,
                            omission: operationValue10,
                            operationValue11);
                        return operationValue12;
                    }

                    return ProjectValue();
                })
                .ToArray();
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
        public List<AttendanceLookupKey> Keys { get; } = [];

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

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
        {
            Uri CalculateUrl()
            {
                Uri url = new("https://registry.test/course");
                return url;
            }

            return Task.FromResult<IEnumerable<CourseLink>>([new(course, CalculateUrl())]);
        }

        public Task<IEnumerable<GroupLink>> GetGroups(CourseLink link)
        {
            Uri CalculateUri()
            {
                Uri uri2 = new("https://registry.test/group2");
                return uri2;
            }

            return Task.FromResult<IEnumerable<GroupLink>>(
                TwoGroups
                    ?
                    [
                        group, new(
                            group.Groups,
                            group.GroupPartition,
                            uri: CalculateUri(),
                            evaluationUri: group.EvaluationUri),
                    ]
                    : [group]);
        }

        public async Task<IDocument> GetHtml(Uri uri)
        {
            var isGroup = uri.AbsolutePath.StartsWith("/group", StringComparison.Ordinal);
            if (isGroup)
            {
                GroupReads++;
            }

            string CalculateSource()
            {
                if (isGroup)
                {
                    return
                        "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>";
                }

                return "<html></html>";
            }

            return await new HtmlParser().ParseDocumentAsync(
                CalculateSource());
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

        public Task<IEnumerable<CourseLink>> GetCourses(Semester semester)
        {
            Uri CalculateUrl()
            {
                Uri url = new("https://registry.test/course");
                return url;
            }

            return Task.FromResult<IEnumerable<CourseLink>>([new(course, CalculateUrl())]);
        }

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

            bool CalculateConditionResult()
            {
                if (!InvalidFirstForm)
                {
                    return false;
                }

                if (FormReads == 1)
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            var extraStudent = conditionResult ? "<tr><td>Extra student</td></tr>" : "";
            return await new HtmlParser().ParseDocumentAsync(
                $"<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option></select><table><tr><th>frecvența/nota</th></tr>{extraStudent}</table></form>");
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
                JsonDocument.Parse(_text.ToString()
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Last());
            return doc.RootElement.Clone();
        }

        public void Dispose()
        {
            Console.SetOut(_original);
            _text.Dispose();
        }
    }
}
