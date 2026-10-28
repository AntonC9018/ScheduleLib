using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;

public sealed class RegistryLabDestinationTests
{
    [Theory]
    [InlineData(
        false,
        false,
        false)]
    [InlineData(
        false,
        false,
        true)]
    [InlineData(
        false,
        true,
        false)]
    [InlineData(
        false,
        true,
        true)]
    [InlineData(
        true,
        false,
        false)]
    [InlineData(
        true,
        false,
        true)]
    [InlineData(
        true,
        true,
        false)]
    [InlineData(
        true,
        true,
        true)]
    public async Task IntakeFallbackPlansScopedLabsForEachScheduledParent(
        bool combined,
        bool ownLecture,
        bool explicitApply)
    {
        await using var fixture = new Fixture(ownLecture: ownLecture, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: combined);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            explicitApply);

        Assert.NotEmpty(plan);
        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
        Assert.Contains(plan, action => action.Topic == "lab (IA2403 (ru) /spec:DezvoltareaAplicatiilor)");
        Assert.Contains(plan, action => action.Topic == "lab (IA2404 (ru) /spec:DezvoltareaAplicatiilor)");
        if (ownLecture)
        {
            Assert.Contains(
                "lecture (IA2403 (ru), IA2404 (ru) /spec:DezvoltareaAplicatiilor)",
                plan.Select(action => action.Topic));
        }

