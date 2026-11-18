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
}
