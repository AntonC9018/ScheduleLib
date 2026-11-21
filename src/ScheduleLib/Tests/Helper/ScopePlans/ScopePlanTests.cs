using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleLib.DependencyInjection.ScopePlans;
using static ScheduleLib.Tests.ScopePlans.ScopePlanFixture;

namespace ScheduleLib.Tests.ScopePlans;

public sealed class ScopePlanTests
{
    [Fact]
    public void PlanListsSeedsAndInitializersInConstructorOrder()
    {
        using var root = CreateServices().Build();
        var plan = root.PlanOf<CalendarSyncHandler>();

        ScopeNode[] seeds = [ScopeNode.Unkeyed(typeof(SettingsArgs)), ScopeNode.Unkeyed(typeof(SourceArgs))];
        Assert.Equal(expected: seeds, actual: plan.Seeds);
        Type[] steps = [typeof(RemoteCalendar), typeof(LoadedSchedule)];
        Assert.Equal(expected: steps, actual: plan.Steps.Select(s => s.Value));
        Assert.Empty(plan.Errors);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void NodesShareClosures()
    {
        using var root = CreateServices().Build();
        var graph = root.GetRequiredService<ScopePlanGraph>();

        var schedule = graph.GetNode(ScopeNode.Unkeyed(typeof(LoadedSchedule)));
        var query = graph.GetNode(ScopeNode.Unkeyed(typeof(LessonQuery)));
        var plan = root.PlanOf<QueryLessonsHandler>();

        Assert.Same(expected: schedule.Closure, actual: query.Closure);
        Assert.Same(expected: schedule.Closure, actual: plan.Closure);
        Assert.Same(expected: plan, actual: root.PlanOf<QueryLessonsHandler>());
    }

    [Fact]
    public async Task OpensScopeWithSeedsAndInitializedValues()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<CalendarSyncHandler>(Supply);

        Assert.Equal(expected: "teacher-of-default:1", actual: scope.Handler.Describe());
        Assert.Equal(expected: ["calendar", "schedule"], actual: root.GetRequiredService<LoadLog>().Order);

        static void Supply(ScopeInputs inputs)
        {
            inputs.Add(Settings).Add(Source);
        }
    }

    [Fact]
    public async Task LoadsOnlyWhatTheWorkflowNeeds()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<ShowSettingsHandler>(Supply);

        Assert.Equal(expected: "teacher-of-default", actual: scope.Handler.Settings.Teacher);
        Assert.Empty(root.GetRequiredService<LoadLog>().Order);

