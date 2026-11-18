using Microsoft.Extensions.DependencyInjection;
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
    public async Task LaterInitializedRegistrationReplacesEarlierOne()
    {
        var services = CreateServices();
        services.AddInitialized<LoadedSchedule, FakeScheduleInitializer>();
        await using var root = services.Build();
        await using var invocation = root.CreateAsyncScope();

        await using var scope = await invocation.ServiceProvider.OpenAsync<QueryLessonsHandler>(SupplySource);

        Assert.Equal(expected: "fake", actual: scope.Handler.Schedule.DataDir);
    }
}