        Assert.True(navigator.Reads > 0);
    }

    [Fact]
    public async Task CliPreviewsScopedFallbackLabsWithoutSubmissions()
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            await using var provider = new Provider(
                fixture,
                navigator,
                diagnostics);
            var command = new Commands(provider);
            Assert.Equal(
                expected: 0,
                actual: await command.Sync(
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
                        Apply = false,
                    }));
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: "preview",
                actual: result.RootElement.GetProperty("status")
                    .GetString());
            Assert.Equal(expected: 0,
                actual: result.RootElement.GetProperty("errors")
                    .GetArrayLength());
            Assert.True(result.RootElement.GetProperty("data")
                    .GetProperty("outcomes")
                    .GetArrayLength()
                > 0);
            Assert.True(navigator.Reads > 0);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SpecificParentTakesItsLabsWhileOtherParentUsesFallback(bool explicitApply)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: true,
            combined: false,
            includeExplicitDestination: true);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            explicitApply);

        Assert.NotEmpty(plan);
        Assert.Contains(
            plan,
            action =>
            {
                if (action.Destination != "https://registry.test/explicit-group")
                {
                    return false;
                }

                if (action.Topic == "lab")
                {
                    return true;
                }

                return false;
            });
        var fallback = plan.Where(action => action.Destination == navigator.Group.Uri.ToString())
            .ToArray();
        Assert.NotEmpty(fallback);
        Assert.All(fallback,
            action => Assert.Equal(expected: "lab (IA2404 (ru) /spec:DezvoltareaAplicatiilor)", actual: action.Topic));
        Assert.All(fallback, action => Assert.Equal(expected: DayOfWeek.Friday, actual: action.Date.DayOfWeek));
        Assert.Contains(navigator.Group.Uri, navigator.ReadUris);

        Uri CalculateExpected()
        {
            var expected2 = new Uri("https://registry.test/explicit-group");
            return expected2;
        }

        Assert.Contains(CalculateExpected(), navigator.ReadUris);
        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
    }

    private sealed class Commands(Provider provider) : RegistryCommands
    {
        protected override IRegistryProvider CreateProvider() => provider;
        protected override IRegistryAccountLock CreateAccountLock() => new NoopLock();
    }

    private sealed class NoopLock : IRegistryAccountLock, IAsyncDisposable
    {
        public Task<IAsyncDisposable> Acquire(RegistryDestination target, CancellationToken token)
            => Task.FromResult<IAsyncDisposable>(this);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Provider(Fixture fixture, Navigator navigator, RegistrySyncDiagnostics diagnostics)
        : IRegistryProvider, IRegistrySession
    {
        public RegistryDestination Target => new(Account: "synthetic-teacher", Destination: "https://registry.test/");
        public IReadOnlyList<RegistrySyncDiagnostic> Diagnostics => diagnostics.Items;
        public RegistrySyncDiagnostics LiveDiagnostics => diagnostics;

        public Task<IRegistrySession> Open(
            SourceArguments source,
            SettingsArguments settings,
            CancellationToken token)
            => Task.FromResult<IRegistrySession>(this);

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(CancellationToken token)
        {
            return fixture.Plan(
                navigator,
                diagnostics,
                explicitApply: true);
        }

        public Task PlanIntakeLabCleanup(
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
            CancellationToken token)
        {
            return fixture.Cleanup(
                navigator,
                diagnostics,
                onGroupPlanned,
                token);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Theory]
    [InlineData(
        false,
        false,
        false)]
    [InlineData(
        true,
        false,
        false)]
    [InlineData(
        true,
        true,
        false)]
    [InlineData(
        true,
        false,
        true)]
    public async Task CleanupSelectsOnlyUnsafeLabColumnsAndUsesTheCliApplyBoundary(
        bool apply,
        bool failDelete,
        bool uncertainSuccessfulResponse)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: true,
            combined: false,
            includeExplicitDestination: true,
            includeAllExplicitDestinations: true);
        navigator.AddMixedLessons();
        navigator.AllowDeletes = true;
        navigator.FailDelete = failDelete;
        navigator.UncertainSuccessfulResponse = uncertainSuccessfulResponse;
        var diagnostics = new RegistrySyncDiagnostics();
        await using var provider = new Provider(
            fixture,
            navigator,
            diagnostics);
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await new Commands(provider).CleanupIntakeLabs(
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
                });
            int expected3;
            if (failDelete)
            {
                expected3 = 6;
            }
            else
            {
                expected3 = 0;
            }

            int actual2 = exit;
            Assert.Equal(expected: expected3, actual: actual2);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var outcomes = result.RootElement.GetProperty("data")
                .GetProperty("outcomes")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(expected: 2, actual: outcomes.Length);
            Assert.All(outcomes,
                action => Assert.Equal(expected: "delete",
                    actual: action.GetProperty("kind")
                        .GetString()));
            Assert.All(
                outcomes,
                action => Assert.Equal(expected: navigator.Group.Uri.ToString(),
                    actual: action.GetProperty("destination")
                        .GetString()));
            Assert.Equal(expected: 1,
                actual: outcomes[0].GetProperty("attendanceCount")
                    .GetInt32());

            string CalculateExpected2()
            {
                if (!apply)
                {
                    return "planned";
                }

                if (failDelete)
                {
                    return "uncertain";
                }

                return "completed";
            }

            Assert.Equal(
                expected: CalculateExpected2(),
                actual: outcomes[0].GetProperty("status")
                    .GetString());

            string CalculateExpected3()
            {
                if (failDelete)
                {
                    return "not-attempted";
                }

                if (apply)
                {
                    return "completed";
                }

                return "planned";
            }

            Assert.Equal(
                expected: CalculateExpected3(),
                actual: outcomes[1].GetProperty("status")
                    .GetString());
            int expected4;
            if (apply)
            {
                int int32;
                if (failDelete)
                {
                    int32 = 1;
                }
                else
                {
                    int32 = 2;
                }

                expected4 = int32;
            }
            else
            {
                expected4 = 0;
            }

            int actual3 = navigator.DeleteCalls;
            Assert.Equal(expected: expected4, actual: actual3);

            bool CalculateCondition()
            {
                if (!apply)
                {
                    return false;
                }

                if (!failDelete)
                {
                    return true;
                }

                return false;
            }

            bool condition = CalculateCondition();


            int expected5;
            if (condition)
            {
                expected5 = 2;
            }
            else
            {
                expected5 = 4;
            }

            int actual4 = navigator.Lessons.Count;
            Assert.Equal(expected: expected5, actual: actual4);

            Uri CalculateExpected()
            {
                var expected2 = new Uri("https://registry.test/explicit-group");
                return expected2;
            }

            Assert.DoesNotContain(CalculateExpected(), navigator.ReadUris);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CleanupDoesNotReadExplicitDestinationsOrAnotherTeachersLabs(bool wildcard, bool ownLabs)
    {
        await using var fixture = new Fixture(ownLecture: true, ownLabs: ownLabs);
        var navigator = fixture.Navigator(wildcard: wildcard, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        await fixture.Cleanup(
            navigator,
            diagnostics,
            actions =>
            {
                Assert.Fail("An ineligible destination must not produce cleanup actions.");
                return Task.FromResult(true);
            },
            default);
        Assert.Equal(expected: 0, actual: navigator.Reads);
        Assert.Empty(diagnostics.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupSkipsAConfirmedRefusalAndContinuesWithTheNextLab(bool uncertainResponse)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: true,
            combined: false,
            includeExplicitDestination: true,
            includeAllExplicitDestinations: true);
        navigator.AddMixedLessons();
        navigator.AllowDeletes = true;
        navigator.RefusedLessonId = 2;
        navigator.UncertainRefusalResponse = uncertainResponse;
        await using var provider = new Provider(
            fixture,
            navigator,
            new());
        using var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var exit = await new Commands(provider).CleanupIntakeLabs(
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
                });
            Assert.Equal(expected: 0, actual: exit);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var actions = result.RootElement.GetProperty("data")
                .GetProperty("outcomes")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(expected: "omitted",
                actual: actions[0].GetProperty("status")
                    .GetString());
            Assert.Contains(expectedSubstring: "refused",
                actualString: actions[0].GetProperty("detail")
                    .GetString());
            Assert.Equal(expected: "completed",
                actual: actions[1].GetProperty("status")
                    .GetString());
            Assert.Equal(expected: 2, actual: navigator.DeleteCalls);
            Assert.Equal(expected: 3, actual: navigator.Lessons.Count);
            Assert.Contains(
                result.RootElement.GetProperty("warnings")
                    .EnumerateArray(),
                warning => warning.GetString()!
                    .Contains("refused", StringComparison.Ordinal));
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task CleanupRefusesToDeleteAColumnChangedAfterPlanning()
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: true,
            combined: false,
            includeExplicitDestination: true,
            includeAllExplicitDestinations: true);
        navigator.AddMixedLessons();
        navigator.AllowDeletes = true;
        await fixture.Cleanup(
            navigator,
            new(),
            async actions =>
            {
                navigator.Lessons[1] = navigator.Lessons[1] with
                {
                    Mark = "am",
                };
                var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(() =>
                    actions[0].Execute(default));
                Assert.False(error.SubmissionStarted);
                Assert.Equal(expected: 0, actual: navigator.DeleteCalls);
                return true;
            },
            default);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitParentDestinationStillPlansOnlyThatParentsLabs(bool combined)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: false, combined: combined);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            explicitApply: true);

        Assert.NotEmpty(plan);
        Assert.All(plan,
            action => Assert.Equal(expected: RegistrySyncActionKind.Create, actual: action.Kind));
        Assert.All(
            plan,
            action =>
            {
                string expected2;
                if (combined)
                {
                    expected2 = "lab (IA2403 (ru) /spec:DezvoltareaAplicatiilor)";
                }
                else
                {
                    expected2 = "lab";
                }

                string? actual2 = action.Topic;
                Assert.Equal(expected: expected2, actual: actual2);
            });
        Assert.All(plan, action => Assert.Equal(expected: "IA2403", actual: action.Groups));
        Assert.All(plan, action => Assert.Equal(expected: DayOfWeek.Tuesday, actual: action.Date.DayOfWeek));
        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedLecturesRemainUsableWhenSeparateLabsBelongToAnotherTeacher(bool ownLecture)
    {
        await using var fixture = new Fixture(ownLecture: ownLecture, ownLabs: false);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            explicitApply: true);

        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unsafe-registry-lab-destination");
        if (ownLecture)
        {
            Assert.NotEmpty(plan);
            Assert.All(
                plan,
                action =>
                    Assert.Equal(expected: "lecture (IA2403 (ru), IA2404 (ru) /spec:DezvoltareaAplicatiilor)",
                        actual: action.Topic));
        }
        else
        {
            Assert.Empty(plan);
            Assert.Contains(diagnostics.Items, item => item.Kind == "registry-assignment-mismatch");
            Assert.Equal(expected: 0, actual: navigator.Reads);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanupPreservesAnIntakeListStillNeededForFallbackLabs(bool partiallySpecific)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: true,
            combined: false,
            includeExplicitDestination: partiallySpecific);
        navigator.AddMixedLessons();
        var diagnostics = new RegistrySyncDiagnostics();
        await fixture.Cleanup(
            navigator,
            diagnostics,
            _ => throw new InvalidOperationException("Unexpected cleanup"),
            default);
        Assert.Equal(expected: 0, actual: navigator.Reads);
        Assert.Equal(expected: 0, actual: navigator.DeleteCalls);
        Assert.Equal(expected: 4, actual: navigator.Lessons.Count);
        Assert.Contains(diagnostics.Items, item => item.Kind == "registry-lab-fallback-preserved");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FallbackUsesIndependentTopicAndAttendanceSequencesAndPreservesOtherAudienceMarks(
        bool existingOtherMark)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        navigator.UseSyntheticRoster = true;
        navigator.OtherAudienceMark = existingOtherMark ? "9" : "";
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            fixture.Attendance(),
            new IndexedTopics());
        var secondParent = plan.Where(action => action.Topic!.Contains("IA2404", StringComparison.Ordinal))
            .OrderBy(action => action.Date)
            .ToArray();
        Assert.StartsWith(expectedStartString: "Topic 1 (", actualString: secondParent[0].Topic);
        Assert.StartsWith(expectedStartString: "Topic 2 (", actualString: secondParent[1].Topic);
        await secondParent[0].Execute(default);

        string CalculateText()
        {
            if (existingOtherMark)
            {
                return "9";
            }

            return "";
        }

        string[] expected2 = new[]
        {
            CalculateText(),
            "am",
        };
        string[] actual2 = Assert.Single(navigator.SubmittedMarks);
        Assert.Equal(
            expected: expected2,
            actual: actual2);
        Assert.DoesNotContain(diagnostics.Items, item => item.Kind == "unmatched-attendance-students");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedAndSoloLectureLabelsRetainOneParentSequenceAndAssessmentRules(bool assessments)
    {
        await using var fixture = new Fixture(
            ownLecture: true,
            ownLabs: false,
            separateLectureSources: true);
        if (assessments)
        {
            fixture.ConfigureLectureAssessments();
        }

        var navigator = fixture.Navigator(wildcard: true, combined: false);
        var plan = await fixture.Plan(
            navigator,
            new(),
            true,
            topics: new IndexedTopics());
        var firstTuesday = plan.Where(action => action.Date.DayOfWeek == DayOfWeek.Tuesday)
            .OrderBy(action => action.Date)
            .First();
        var firstWednesday = plan.Where(action => action.Date.DayOfWeek == DayOfWeek.Wednesday)
            .OrderBy(action => action.Date)
            .First();
        string expectedStartString2;
        if (assessments)
        {
            expectedStartString2 = "Atestare 1 (";
        }
        else
        {
            expectedStartString2 = "Topic 1 (";
        }

        string? actualString2 = firstTuesday.Topic;
        Assert.StartsWith(expectedStartString: expectedStartString2, actualString: actualString2);
        string expectedStartString3;
        if (assessments)
        {
            expectedStartString3 = "Atestare 2 (";
        }
        else
        {
            expectedStartString3 = "Topic 2 (";
        }

        string? actualString3 = firstWednesday.Topic;
        Assert.StartsWith(expectedStartString: expectedStartString3, actualString: actualString3);
        Assert.Contains(expectedSubstring: "IA2403", actualString: firstTuesday.Topic);
        Assert.Contains(expectedSubstring: "IA2404", actualString: firstTuesday.Topic);
        Assert.DoesNotContain(expectedSubstring: "IA2404", actualString: firstWednesday.Topic);
        Assert.Single(plan.Where(action => action.Date == firstTuesday.Date));
    }

    [Fact]
    public async Task AmbiguousAssessmentProtectsItsOrdinaryAudienceRowsEvenUnderDeletePolicy()
    {
        await using var fixture = new Fixture(
            ownLecture: false,
            ownLabs: true,
            sameDayLabs: true);
        fixture.ConfigureDeletePolicy();
        var navigator = fixture.Navigator(wildcard: true, combined: false);
        navigator.Lessons.AddRange(
        [
            new(
                1,
                Date: "01.09.2026 00:00",
                ShortDate: "01.09.26 00:00",
                Mode: "laborator",
                Topic: "Atestare 1",
                Mark: "a"),
            new(
                2,
                Date: "01.09.2026 09:45",
                ShortDate: "01.09.26 09:45",
                Mode: "laborator",
                Topic: "Existing lab",
                Mark: "am"),
        ]);
        var diagnostics = new RegistrySyncDiagnostics();
        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true);
        Assert.Empty(plan);
        Assert.Contains(diagnostics.Items, item => item.Kind == "ambiguous-registry-assessment");
        Assert.Equal(expected: 0, actual: navigator.DeleteCalls);
    }

    private sealed class IndexedTopics : ILessonTopics
    {
        public string? Get(in AttendanceLookupKey key) => "Topic " + (key.DayIndex + 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdditionalCoursesRostersShareSourceAndReceiveTheirOwnAttendance(bool overlap)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: false,
            combined: false,
            includeAdditionalCourses: true);
        navigator.UseSyntheticRoster = true;
        navigator.Rosters[navigator.Group.Uri] = overlap ? ["Synthetic Alpha", "Synthetic Beta"] : ["Synthetic Alpha"];
        navigator.Rosters[new("https://registry.test/se-group")] =
            overlap ? ["Synthetic Alpha", "Synthetic Beta"] : ["Synthetic Beta"];
        var diagnostics = new RegistrySyncDiagnostics();

        StudentAttendanceList CalculateAttendance()
        {
            var attendance = fixture.Attendance(sameAudience: true);
            return attendance;
        }

        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            CalculateAttendance());
        Assert.DoesNotContain(
            diagnostics.Items,
            item => item.Kind is "ambiguous-registry-destination"
                or "unmatched-attendance-students"
                or "attendance-students-not-in-registry");
        var first = plan.Where(action => action.Destination == navigator.Group.Uri.ToString())
            .OrderBy(action => action.Date)
            .First();
        var se = plan.Where(action => action.Destination == "https://registry.test/se-group")
            .OrderBy(action => action.Date)
            .First();
        Assert.Equal(expected: first.Date, actual: se.Date);
        Assert.Equal(expected: first.Topic, actual: se.Topic);
        await first.Execute(default);
        await se.Execute(default);
        string[] expected2;
        if (overlap)
        {
            expected2 = new[]
            {
                "a",
                "am",
            };
        }
        else
        {
            expected2 = new[]
            {
                "a",
            };
        }

        string[] actual2 = navigator.SubmittedMarks[0];
        Assert.Equal(
            expected: expected2,
            actual: actual2);
        string[] expected3;
        if (overlap)
        {
            expected3 = new[]
            {
                "a",
                "am",
            };
        }
        else
        {
            expected3 = new[]
            {
                "am",
            };
        }

        string[] actual3 = navigator.SubmittedMarks[1];
        Assert.Equal(
            expected: expected3,
            actual: actual3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteCourseDoesNotReportStudentsAbsentFromUnscannedAdditionalRoster(bool failRead)
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: false,
            combined: false,
            includeAdditionalCourses: true);
        navigator.Rosters[navigator.Group.Uri] = ["Synthetic Alpha"];
        navigator.Rosters[new("https://registry.test/se-group")] = ["Synthetic Beta"];
        if (failRead)
        {
            navigator.FailReadUri = new("https://registry.test/se-group");
        }

        var diagnostics = new RegistrySyncDiagnostics();

        StudentAttendanceList CalculateAttendance()
        {
            var attendance = fixture.Attendance(sameAudience: true);
            return attendance;
        }

        var task = fixture.PlanIncrementally(
            navigator,
            diagnostics,
            CalculateAttendance(),
            _ => Task.FromResult(failRead));
        if (failRead)
        {
            await Assert.ThrowsAsync<IOException>(() => task);
        }
        else
        {
            await task;
        }

        Assert.DoesNotContain(
            diagnostics.Items,
            item => item.Kind is "unmatched-attendance-students" or "attendance-students-not-in-registry");
        if (!failRead)
        {
            Uri CalculateExpected()
            {
                var expected = new Uri("https://registry.test/se-group");
                return expected;
            }

            Assert.DoesNotContain(CalculateExpected(), navigator.ReadUris);
        }
    }

    [Fact]
    public async Task OrdinaryCoverageCannotAuthorizeCleanupOfUnresolvedAdditionalCoursesLabs()
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: false,
            combined: false,
            includeAllExplicitDestinations: true);
        var broad = fixture.Navigator(wildcard: true, combined: false).Group;

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/se-fallback");
            return uri2;
        }

        GroupLink CalculateItem()
        {
            GroupLink item2 = new(
                broad.Groups,
                broad.GroupPartition,
                uri: CalculateUri(),
                evaluationUri: broad.EvaluationUri,
                isAdditionalCourses: true);
            return item2;
        }

        navigator.ExtraGroups.Add(CalculateItem());
        for (var parentIndex = 0; parentIndex < broad.Groups.Value.Count; parentIndex++)
        {
            for (var duplicate = 0; duplicate < 2; duplicate++)
            {
                Uri CalculateUri2()
                {
                    string uriString = $"https://registry.test/se-{parentIndex}-{duplicate}";
                    Uri uri3 = new(uriString);
                    return uri3;
                }

                navigator.ExtraGroups.Add(new(
                    new FoundGroups
                    {
                        Value = [broad.Groups.Value[parentIndex]],
                        IsWildcard = false,
                    },
                    broad.GroupPartition,
                    uri: CalculateUri2(),
                    evaluationUri: broad.EvaluationUri,
                    isAdditionalCourses: true));
            }
        }

        var diagnostics = new RegistrySyncDiagnostics();
        await fixture.Cleanup(
            navigator,
            diagnostics,
            _ => throw new InvalidOperationException("Unexpected cleanup"),
            default);
        Assert.Equal(expected: 0, actual: navigator.Reads);
        Assert.Contains(diagnostics.Items, item => item.Kind == "registry-lab-routing-unresolved");
    }

    [Fact]
    public async Task UnresolvedRosterVariantDefersCourseStudentAbsenceReports()
    {
        await using var fixture = new Fixture(ownLecture: false, ownLabs: true);
        var navigator = fixture.Navigator(
            wildcard: false,
            combined: false,
            includeAdditionalCourses: true);
        var ordinary = navigator.Group;

        Uri CalculateUri()
        {
            Uri uri2 = new("https://registry.test/ordinary-duplicate");
            return uri2;
        }

        GroupLink CalculateItem()
        {
            GroupLink item2 = new(
                ordinary.Groups,
                ordinary.GroupPartition,
                uri: CalculateUri(),
                evaluationUri: ordinary.EvaluationUri);
            return item2;
        }

        navigator.ExtraGroups.Add(CalculateItem());
        navigator.Rosters[new("https://registry.test/se-group")] = ["Synthetic Alpha"];
        var diagnostics = new RegistrySyncDiagnostics();

        StudentAttendanceList CalculateAttendance()
        {
            var attendance = fixture.Attendance(sameAudience: true);
            return attendance;
        }

        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            CalculateAttendance());
        Assert.NotEmpty(plan);
        Assert.Contains(diagnostics.Items, item => item.Kind == "ambiguous-registry-destination");
        Assert.DoesNotContain(
            diagnostics.Items,
            item => item.Kind is "unmatched-attendance-students" or "attendance-students-not-in-registry");
        Assert.DoesNotContain(ordinary.Uri, navigator.ReadUris);

        // Reusing a diagnostic collector must not make a repeated unresolved route look complete.
        StudentAttendanceList CalculateAttendance2()
        {
            var attendance2 = fixture.Attendance(sameAudience: true);
            return attendance2;
        }

        _ = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            CalculateAttendance2());
        Assert.DoesNotContain(
            diagnostics.Items,
            item => item.Kind is "unmatched-attendance-students" or "attendance-students-not-in-registry");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;
        private readonly CourseId _course;
        private readonly LessonGroups _parents;
        private readonly GroupPartitionKey _partition = new(SubGroup.All, Specialization.DezvoltareaAplicatiilor);

        public Fixture(
            bool ownLecture,
            bool ownLabs,
            bool separateLectureSources = false,
            bool sameDayLabs = false)
        {
            var builder = new ScheduleBuilder();
            builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            builder.EnableLookupModule();
            _course = builder.Course("Synthetic elective");
            _parents = [builder.Group("IA2403(ru)"), builder.Group("IA2404(ru)")];
            var teacher = builder.Teacher("Anton Curmanschii");
            var other = builder.Teacher("Another Lecturer");
            builder.RegularLesson(lesson =>
            {
                lesson.Course(_course);
                if (separateLectureSources)
                {
                    lesson.Group(_parents[0]);
                }
                else
                {
                    lesson.Groups([.. _parents]);
                }

                lesson.Specialization(_partition.Specialization);
                lesson.Type(LessonType.Curs);
                lesson.DayOfWeek(DayOfWeek.Tuesday);
                lesson.TimeSlot(TimeSlot.First);
                var lessonBuilder = lesson;
                TeacherBuilder teacher2;
                if (ownLecture)
                {
                    teacher2 = teacher;
                }
                else
                {
                    teacher2 = other;
                }

                TeacherId teacher3 = teacher2;
                lessonBuilder.Teacher(teacher3);
            });
            foreach (var group in _parents)
            {
                builder.RegularLesson(lesson =>
                {
                    lesson.Course(_course);
                    lesson.Group(group);
                    lesson.Specialization(_partition.Specialization);
                    lesson.Type(LessonType.Lab);
                    var lessonBuilder = lesson;

                    bool CalculateCondition()
                    {
                        if (group == _parents[0])
                        {
                            return true;
                        }
                        return sameDayLabs;
                    }

                    bool condition = CalculateCondition();


                    DayOfWeek dayOfWeek;
                    if (condition)
                    {
                        dayOfWeek = DayOfWeek.Tuesday;
                    }
                    else
                    {
                        dayOfWeek = DayOfWeek.Friday;
                    }

                    lessonBuilder.DayOfWeek(dayOfWeek);
                    var lessonBuilder2 = lesson;

                    bool CalculateCondition2()
                    {
                        if (!sameDayLabs)
                        {
                            return false;
                        }

                        if (group == _parents[1])
                        {
                            return true;
                        }

                        return false;
                    }

                    bool condition2 = CalculateCondition2();


                    int index;
                    if (condition2)
                    {
                        index = 2;
                    }
                    else
                    {
                        index = 1;
                    }

                    TimeSlot timeSlot = new(index);
                    lessonBuilder2.TimeSlot(timeSlot);
                    var lessonBuilder3 = lesson;
                    TeacherBuilder teacher2;
                    if (ownLabs)
                    {
                        teacher2 = teacher;
                    }
                    else
                    {
                        teacher2 = other;
                    }

                    TeacherId teacher3 = teacher2;
                    lessonBuilder3.Teacher(teacher3);
                });
            }
            if (separateLectureSources)
            {
                foreach (var (group, day) in new[]
                         {
                             (Group: _parents[1], Day: DayOfWeek.Tuesday),
                             (Group: _parents[0], Day: DayOfWeek.Wednesday),
                         })
                {
                    builder.RegularLesson(lesson =>
                    {
                        lesson.Course(_course);
                        lesson.Group(group);
                        lesson.Specialization(_partition.Specialization);
                        lesson.Type(LessonType.Curs);
                        lesson.DayOfWeek(day);
                        lesson.TimeSlot(TimeSlot.First);
                        lesson.Teacher(teacher);
                    });
                }
            }
            var schedule = builder.Build();
            builder.RefreshLookup();
            var services = new ServiceCollection();
            AppConfiguration.ConfigureServices(services);
            services.AddSingleton(builder);
            services.AddSingleton(schedule);
            _provider = AppConfiguration.BuildServiceProvider(services);
            AppConfiguration.ConfigureLayeredConfig(_provider);
            _scope = _provider.CreateMarkerScope(marker =>
            {
                var teacherLayerConfig = marker;
                var parse = NameHelper.Parse("Curmanschii Anton");
                teacherLayerConfig.TeacherName = parse;
            });
        }

        public Navigator Navigator(
            bool wildcard,
            bool combined,
            bool includeExplicitDestination = false,
            bool includeAllExplicitDestinations = false,
            bool includeAdditionalCourses = false)
        {
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

            var group = new GroupLink(
                new FoundGroups
                {
                    Value = wildcard ? _parents : [_parents[0]],
                    IsWildcard = wildcard,
                    IsCombined = combined,
                },
                _partition,
                uri: CalculateUri(),
                evaluationUri: CalculateEvaluationUri());

            Uri CalculateUri2()
            {
                Uri uri3 = new("https://registry.test/explicit-group");
                return uri3;
            }

            Uri CalculateEvaluationUri2()
            {
                Uri evaluationUri3 = new("https://registry.test/explicit-evaluation");
                return evaluationUri3;
            }

            GroupLink[] additional = includeExplicitDestination
                ?
                [
                    new(
                        new FoundGroups
                        {
                            Value = [_parents[0]],
                            IsWildcard = false,
                        },
                        _partition,
                        uri: CalculateUri2(),
                        evaluationUri: CalculateEvaluationUri2()),
                ]
                : [];
            if (includeAllExplicitDestinations)
            {
                Uri CalculateUri3()
                {
                    Uri uri4 = new("https://registry.test/other-explicit-group");
                    return uri4;
                }

                Uri CalculateEvaluationUri3()
                {
                    Uri evaluationUri4 = new("https://registry.test/other-explicit-evaluation");
                    return evaluationUri4;
                }

                additional =
                [
                    .. additional, new(
                        new FoundGroups
                        {
                            Value = [_parents[1]],
                            IsWildcard = false,
                        },
                        _partition,
                        uri: CalculateUri3(),
                        evaluationUri: CalculateEvaluationUri3()),
                ];
            }

            if (includeAdditionalCourses)
            {
                Uri CalculateUri4()
                {
                    Uri uri5 = new("https://registry.test/se-group");
                    return uri5;
                }

                Uri CalculateEvaluationUri4()
                {
                    Uri evaluationUri5 = new("https://registry.test/se-evaluation");
                    return evaluationUri5;
                }

                additional =
                [
                    .. additional, new(
                        group.Groups,
                        group.GroupPartition,
                        uri: CalculateUri4(),
                        evaluationUri: CalculateEvaluationUri4(),
                        isAdditionalCourses: true),
                ];
            }

            return new(
                _course,
                group,
                additional);
        }

        public Task<IReadOnlyList<RegistrySyncAction>> Plan(
            Navigator navigator,
            RegistrySyncDiagnostics diagnostics,
            bool explicitApply,
            StudentAttendanceList? attendance = null,
            ILessonTopics? topics = null)
        {
            return _scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
                .Plan(
                    new()
                    {
                        Navigator = navigator,
                        Attendance = attendance ?? new([]),
                        HasAttendanceSources = attendance.HasValue,
                        LessonTopics = topics ?? new Topics(),
                        Semester = Semester.Sem1,
                        Diagnostics = diagnostics,
                        LessonFilter =
                            _scope.ServiceProvider.GetRequiredService<CurrentTeacherLessonFilterProvider>()
                                .Get(),
                    },
                    explicitApply: explicitApply);
        }

        public Task PlanIncrementally(
            Navigator navigator,
            RegistrySyncDiagnostics diagnostics,
            StudentAttendanceList attendance,
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> callback)
        {
            return _scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
                .PlanGroups(
                    new()
                    {
                        Navigator = navigator,
                        Attendance = attendance,
                        HasAttendanceSources = true,
                        LessonTopics = new Topics(),
                        Semester = Semester.Sem1,
                        Diagnostics = diagnostics,
                        LessonFilter =
                            _scope.ServiceProvider.GetRequiredService<CurrentTeacherLessonFilterProvider>()
                                .Get(),
                    },
                    callback);
        }

        public void ConfigureDeletePolicy()
        {
            _provider.GetRequiredService<TreeBuilder>().Defaults
                .Builder<RegistryConfig>()
                .ConfigureValue(config =>
                {
                    var registryConfig = config;
                    registryConfig.ExtraLessonInstanceAction = ExtraLessonInstanceAction.Delete;
                });
            Assert.Equal(
                expected: ExtraLessonInstanceAction.Delete,
                actual: _scope.ServiceProvider
                    .GetRequiredService<Anton.LayeredData.Retrieval.DataProvider<BuiltRegistryConfig>>()
                    .Get()!
                    .ExtraLessonInstanceAction);
        }

        public void ConfigureLectureAssessments()
        {
            _provider.GetRequiredService<TreeBuilder>().Defaults
                .Builder<RegistryConfig>()
                .ConfigureValue(config =>
                    config.Assessments =
                    [
                        new()
                        {
                            Course = "Synthetic elective",
                            Group = "IA2403",
                            Specialization = _partition.Specialization.Value,
                            LessonType = LessonType.Curs,
                            Dates =
                            [
                                new(
                                    year: 2026,
                                    month: 9,
                                    day: 1),
                                new(
                                    year: 2026,
                                    month: 9,
                                    day: 2),
                            ],
                        },
                    ]);
            Assert.Single(
                _scope.ServiceProvider
                    .GetRequiredService<Anton.LayeredData.Retrieval.DataProvider<BuiltRegistryConfig>>()
                    .Get()!
                    .Assessments);
        }

        public StudentAttendanceList Attendance(bool sameAudience = false)
        {
            var builder = new AllStudentAttendanceListBuilder();
            var first = builder.List(new(
                [_parents[0]],
                _partition,
                _course,
                LessonType.Lab));

            Name CalculateName()
            {
                var name = NameHelper.Parse("Synthetic Alpha");
                return name;
            }

            var alpha = first.Student(CalculateName());
            alpha.Day(ScheduleLib.OnlineRegistry.Attendance.NotPresent);
            alpha.Day(ScheduleLib.OnlineRegistry.Attendance.Present);
            var second = sameAudience
                ? first
                : builder.List(new(
                    [_parents[1]],
                    _partition,
                    _course,
                    LessonType.Lab));

            Name CalculateName2()
            {
                var name2 = NameHelper.Parse("Synthetic Beta");
                return name2;
            }

            var beta = second.Student(CalculateName2());
            beta.Day(ScheduleLib.OnlineRegistry.Attendance.MotivatedAbsent);
            beta.Day(ScheduleLib.OnlineRegistry.Attendance.NotPresent);
            return builder.Build(ScheduleLib.OnlineRegistry.Attendance.None);
        }

        public Task Cleanup(
            Navigator navigator,
            RegistrySyncDiagnostics diagnostics,
            Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
            CancellationToken token)
        {
            return _scope.ServiceProvider.GetRequiredService<AddLessonsToOnlineRegistryTaskHandler>()
                .PlanIntakeLabCleanup(
                    navigator,
                    Semester.Sem1,
                    _scope.ServiceProvider.GetRequiredService<CurrentTeacherLessonFilterProvider>()
                        .Get(),
                    diagnostics,
                    onGroupPlanned,
                    token);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
        }
    }

    private sealed class Topics : ILessonTopics
    {
        public string? Get(in AttendanceLookupKey key) => key.LessonType == LessonType.Lab ? "lab" : "lecture";
    }

    private sealed class Navigator(CourseId course, GroupLink group, params GroupLink[] additionalGroups)
        : IRegistrySyncNavigator
    {
        public sealed record Lesson(
            int Id,
            string Date,
            string ShortDate,
            string Mode,
            string Topic,
            string Mark);

        public List<Lesson> Lessons { get; } = [];

        public bool UseSyntheticRoster { get; set; }

        public Dictionary<Uri, string[]> Rosters { get; } = [];

        public List<GroupLink> ExtraGroups { get; } = [];

        public Uri? FailReadUri { get; set; }

        public string OtherAudienceMark { get; set; } = "";

        public List<string[]> SubmittedMarks { get; } = [];

        public bool AllowDeletes { get; set; }

        public bool FailDelete { get; set; }

        public int? RefusedLessonId { get; set; }

        public bool UncertainRefusalResponse { get; set; }

        public bool UncertainSuccessfulResponse { get; set; }

        public int DeleteCalls { get; private set; }

        public void AddMixedLessons()
        {
            Lessons.AddRange(
            [
                new(
                    1,
                    Date: "01.09.2026 15:00",
                    ShortDate: "01.09.26 15:00",
                    Mode: "curs",
                    Topic: "lecture",
                    Mark: ""),
                new(
                    2,
                    Date: "04.09.2026 15:00",
                    ShortDate: "04.09.26 15:00",
                    Mode: "laborator",
                    Topic: "lab one",
                    Mark: "a"),
                new(
                    3,
                    Date: "08.09.2026 15:00",
                    ShortDate: "08.09.26 15:00",
                    Mode: "seminar",
                    Topic: "seminar",
                    Mark: "am"),
                new(
                    4,
                    Date: "11.09.2026 15:00",
                    ShortDate: "11.09.26 15:00",
                    Mode: "laborator",
                    Topic: "lab two",
                    Mark: ""),
            ]);
        }

        public GroupLink Group => group;

        public int Reads { get; private set; }

        public List<Uri> ReadUris { get; } = [];

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
            => Task.FromResult<IEnumerable<GroupLink>>([group, .. additionalGroups, .. ExtraGroups]);

        public async Task<IDocument> GetHtml(Uri uri)
        {
            Reads++;
            ReadUris.Add(uri);
            if (uri == FailReadUri)
            {
                throw new IOException("Synthetic roster read failure.");
            }

            if (Rosters.TryGetValue(uri, out _))
            {
                string CalculateSource()
                {
                    string escapeDataString = Uri.EscapeDataString(uri.ToString());
                    string source =
                        $"<div><a href='https://registry.test/roster-add?list={escapeDataString}'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>";
                    return source;
                }

                return await new HtmlParser().ParseDocumentAsync(
                    CalculateSource());
            }

            if (uri.AbsolutePath == "/roster-add")
            {
                string CalculateUriString()
                {
                    string uriString = Uri.UnescapeDataString(uri.Query[6..]);
                    return uriString;
                }

                var roster = Rosters[new Uri(CalculateUriString())];
                var rows = string.Join(
                    "",
                    roster.Select((name, index) =>
                        $"<tr><td>{index + 1}</td><td>{name}</td><td>spec</td><td><input name='student-{index}'></td></tr>"));
                return await new HtmlParser().ParseDocumentAsync(
                    $"<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option><option value='curs'>Curs</option></select><textarea id='LessonTopic'></textarea><table><tr><th>NN</th><th>Name</th><th>Spec</th><th>frecvența/nota</th></tr>{rows}</table></form>");
            }

            bool CheckCondition()
            {
                if (!FailDelete)
                {
                    return false;
                }

                if (DeleteCalls == 0)
                {
                    return false;
                }

                if (uri == group.Uri)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                throw new IOException("Synthetic confirmation read failure.");
            }

            if (Lessons.Count != 0 && uri == group.Uri)
            {
                var rows = string.Join(
                    "",
                    Lessons.Select(lesson =>
                        $"<tr><td><a href='https://registry.test/details/{lesson.Id}'>{lesson.Date}</a> {lesson.Mode}</td><td>1 {lesson.Topic}</td><td><a href='https://registry.test/edit/{lesson.Id}'>edit</a></td></tr>"));
                var headers = string.Join("", Lessons.Select(lesson => $"<th>{lesson.ShortDate}, {lesson.Mode}</th>"));
                var marks = string.Join("", Lessons.Select(lesson => $"<td>{lesson.Mark}</td>"));
                return await new HtmlParser().ParseDocumentAsync(
                    $"<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr>{rows}</table><table><tr><th>NN</th><th>Name</th><th>Spec</th>{headers}</tr><tr><td>1</td><td>Synthetic Student</td><td>spec</td>{marks}</tr></table>");
            }

            bool CheckCondition2()
            {
                if (!UseSyntheticRoster)
                {
                    return false;
                }

                if (uri.AbsolutePath == "/add")
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition2())
            {
                return await new HtmlParser().ParseDocumentAsync(
                    $"<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option><option value='curs'>Curs</option></select><textarea id='LessonTopic'></textarea><table><tr><th>NN</th><th>Name</th><th>Spec</th><th>frecvența/nota</th></tr><tr><td>1</td><td>Synthetic Alpha</td><td>spec</td><td><input name='alpha' value='{OtherAudienceMark}'></td></tr><tr><td>2</td><td>Synthetic Beta</td><td>spec</td><td><input name='beta'></td></tr></table></form>");
            }

            if (uri.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal))
            {
                string CalculateSource2()
                {
                    string source2 = $"<input name='id' value='{uri.Segments[^1]}'>";
                    return source2;
                }

                return await new HtmlParser().ParseDocumentAsync(CalculateSource2());
            }

            return await new HtmlParser().ParseDocumentAsync(
                uri == group.Uri || additionalGroups.Any(item => item.Uri == uri)
                    ? "<div><a href='https://registry.test/add'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>"
                    : "<html></html>");
        }

        public Task SubmitLesson(IDocument document)
        {
            if (!UseSyntheticRoster)
            {
                throw new InvalidOperationException("Unexpected submission.");
            }

            if (document.QuerySelector("input[name^=student-]") is not null)
            {
                var list = SubmittedMarks;
                var querySelectorAll = document.QuerySelectorAll<IHtmlInputElement>("input[name^=student-]");
                Func<IHtmlInputElement, string> selector = input => input.Value;
                var select = querySelectorAll.Select(selector);
                string[] item = select.ToArray();
                list.Add(
                    item);
                return Task.CompletedTask;
            }
            SubmittedMarks.Add(
            [
                document.QuerySelector<IHtmlInputElement>("input[name=alpha]")!.Value,
                document.QuerySelector<IHtmlInputElement>("input[name=beta]")!.Value,
            ]);
            return Task.CompletedTask;
        }

        public Task SubmitDelete(IDocument document)
        {
            if (!AllowDeletes)
            {
                throw new InvalidOperationException("Unexpected deletion.");
            }

            DeleteCalls++;
            if (FailDelete)
            {
                throw new IOException("Synthetic uncertain deletion.");
            }

            var inputValue = document.QuerySelector("input")!
                .GetAttribute("value")!;
            var id = int.Parse(
                inputValue,
                System.Globalization.CultureInfo.InvariantCulture);
            if (id == RefusedLessonId)
            {
                if (UncertainRefusalResponse)
                {
                    throw new IOException("Synthetic unconfirmed response.");
                }

                return Task.CompletedTask;
            }
            Lessons.RemoveAll(lesson => lesson.Id == id);
            if (UncertainSuccessfulResponse)
            {
                throw new IOException("Synthetic unconfirmed successful deletion.");
            }

            return Task.CompletedTask;
        }
    }
}