        static void Supply(ScopeInputs inputs)
        {
            inputs.Add(Settings);
        }
    }

    [Fact]
    public async Task MissingSeedsAreRejectedBeforeAnyWork()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<MissingSeedsException>(Open);

        ScopeNode[] expected = [ScopeNode.Unkeyed(typeof(SettingsArgs))];
        Assert.Equal(expected: expected, actual: exception.Seeds);
        Assert.Empty(root.GetRequiredService<LoadLog>().Order);

        async Task Open()
        {
            await invocation.ServiceProvider.OpenAsync<CalendarSyncHandler>(SupplySource);
        }
    }

    [Fact]
    public async Task PlanErrorsAreRejectedBeforeAnyWork()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<ScopePlanErrorsException>(Open);

        var error = Assert.Single(exception.Errors);
        Assert.Equal(expected: ScopePlanIssueKind.MissingRegistration, actual: error.Kind);
        Assert.Equal(expected: ScopeNode.Unkeyed(typeof(BrokenHandler)), actual: error.Subject);
        ScopeNode[] related = [ScopeNode.Unkeyed(typeof(Unregistered))];
        Assert.Equal(expected: related, actual: error.Related);
        Assert.Empty(root.GetRequiredService<LoadLog>().Order);

        async Task Open()
        {
            await invocation.ServiceProvider.OpenAsync<BrokenHandler>(SupplySource);
        }
    }

    [Fact]
    public async Task InitializedValueOutsideAPlannedScopeIsNotAvailable()
    {
        await using var root = CreateServices().Build();
        await using var scope = root.CreateAsyncScope();

        Assert.Throws<ScopeValueNotAvailableException>(() => scope.ServiceProvider.GetRequiredService<LoadedSchedule>());
    }

    [Fact]
    public async Task DisposableValuesAreRejected()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var plan = root.PlanOf<DisposableValueHandler>();

        var error = Assert.Single(plan.Errors);
        Assert.Equal(expected: ScopePlanIssueKind.DisposableValue, actual: error.Kind);
        await Assert.ThrowsAsync<DisposableScopeValueException>(Open);

        async Task Open()
        {
            await invocation.ServiceProvider.OpenAsync<SourceBaseHandler>(SupplyDisposable);
        }

        static void SupplyDisposable(ScopeInputs inputs)
        {
            using var source = new DisposableSource();
            inputs.Add<SourceBase>(source);
        }
    }

    [Fact]
    public async Task KeyedSeedsReachUnkeyedConsumers()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<CompareSemestersHandler>(Supply);

        Assert.Equal(expected: "data/2024-sem2->data/2025-sem1", actual: scope.Handler.Describe());

        static void Supply(ScopeInputs inputs)
        {
            var previous = new SourceArgs("data/2024-sem2");
            inputs.Add(Source).AddKeyed(key: "previous", value: previous);
        }
    }

    [Fact]
    public void ServiceLocatorsAreWarningsAndHiddenNeedsArePlanned()
    {
        using var root = CreateServices().Build();
        var plan = root.PlanOf<LegacyHandler>();

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(expected: ScopePlanIssueKind.ServiceLocator, actual: warning.Kind);
        Assert.Equal(expected: ScopeNode.Unkeyed(typeof(LegacyPlanner)), actual: warning.Subject);
        Assert.Equal(expected: [typeof(LoadedSchedule)], actual: plan.Steps.Select(s => s.Value));
        Assert.True(plan.Closure.IsOpaque);
    }

    [Theory]
    [InlineData(typeof(LambdaHandler))]
    [InlineData(typeof(LambdaCollectionHandler))]
    public void LambdaRegistrationsAreWarningsAndTheirHiddenNeedsArePlanned(Type handler)
    {
        using var root = CreateServices().Build();
        var plan = root.GetRequiredService<ScopePlanGraph>()
            .GetPlan(handler);

        var warning = Assert.Single(plan.Warnings);
        Assert.Equal(expected: ScopePlanIssueKind.LambdaRegistration, actual: warning.Kind);
        Assert.Equal(expected: [typeof(LoadedSchedule)], actual: plan.Steps.Select(s => s.Value));
    }

    [Fact]
    public void PlanStopsAtSingletonsAndFollowsOpenGenerics()
    {
        using var root = CreateServices().Build();

        var singletonUser = root.PlanOf<SingletonUserHandler>();
        var wrapper = root.PlanOf<WrapperHandler>();

        Assert.Empty(singletonUser.Warnings);
        Assert.Equal(expected: [typeof(LoadedSchedule)], actual: wrapper.Steps.Select(s => s.Value));
        Assert.Empty(wrapper.Errors);
    }

    [Fact]
    public void UnregisteredRootIsPlannedTheSameInEitherOrder()
    {
        using var rootFirst = CreateServices().Build();
        using var consumerFirst = CreateServices().Build();

        var rootPlanFirst = rootFirst.PlanOf<QueryLessonsHandler>();
        var consumerPlanSecond = rootFirst.PlanOf<QueryLessonsConsumer>();
        var consumerPlanFirst = consumerFirst.PlanOf<QueryLessonsConsumer>();
        var rootPlanSecond = consumerFirst.PlanOf<QueryLessonsHandler>();

        ScopeNode[] seeds = [ScopeNode.Unkeyed(typeof(SourceArgs))];
        foreach (var rootPlan in new[] { rootPlanFirst, rootPlanSecond })
        {
            Assert.Equal(expected: seeds, actual: rootPlan.Seeds);
            Assert.Empty(rootPlan.Errors);
        }
        foreach (var consumerPlan in new[] { consumerPlanFirst, consumerPlanSecond })
        {
            var error = Assert.Single(consumerPlan.Errors);
            Assert.Equal(expected: ScopePlanIssueKind.MissingRegistration, actual: error.Kind);
        }
    }

    [Fact]
    public async Task ScopeDisposesAHandlerItCreated()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var scope = await invocation.ServiceProvider.OpenAsync<DisposableHandler>(SupplySource);
        var handler = scope.Handler;
        await scope.DisposeAsync();

        Assert.Equal(expected: 1, actual: handler.DisposeCount);
    }

    [Fact]
    public void CycleThroughAnUnregisteredRootIsReportedTheSameInEitherOrder()
    {
        using var rootFirst = CreateCycleServices().BuildServiceProvider();
        using var serviceFirst = CreateCycleServices().BuildServiceProvider();

        var rootPlanFirst = rootFirst.PlanOf<CycleRoot>();
        var servicePlanSecond = rootFirst.PlanOf<CycleService>();
        var servicePlanFirst = serviceFirst.PlanOf<CycleService>();
        var rootPlanSecond = serviceFirst.PlanOf<CycleRoot>();

        var rootIssues = IssueKinds(rootPlanFirst);
        var serviceIssues = IssueKinds(servicePlanFirst);
        Assert.Equal(expected: rootIssues, actual: IssueKinds(rootPlanSecond));
        Assert.Equal(expected: serviceIssues, actual: IssueKinds(servicePlanSecond));
        Assert.Equal(expected: [ScopePlanIssueKind.MissingRegistration], actual: serviceIssues);

        static IServiceCollection CreateCycleServices()
        {
            var services = new ServiceCollection();
            services.AddScopePlans();
            services.AddSeed<SourceArgs>();
            services.AddScoped<CycleService>();
            return services;
        }

        static ScopePlanIssueKind[] IssueKinds(ScopePlan plan)
        {
            return [.. plan.Errors.Select(e => e.Kind)];
        }
    }

    [Fact]
    public async Task ReplacedInitializerIsNoLongerValidated()
    {
        var services = new ServiceCollection();
        services.AddScopePlans();
        services.AddSeed<SourceArgs>();
        services.AddTransient<LessonQuery>();
        services.AddInitialized<LoadedSchedule, UnresolvableScheduleInitializer>();
        services.RemoveScopeValue<LoadedSchedule>();
        services.AddInitialized<LoadedSchedule, FakeScheduleInitializer>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<QueryLessonsHandler>(supply: null);

        Assert.Equal(expected: "fake", actual: scope.Handler.Schedule.DataDir);
    }

    [Fact]
    public void RegisteredCycleIsReportedTheSameInEitherOrder()
    {
        using var firstFirst = CreateCycleServices().BuildServiceProvider();
        using var secondFirst = CreateCycleServices().BuildServiceProvider();

        _ = firstFirst.PlanOf<CycleFirst>();
        var secondPlanLater = firstFirst.PlanOf<CycleSecond>();
        var secondPlanFirst = secondFirst.PlanOf<CycleSecond>();

        ScopePlanIssue[] errors = [.. secondPlanFirst.Errors];
        Assert.Equal(expected: errors, actual: [.. secondPlanLater.Errors]);
        var cycle = Assert.Single(secondPlanFirst.Errors, e => e.Kind == ScopePlanIssueKind.Cycle);
        Assert.Single(secondPlanFirst.Errors, e => e.Kind == ScopePlanIssueKind.MissingRegistration);
        ScopeNode[] path =
        [
            ScopeNode.Unkeyed(typeof(CycleFirst)),
            ScopeNode.Unkeyed(typeof(CycleSecond)),
            ScopeNode.Unkeyed(typeof(CycleFirst)),
        ];
        Assert.Equal(expected: path, actual: cycle.Related);

        static IServiceCollection CreateCycleServices()
        {
            var services = new ServiceCollection();
            services.AddScopePlans();
            services.AddSeed<SourceArgs>();
            services.AddScoped<CycleFirst>();
            services.AddScoped<CycleSecond>();
            return services;
        }
    }

    [Fact]
    public void CycleReachedThroughSeveralPathsReportsEachProblemOnce()
    {
        var services = new ServiceCollection();
        services.AddScopePlans();
        services.AddSeed<SourceArgs>();
        services.AddScoped<DiamondTop>();
        services.AddScoped<DiamondLeft>();
        services.AddScoped<DiamondRight>();
        services.AddScoped<DiamondBottom>();
        using var root = services.BuildServiceProvider();

        var plan = root.PlanOf<DiamondTop>();

        var cycle = Assert.Single(plan.Errors, e => e.Kind == ScopePlanIssueKind.Cycle);
        var missing = Assert.Single(plan.Errors, e => e.Kind == ScopePlanIssueKind.MissingRegistration);
        Assert.Equal(expected: ScopeNode.Unkeyed(typeof(DiamondBottom)), actual: missing.Subject);
        ScopeNode[] path =
        [
            ScopeNode.Unkeyed(typeof(DiamondTop)),
            ScopeNode.Unkeyed(typeof(DiamondLeft)),
            ScopeNode.Unkeyed(typeof(DiamondBottom)),
            ScopeNode.Unkeyed(typeof(DiamondTop)),
        ];
        Assert.Equal(expected: path, actual: cycle.Related);
    }

    [Fact]
    public void KeyedCycleIsReportedTheSameFromEitherEnd()
    {
        using var fromInt = CreateKeyedCycleServices().BuildServiceProvider();
        using var fromString = CreateKeyedCycleServices().BuildServiceProvider();

        var intCycle = Assert.Single(fromInt.PlanOf<IntKeyRoot>().Errors);
        var stringCycle = Assert.Single(fromString.PlanOf<StringKeyRoot>().Errors);

        Assert.Equal(expected: intCycle, actual: stringCycle);
        ScopeNode[] path = [new(typeof(ILink), 1), new(typeof(ILink), "1"), new(typeof(ILink), 1)];
        Assert.Equal(expected: path, actual: intCycle.Related);

        static IServiceCollection CreateKeyedCycleServices()
        {
            var services = new ServiceCollection();
            services.AddScopePlans();
            services.AddSeed<SourceArgs>();
            services.AddKeyedScoped<ILink, IntKeyLink>(1);
            services.AddKeyedScoped<ILink, StringKeyLink>("1");
            return services;
        }
    }

    [Fact]
    public void EachSeparateCycleIsReported()
    {
        var services = new ServiceCollection();
        services.AddScopePlans();
        services.AddSeed<SourceArgs>();
        services.AddScoped<CycleFirst>();
        services.AddScoped<CycleSecond>();
        services.AddScoped<CycleRoot>();
        services.AddScoped<CycleService>();
        using var root = services.BuildServiceProvider();

        var plan = root.PlanOf<TwoCyclesRoot>();

        var cycles = plan.Errors.Where(e => e.Kind == ScopePlanIssueKind.Cycle).ToList();
        Assert.Equal(expected: 2, actual: cycles.Count);
    }

    public static TheoryData<Action<IServiceCollection>> AddsOfEveryKind()
    {
        return new()
        {
            static s => s.AddSeed<TeacherSettings>(),
            static s => s.AddDerived<TeacherSettings, FixedTeacherSettingsFactory>(),
            static s => s.AddInitialized<TeacherSettings, InitializedTeacherSettingsInitializer>(),
        };
    }

    [Theory]
    [MemberData(nameof(AddsOfEveryKind))]
    public void AddingARegisteredValueAgainThrows(Action<IServiceCollection> add)
    {
        var services = CreateServices();

        var duplicate = Assert.Throws<DuplicateScopeValueException>(() => add(services));

        Assert.Equal(expected: ScopeNode.Unkeyed(typeof(TeacherSettings)), actual: duplicate.Value);
    }

    [Fact]
    public void KeyedSeedsAreDuplicatesOnlyUnderTheSameKey()
    {
        var services = CreateServices();

        services.AddKeyedSeed<SourceArgs>("next");

        Assert.Throws<DuplicateScopeValueException>(() => services.AddKeyedSeed<SourceArgs>("previous"));
    }

    [Fact]
    public void RemovingAnUnregisteredValueThrows()
    {
        var services = CreateServices();

        var unkeyed = Assert.Throws<ScopeValueNotRegisteredException>(services.RemoveScopeValue<LessonQuery>);
        var keyed = Assert.Throws<ScopeValueNotRegisteredException>(() => services.RemoveScopeValue<SourceArgs>("next"));

        Assert.Equal(expected: ScopeNode.Unkeyed(typeof(LessonQuery)), actual: unkeyed.Value);
        Assert.Equal(expected: new ScopeNode(typeof(SourceArgs), "next"), actual: keyed.Value);
    }

    [Fact]
    public void RemovedKeyedSeedCanBeAddedAgain()
    {
        var services = CreateServices();

        services.RemoveScopeValue<SourceArgs>("previous");

        Assert.DoesNotContain(services, d => Equals(d.ServiceKey, "previous"));
        Assert.Single(services, d => d.ServiceType == typeof(Slot<SourceArgs>) && d.ServiceKey is null);
        services.AddKeyedSeed<SourceArgs>("previous");
        using var root = services.Build();
        using var scope = root.CreateScope();
        var slot = scope.ServiceProvider.GetKeyedService<Slot<SourceArgs>>("previous");
        Assert.NotNull(slot);
    }

    [Fact]
    public void SeedAndInitializedRegistrationsReplaceEachOther()
    {
        var asSeed = CreateServices();
        asSeed.RemoveScopeValue<LoadedSchedule>();
        asSeed.AddSeed<LoadedSchedule>();
        using var seedRoot = asSeed.Build();
        var asInitialized = CreateServices();
        asInitialized.RemoveScopeValue<LoadedSchedule>();
        asInitialized.AddSeed<LoadedSchedule>();
        asInitialized.RemoveScopeValue<LoadedSchedule>();
        asInitialized.AddInitialized<LoadedSchedule, FakeScheduleInitializer>();
        using var initializedRoot = asInitialized.Build();

        ScopeNode[] seeds = [ScopeNode.Unkeyed(typeof(LoadedSchedule))];
        Assert.Equal(expected: seeds, actual: seedRoot.PlanOf<QueryLessonsHandler>().Seeds);
        Assert.Empty(initializedRoot.PlanOf<QueryLessonsHandler>().Seeds);
    }

    [Fact]
    public async Task SeedReplacesADerivedRegistration()
    {
        var services = CreateServices();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddSeed<TeacherSettings>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<ShowSettingsHandler>(
            s => s.Add(new TeacherSettings("supplied")));

        ScopeNode[] seeds = [ScopeNode.Unkeyed(typeof(TeacherSettings))];
        Assert.Equal(expected: seeds, actual: root.PlanOf<ShowSettingsHandler>().Seeds);
        Assert.Equal(expected: "supplied", actual: scope.Handler.Settings.Teacher);
    }

    [Fact]
    public void DerivedRegistrationReplacesASeed()
    {
        var services = CreateServices();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddSeed<TeacherSettings>();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddDerived<TeacherSettings, FixedTeacherSettingsFactory>();
        using var root = services.Build();

        Assert.Empty(root.PlanOf<ShowSettingsHandler>().Seeds);
    }

    [Fact]
    public async Task InitializedRegistrationReplacesADerivedOne()
    {
        var services = CreateServices();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddInitialized<TeacherSettings, InitializedTeacherSettingsInitializer>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<ShowSettingsHandler>(supply: null);

        Assert.Equal(expected: "initialized", actual: scope.Handler.Settings.Teacher);
    }

    [Fact]
    public void RemovingAnInitializerKeepsAnUnrelatedRegistrationOfItsType()
    {
        var services = CreateServices();
        services.AddSingleton<FakeScheduleInitializer>();
        services.RemoveScopeValue<LoadedSchedule>();
        services.AddInitialized<LoadedSchedule, FakeScheduleInitializer>();
        services.RemoveScopeValue<LoadedSchedule>();

        var remaining = Assert.Single(services, d => d.ServiceType == typeof(FakeScheduleInitializer));
        Assert.Equal(expected: ServiceLifetime.Singleton, actual: remaining.Lifetime);
    }

    [Fact]
    public async Task ReplacedDerivedValueWinsOverAnOrdinaryRegistrationBetween()
    {
        var services = CreateServices();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddScoped(_ => new TeacherSettings("ordinary"));
        services.AddDerived<TeacherSettings, FixedTeacherSettingsFactory>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<ShowSettingsHandler>(supply: null);

        Assert.Equal(expected: "fixed", actual: scope.Handler.Settings.Teacher);
    }

    [Fact]
    public async Task RemoveThenAddReplacesADerivedValue()
    {
        var services = CreateServices();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddDerived<TeacherSettings, UnresolvableTeacherSettingsFactory>();
        services.RemoveScopeValue<TeacherSettings>();
        services.AddDerived<TeacherSettings, FixedTeacherSettingsFactory>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<ShowSettingsHandler>(supply: null);

        Assert.Equal(expected: "fixed", actual: scope.Handler.Settings.Teacher);
        Assert.Empty(root.PlanOf<ShowSettingsHandler>().Seeds);
    }

    [Fact]
    public async Task RemoveThenAddReplacesAnInitializedValue()
    {
        var services = CreateServices();
        services.RemoveScopeValue<LoadedSchedule>();
        services.AddInitialized<LoadedSchedule, FakeScheduleInitializer>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<QueryLessonsHandler>(SupplySource);

        Assert.Equal(expected: "fake", actual: scope.Handler.Schedule.DataDir);
    }

    public static TheoryData<Action<IServiceCollection>> OrdinaryEditsOfAValue()
    {
        var preloaded = ServiceDescriptor.Scoped(static _ => new LoadedSchedule("preloaded", Load: 0));
        return new()
        {
            s => s.Replace(preloaded),
            s =>
            {
                s.RemoveAll<LoadedSchedule>();
                s.Add(preloaded);
            },
        };
    }

    [Theory]
    [MemberData(nameof(OrdinaryEditsOfAValue))]
    public async Task ValueEditedLikeAnOrdinaryServiceIsPlannedAsOne(Action<IServiceCollection> edit)
    {
        var services = CreateServices();
        edit(services);
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<QueryLessonsHandler>(supply: null);

        Assert.Empty(root.PlanOf<QueryLessonsHandler>().Steps);
        Assert.Equal(expected: "preloaded", actual: scope.Handler.Schedule.DataDir);
    }

    [Fact]
    public void PlanReadsScopeValuesFromTheCollectionItIsBuiltFrom()
    {
        var services = CreateServices();
        services.RemoveAll<ScopeValueRegistration>();
        using var root = services.Build();

        var plan = root.PlanOf<QueryLessonsHandler>();

        Assert.Empty(plan.Seeds);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void AddingScopePlansAgainChangesNothing()
    {
        var services = CreateServices();
        int count = services.Count;

        services.AddScopePlans();

        Assert.Equal(expected: count, actual: services.Count);
    }
}
