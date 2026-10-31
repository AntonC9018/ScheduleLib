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
            Assert.Equal(
                expected: "preview",
                actual: result.RootElement.GetProperty("status")
                    .GetString());
            Assert.Equal(
                expected: 0,
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
        Assert.All(
            fallback,
            action => Assert.Equal(expected: "lab (IA2404 (ru) /spec:DezvoltareaAplicatiilor)", actual: action.Topic));
        Assert.All(fallback, action => Assert.Equal(expected: DayOfWeek.Friday, actual: action.Date.DayOfWeek));
        Assert.Contains(navigator.Group.Uri, navigator.ReadUris);

        Assert.Contains(new Uri("https://registry.test/explicit-group"), navigator.ReadUris);
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
            Assert.Equal(expected: failDelete ? 6 : 0, actual: exit);
            using var result = System.Text.Json.JsonDocument.Parse(output.ToString());
            var outcomes = result.RootElement.GetProperty("data")
                .GetProperty("outcomes")
                .EnumerateArray()
                .ToArray();
            Assert.Equal(expected: 2, actual: outcomes.Length);
            Assert.All(
                outcomes,
                action => Assert.Equal(
                    expected: "delete",
                    actual: action.GetProperty("kind")
                        .GetString()));
            Assert.All(
                outcomes,
                action => Assert.Equal(
                    expected: navigator.Group.Uri.ToString(),
                    actual: action.GetProperty("destination")
                        .GetString()));
            Assert.Equal(
                expected: 1,
                actual: outcomes[0].GetProperty("attendanceCount")
                    .GetInt32());

            string ExpectedFirstOutcomeStatus()
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
                expected: ExpectedFirstOutcomeStatus(),
                actual: outcomes[0].GetProperty("status")
                    .GetString());

            string ExpectedSecondOutcomeStatus()
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
                expected: ExpectedSecondOutcomeStatus(),
                actual: outcomes[1].GetProperty("status")
                    .GetString());

            int ExpectedDeleteCalls()
            {
                if (!apply)
                {
                    return 0;
                }

                if (failDelete)
                {
                    return 1;
                }

                return 2;
            }

            Assert.Equal(expected: ExpectedDeleteCalls(), actual: navigator.DeleteCalls);
            Assert.Equal(expected: apply && !failDelete ? 2 : 4, actual: navigator.Lessons.Count);

            Assert.DoesNotContain(new Uri("https://registry.test/explicit-group"), navigator.ReadUris);
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
            Assert.Equal(
                expected: "omitted",
                actual: actions[0].GetProperty("status")
                    .GetString());
            Assert.Contains(
                expectedSubstring: "refused",
                actualString: actions[0].GetProperty("detail")
                    .GetString());
            Assert.Equal(
                expected: "completed",
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
        Assert.All(
            plan,
            action => Assert.Equal(expected: RegistrySyncActionKind.Create, actual: action.Kind));
        Assert.All(
            plan,
            action => Assert.Equal(
                expected: combined ? "lab (IA2403 (ru) /spec:DezvoltareaAplicatiilor)" : "lab",
                actual: action.Topic));
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
                    Assert.Equal(
                        expected: "lecture (IA2403 (ru), IA2404 (ru) /spec:DezvoltareaAplicatiilor)",
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

        Assert.Equal(
            expected: new[]
            {
                existingOtherMark ? "9" : "",
                "am",
            },
            actual: Assert.Single(navigator.SubmittedMarks));
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
        Assert.StartsWith(
            expectedStartString: assessments ? "Atestare 1 (" : "Topic 1 (",
            actualString: firstTuesday.Topic);
        Assert.StartsWith(
            expectedStartString: assessments ? "Atestare 2 (" : "Topic 2 (",
            actualString: firstWednesday.Topic);
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

        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            fixture.Attendance(sameAudience: true));
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
        Assert.Equal(
            expected: overlap ? new[] { "a", "am" } : new[] { "a" },
            actual: navigator.SubmittedMarks[0]);
        Assert.Equal(
            expected: overlap ? new[] { "a", "am" } : new[] { "am" },
            actual: navigator.SubmittedMarks[1]);
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

        var task = fixture.PlanIncrementally(
            navigator,
            diagnostics,
            fixture.Attendance(sameAudience: true),
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
            Assert.DoesNotContain(new Uri("https://registry.test/se-group"), navigator.ReadUris);
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

        navigator.ExtraGroups.Add(new GroupLink(
            broad.Groups,
            broad.GroupPartition,
            uri: new Uri("https://registry.test/se-fallback"),
            evaluationUri: broad.EvaluationUri,
            isAdditionalCourses: true));
        for (var parentIndex = 0; parentIndex < broad.Groups.Value.Count; parentIndex++)
        {
            for (var duplicate = 0; duplicate < 2; duplicate++)
            {
                navigator.ExtraGroups.Add(new(
                    new FoundGroups
                    {
                        Value = [broad.Groups.Value[parentIndex]],
                        IsWildcard = false,
                    },
                    broad.GroupPartition,
                    uri: new Uri($"https://registry.test/se-{parentIndex}-{duplicate}"),
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

        navigator.ExtraGroups.Add(new GroupLink(
            ordinary.Groups,
            ordinary.GroupPartition,
            uri: new Uri("https://registry.test/ordinary-duplicate"),
            evaluationUri: ordinary.EvaluationUri));
        navigator.Rosters[new("https://registry.test/se-group")] = ["Synthetic Alpha"];
        var diagnostics = new RegistrySyncDiagnostics();

        var plan = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            fixture.Attendance(sameAudience: true));
        Assert.NotEmpty(plan);
        Assert.Contains(diagnostics.Items, item => item.Kind == "ambiguous-registry-destination");
        Assert.DoesNotContain(
            diagnostics.Items,
            item => item.Kind is "unmatched-attendance-students" or "attendance-students-not-in-registry");
        Assert.DoesNotContain(ordinary.Uri, navigator.ReadUris);

        // Reusing a diagnostic collector must not make a repeated unresolved route look complete.
        _ = await fixture.Plan(
            navigator,
            diagnostics,
            true,
            fixture.Attendance(sameAudience: true));
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
                lesson.Teacher(ownLecture ? teacher : other);
            });
            foreach (var group in _parents)
            {
                builder.RegularLesson(lesson =>
                {
                    lesson.Course(_course);
                    lesson.Group(group);
                    lesson.Specialization(_partition.Specialization);
                    lesson.Type(LessonType.Lab);
                    lesson.DayOfWeek(group == _parents[0] || sameDayLabs ? DayOfWeek.Tuesday : DayOfWeek.Friday);
                    lesson.TimeSlot(new TimeSlot(sameDayLabs && group == _parents[1] ? 2 : 1));
                    lesson.Teacher(ownLabs ? teacher : other);
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
            _scope = _provider.CreateMarkerScope(
                marker => marker.TeacherName = NameHelper.Parse("Curmanschii Anton"));
        }

        public Navigator Navigator(
            bool wildcard,
            bool combined,
            bool includeExplicitDestination = false,
            bool includeAllExplicitDestinations = false,
            bool includeAdditionalCourses = false)
        {
            var groupEvaluationUri = new Uri("https://registry.test/evaluation");

            var group = new GroupLink(
                new FoundGroups
                {
                    Value = wildcard ? _parents : [_parents[0]],
                    IsWildcard = wildcard,
                    IsCombined = combined,
                },
                _partition,
                uri: new Uri("https://registry.test/group"),
                evaluationUri: groupEvaluationUri);

            var explicitEvaluationUri = new Uri("https://registry.test/explicit-evaluation");

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
                        uri: new Uri("https://registry.test/explicit-group"),
                        evaluationUri: explicitEvaluationUri),
                ]
                : [];
            if (includeAllExplicitDestinations)
            {
                var otherExplicitEvaluationUri = new Uri("https://registry.test/other-explicit-evaluation");

                additional =
                [
                    .. additional, new(
                        new FoundGroups
                        {
                            Value = [_parents[1]],
                            IsWildcard = false,
                        },
                        _partition,
                        uri: new Uri("https://registry.test/other-explicit-group"),
                        evaluationUri: otherExplicitEvaluationUri),
                ];
            }

            if (includeAdditionalCourses)
            {
                var additionalCoursesEvaluationUri = new Uri("https://registry.test/se-evaluation");

                additional =
                [
                    .. additional, new(
                        group.Groups,
                        group.GroupPartition,
                        uri: new Uri("https://registry.test/se-group"),
                        evaluationUri: additionalCoursesEvaluationUri,
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
                .ConfigureValue(config => config.ExtraLessonInstanceAction = ExtraLessonInstanceAction.Delete);
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

            var alpha = first.Student(NameHelper.Parse("Synthetic Alpha"));
            alpha.Day(ScheduleLib.OnlineRegistry.Attendance.NotPresent);
            alpha.Day(ScheduleLib.OnlineRegistry.Attendance.Present);
            var second = sameAudience
                ? first
                : builder.List(new(
                    [_parents[1]],
                    _partition,
                    _course,
                    LessonType.Lab));

            var beta = second.Student(NameHelper.Parse("Synthetic Beta"));
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
            => Task.FromResult<IEnumerable<CourseLink>>([new(course, new Uri("https://registry.test/course"))]);

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
                var escapedListUri = Uri.EscapeDataString(uri.ToString());
                return await new HtmlParser().ParseDocumentAsync(
                    $"<div><a href='https://registry.test/roster-add?list={escapedListUri}'>Adaugare</a></div><table><tr><th>lesson</th></tr></table>");
            }

            if (uri.AbsolutePath == "/roster-add")
            {
                var roster = Rosters[new Uri(Uri.UnescapeDataString(uri.Query[6..]))];
                var rows = string.Join(
                    "",
                    roster.Select((name, index) =>
                        $"<tr><td>{index + 1}</td><td>{name}</td><td>spec</td><td><input name='student-{index}'></td></tr>"));
                return await new HtmlParser().ParseDocumentAsync(
                    $"<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option><option value='curs'>Curs</option></select><textarea id='LessonTopic'></textarea><table><tr><th>NN</th><th>Name</th><th>Spec</th><th>frecvența/nota</th></tr>{rows}</table></form>");
            }

            if (FailDelete && DeleteCalls != 0 && uri == group.Uri)
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

            if (UseSyntheticRoster && uri.AbsolutePath == "/add")
            {
                return await new HtmlParser().ParseDocumentAsync(
                    $"<form><input id='LessonDate'><select id='LessonMode'><option value='laborator'>Lab</option><option value='curs'>Curs</option></select><textarea id='LessonTopic'></textarea><table><tr><th>NN</th><th>Name</th><th>Spec</th><th>frecvența/nota</th></tr><tr><td>1</td><td>Synthetic Alpha</td><td>spec</td><td><input name='alpha' value='{OtherAudienceMark}'></td></tr><tr><td>2</td><td>Synthetic Beta</td><td>spec</td><td><input name='beta'></td></tr></table></form>");
            }

            if (uri.AbsolutePath.StartsWith("/details/", StringComparison.Ordinal))
            {
                return await new HtmlParser().ParseDocumentAsync($"<input name='id' value='{uri.Segments[^1]}'>");
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
                SubmittedMarks.Add(
                    document.QuerySelectorAll<IHtmlInputElement>("input[name^=student-]")
                        .Select(input => input.Value)
                        .ToArray());
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
