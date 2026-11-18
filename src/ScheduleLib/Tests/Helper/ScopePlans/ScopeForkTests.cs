using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;
using static ScheduleLib.Tests.ScopePlans.ScopePlanFixture;

namespace ScheduleLib.Tests.ScopePlans;

public sealed class ScopeForkTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task ForksInheritSeedsAndInitializedValues()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var log = root.GetRequiredService<LoadLog>();

        await using var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        var grades = new List<QuizGrades>();
        foreach (int quiz in new[] { 1, 2 })
        {
            await using var copy = await parent.Handler.Copies.OpenAsync(s => s.Add(new QuizArgs(quiz)), None);
            Assert.Same(expected: parent.Handler.Session, actual: copy.Handler.Session);
            grades.Add(copy.Handler.Grades);
        }

        QuizGrades[] expected = [new(Quiz: 1, ScheduleLoad: 1), new(Quiz: 2, ScheduleLoad: 1)];
        Assert.Equal(expected: expected, actual: grades);
        Assert.Equal(expected: 1, actual: log.Count("schedule"));
        Assert.Equal(expected: 1, actual: log.Count("registry opened"));
        Assert.Equal(expected: 0, actual: log.Count("registry closed"));
    }

    [Fact]
    public async Task ReplacedSeedRebuildsTheValuesBuiltFromIt()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        await using var parent = await invocation.ServiceProvider.OpenAsync<QueryForkingHandler>(SupplySource);
        var queries = parent.Handler.Queries;
        await using var other = await queries.OpenAsync(s => s.Add(new SourceArgs("data/2024-sem2")), None);
        await using var same = await queries.OpenAsync(supply: null, None);

        var expected = new LoadedSchedule(DataDir: "data/2024-sem2", Load: 2);
        Assert.Equal(expected: expected, actual: other.Handler.Schedule);
        Assert.Same(expected: parent.Handler.Schedule, actual: same.Handler.Schedule);
    }

    [Fact]
    public async Task OpaqueInitializedValuesAreRebuilt()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var log = root.GetRequiredService<LoadLog>();

        await using var parent = await invocation.ServiceProvider.OpenAsync<LegacyReportHandler>(SupplySource);
        await using var child = await parent.Handler.Children.OpenAsync(supply: null, None);

        Assert.Equal(expected: 1, actual: parent.Handler.Report.Load);
        Assert.Equal(expected: 2, actual: child.Handler.Report.Load);
        Assert.Equal(expected: 1, actual: log.Count("schedule"));
    }

    [Fact]
    public async Task MissingSeedCheckCountsInheritedSeeds()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        await using var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        var exception = await Assert.ThrowsAsync<MissingSeedsException>(Open);

        ScopeNode[] expected = [ScopeNode.Unkeyed(typeof(QuizArgs))];
        Assert.Equal(expected: expected, actual: exception.Seeds);

        async Task Open()
        {
            await parent.Handler.Copies.OpenAsync(supply: null, None);
        }
    }

    [Fact]
    public async Task ResourcesCloseWhenTheirScopeEnds()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        var connection = parent.Handler.Session.Connection;
        await using (await parent.Handler.Copies.OpenAsync(s => s.Add(new QuizArgs(1)), None))
        {
        }
        Assert.False(connection.IsClosed);
        await parent.DisposeAsync();

        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task ParentEndingWithAnOpenForkDisposesItAndThrows()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var log = root.GetRequiredService<LoadLog>();

        var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        _ = await parent.Handler.Copies.OpenAsync(s => s.Add(new QuizArgs(1)), None);
        var exception = await Assert.ThrowsAsync<ForkLeftOpenException>(async () => await parent.DisposeAsync());

        Type[] expected = [typeof(MoodleCopyHandler)];
        Assert.Equal(expected: expected, actual: exception.Forks);
        Assert.Equal(expected: 1, actual: log.Count("registry closed"));
    }

    [Fact]
    public async Task ForkCannotOpenAfterItsParentEnded()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        var copies = parent.Handler.Copies;
        await parent.DisposeAsync();
        var exception = await Assert.ThrowsAsync<ParentScopeEndedException>(Open);

        Assert.Equal(expected: typeof(MoodleCopyHandler), actual: exception.Fork);

        async Task Open()
        {
            await copies.OpenAsync(s => s.Add(new QuizArgs(1)), None);
        }
    }

    [Fact]
    public async Task FailingResourceDoesNotStopTheRestClosing()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var scope = await invocation.ServiceProvider.OpenAsync<FailingHandler>(supply: null);
        var connection = scope.Handler.Session.Connection;
        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(async () => await scope.DisposeAsync());

        Assert.IsType<ResourceCloseFailedException>(Assert.Single(exception.Failures));
        Assert.Empty(exception.ForksLeftOpen);
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task FailingForkDoesNotStopItsSiblingsOrItsParentClosing()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<FailingParentHandler>(supply: null);
        var connection = parent.Handler.Session.Connection;
        _ = await parent.Handler.Children.OpenAsync(supply: null, None);
        _ = await parent.Handler.Children.OpenAsync(supply: null, None);
        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(async () => await parent.DisposeAsync());

        Type[] forks = [typeof(FailingHandler), typeof(FailingHandler)];
        Assert.Equal(expected: forks, actual: exception.ForksLeftOpen);
        Assert.Equal(expected: 2, actual: exception.Failures.Length);
        Assert.All(exception.Failures, f => Assert.IsType<ScopeCleanupException>(f));
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task ParentEndingWaitsForAForkStillOpening()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var gate = root.GetRequiredService<InitializerGate>();

        var parent = await invocation.ServiceProvider.OpenAsync<GatedParentHandler>(supply: null);
        var connection = parent.Handler.Session.Connection;
        var opening = parent.Handler.Children.OpenAsync(supply: null, None).AsTask();
        await gate.Entered;
        var ending = parent.DisposeAsync().AsTask();

        Assert.False(ending.IsCompleted);
        Assert.False(connection.IsClosed);
        gate.Release();
        var exception = await Assert.ThrowsAsync<ParentScopeEndedException>(() => opening);
        await ending;

        Assert.Equal(expected: typeof(GatedHandler), actual: exception.Fork);
        Assert.True(gate.ConnectionWasOpen);
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task InvocationScopeEndsItsForksWhenAServiceFailsToDispose()
    {
        await using var root = CreateServices().Build();
        // Disposed once below, inside the assertion, because ending it throws.
#pragma warning disable CA2000
        var invocation = await InvocationScope.OpenAsync(root);
#pragma warning restore CA2000

        var parent = await invocation.Services.OpenAsync<GradeImportHandler>(SupplySource);
        var connection = parent.Handler.Session.Connection;
        _ = invocation.Services.GetRequiredService<FailingDisposable>();
        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(async () => await invocation.DisposeAsync());

        Type[] forks = [typeof(GradeImportHandler)];
        Assert.Equal(expected: forks, actual: exception.ForksLeftOpen);
        Assert.IsType<ResourceCloseFailedException>(Assert.Single(exception.Failures));
        Assert.True(connection.IsClosed);
        await invocation.DisposeAsync(); // reported once, to the first caller
    }

    [Fact]
    public async Task ForkStillOpeningReportsItsCleanupFailureOnlyToItsOpener()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var gate = root.GetRequiredService<InitializerGate>();

        var parent = await invocation.ServiceProvider.OpenAsync<GatedFailingParentHandler>(supply: null);
        var opening = parent.Handler.Children.OpenAsync(supply: null, None).AsTask();
        await gate.Entered;
        var ending = parent.DisposeAsync().AsTask();
        gate.Release();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => opening);
        await ending;

        Assert.Equal(expected: 2, actual: exception.InnerExceptions.Count);
        Assert.IsType<ParentScopeEndedException>(exception.InnerExceptions[0]);
        var cleanup = Assert.IsType<ScopeCleanupException>(exception.InnerExceptions[1]);
        Assert.IsType<ResourceCloseFailedException>(Assert.Single(cleanup.Failures));
    }

    [Fact]
    public async Task ForkFailingWhileItsParentEndsReportsBothToItsOpener()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var gate = root.GetRequiredService<InitializerGate>();
        // The initializer's own failure is kept even when it is the same kind of failure.
        gate.FailureOnRelease = new ParentScopeEndedException(typeof(Unregistered));

        var parent = await invocation.ServiceProvider.OpenAsync<GatedParentHandler>(supply: null);
        var opening = parent.Handler.Children.OpenAsync(supply: null, None).AsTask();
        await gate.Entered;
        var ending = parent.DisposeAsync().AsTask();
        gate.Release();

        var exception = await Assert.ThrowsAsync<AggregateException>(() => opening);
        await ending;

        Assert.Equal(expected: 2, actual: exception.InnerExceptions.Count);
        var parentEnded = Assert.IsType<ParentScopeEndedException>(exception.InnerExceptions[0]);
        Assert.Equal(expected: typeof(GatedHandler), actual: parentEnded.Fork);
        Assert.Same(expected: gate.FailureOnRelease, actual: exception.InnerExceptions[1]);
    }

    [Fact]
    public async Task ParentEndingWaitsForAForkStillSupplyingItsSeeds()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        using var entered = new ManualResetEventSlim();
        using var released = new ManualResetEventSlim();

        var parent = await invocation.ServiceProvider.OpenAsync<GatedParentHandler>(supply: null);
        var children = parent.Handler.Children;
        var opening = Task.Run(async () => await children.OpenAsync(BlockThenFail, None));
        entered.Wait(TimeSpan.FromSeconds(10));
        var ending = parent.DisposeAsync().AsTask();

        await Assert.ThrowsAsync<TimeoutException>(() => ending.WaitAsync(TimeSpan.FromMilliseconds(100)));
        released.Set();
        var exception = await Assert.ThrowsAsync<AggregateException>(() => opening);
        await ending;

        Assert.Equal(expected: 2, actual: exception.InnerExceptions.Count);
        Assert.IsType<ParentScopeEndedException>(exception.InnerExceptions[0]);
        Assert.IsType<SupplyFailedException>(exception.InnerExceptions[1]);

        void BlockThenFail(ScopeInputs inputs)
        {
            entered.Set();
            released.Wait(TimeSpan.FromSeconds(10));
            throw new SupplyFailedException();
        }
    }

    [Fact]
    public async Task ForkEndedByItsOwnerWhileItsParentEndsReportsItsFailureOnce()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<FailingParentHandler>(supply: null);
        var child = await parent.Handler.Children.OpenAsync(supply: null, None);
        var ownerEnding = child.DisposeAsync().AsTask();
        var parentEnding = parent.DisposeAsync().AsTask();

        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(() => ownerEnding);
        await parentEnding;

        Assert.IsType<ResourceCloseFailedException>(Assert.Single(exception.Failures));
    }

    [Fact]
    public async Task OpeningFromAnEndedParentFailsBeforeCheckingSeeds()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        await parent.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ParentScopeEndedException>(
            async () => await parent.Handler.Copies.OpenAsync(supply: null, None));
        Assert.Equal(expected: typeof(MoodleCopyHandler), actual: exception.Fork);
    }

    [Fact]
    public async Task OpeningAfterTheRootIsDisposedReportsTheEndedParent()
    {
        var root = CreateServices().Build();
        var invocation = root.CreateAsyncScope();
        var parent = await invocation.ServiceProvider.OpenAsync<GradeImportHandler>(SupplySource);
        var copies = parent.Handler.Copies;
        await parent.DisposeAsync();
        await invocation.DisposeAsync();
        await root.DisposeAsync();

        await Assert.ThrowsAsync<ParentScopeEndedException>(async () => await copies.OpenAsync(supply: null, None));
    }

    [Fact]
    public async Task SupplyingAValueThatIsNotASeedIsRejected()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();
        var forks = invocation.ServiceProvider.Forks<QueryLessonsHandler>();

        var exception = await Assert.ThrowsAsync<UnregisteredSeedsException>(
            async () => await forks.OpenAsync(SupplyUnregistered, None));

        ScopeNode[] seeds = [ScopeNode.Unkeyed(typeof(Unregistered))];
        Assert.Equal(expected: seeds, actual: exception.Seeds);

        static void SupplyUnregistered(ScopeInputs inputs)
        {
            inputs.Add(new Unregistered());
        }
    }

    [Fact]
    public async Task FailingHandlerDisposalStillClosesTheResources()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var scope = await invocation.ServiceProvider.OpenAsync<FailingDisposeHandler>(supply: null);
        var connection = scope.Handler.Session.Connection;
        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(async () => await scope.DisposeAsync());

        Assert.IsType<ResourceCloseFailedException>(Assert.Single(exception.Failures));
        Assert.True(connection.IsClosed);
    }

    [Fact]
    public async Task ParentEndsAnAbandonedForkThroughItsOwnTeardown()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var parent = await invocation.ServiceProvider.OpenAsync<DisposableForkingHandler>(supply: null);
        var child = await parent.Handler.Children.OpenAsync(SupplySource, None);
        var handler = child.Handler;
        await Assert.ThrowsAsync<ForkLeftOpenException>(async () => await parent.DisposeAsync());
        await child.DisposeAsync();

        Assert.Equal(expected: 1, actual: handler.DisposeCount);
    }

    [Fact]
    public async Task ActivatedHandlerAndResourceFailuresAreBothReported()
    {
        await using var root = CreateServices().Build();
        await using var invocation = root.CreateAsyncScope();

        var scope = await invocation.ServiceProvider.OpenAsync<FailingActivatedHandler>(supply: null);
        _ = scope.Handler;
        var exception = await Assert.ThrowsAsync<ScopeCleanupException>(async () => await scope.DisposeAsync());

        Assert.Equal(expected: 2, actual: exception.Failures.Length);
        Assert.All(exception.Failures, f => Assert.IsType<ResourceCloseFailedException>(f));
    }
}
