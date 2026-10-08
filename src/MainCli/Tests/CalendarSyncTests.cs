using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Dates;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using Xunit;
using Event = Google.Apis.Calendar.v3.Data.Event;

public sealed class CalendarSyncTests
{
    [Fact]
    public async Task PreviewReportsReplacementAndEventsWithoutWrites()
    {
        using var provider = new Fake();
        var result = await CalendarSync.Run(provider, "Lessons", [new() { Summary = "Desired" }], false, default);
        Assert.Equal("actual@example.test", result.Account);
        Assert.Equal("old", result.ReplacedCalendar!.Id);
        Assert.Equal("old-event", Assert.Single(result.DeletedEvents).Id);
        Assert.Equal("Desired", Assert.Single(result.DesiredEvents).Summary);
        Assert.Empty(provider.Writes);
        Assert.Empty(result.Outcomes);
    }

    [Fact]
    public async Task ApplyRecomputesRemoteStateAndReportsNewIds()
    {
        using var provider = new Fake();
        await CalendarSync.Run(provider, "Lessons", [], false, default);
        provider.Destination = "changed";
        var result = await CalendarSync.Run(provider, "Lessons", [new()], true, default);
        Assert.Equal(new[] { "delete changed", "calendar", "event new" }, provider.Writes);
        Assert.Equal("new", result.CreatedCalendarId);
        Assert.Equal("event-1", result.Outcomes.Last().EventId);
        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("calendar", 2)]
    [InlineData("event", 3)]
    public async Task UncertainCreatesAreNotRetriedAndPreserveCompletedOutcomes(string fail, int writes)
    {
        using var provider = new Fake { Fail = fail };
        var result = await CalendarSync.Run(provider, "Lessons", [new(), new()], true, default);
        Assert.Equal(6, result.ExitCode);
        Assert.Equal(writes, provider.Writes.Count);
        Assert.Equal("uncertain", result.Outcomes.Last().State);
        Assert.Equal("completed", result.Outcomes.First().State);
        Assert.Equal(fail == "calendar" ? null : "new", result.CreatedCalendarId);
    }

    [Fact]
    public async Task RejectedFirstCreateIsFailureBeforePartialApplication()
    {
        using var provider = new Fake { Fail = "rejected", Missing = true };
        var result = await CalendarSync.Run(provider, "Lessons", [new()], true, default);
        Assert.Equal(5, result.ExitCode);
        Assert.Equal("failed", Assert.Single(result.Outcomes).State);
        Assert.Equal("calendar", Assert.Single(provider.Writes));
    }

    [Fact]
    public async Task CancellationAfterOneEventKeepsItsIdAndIdentifiesTheNextUncertainEvent()
    {
        using var provider = new Fake { Fail = "cancel-second" };
        var result = await CalendarSync.Run(provider, "Lessons", [new(), new()], true, default);
        Assert.Equal(130, result.ExitCode);
        Assert.Equal("event-1", result.Outcomes[2].EventId);
        Assert.Equal(0, result.Outcomes[2].DesiredEventIndex);
        Assert.Equal(1, result.Outcomes[3].DesiredEventIndex);
        Assert.Equal("uncertain", result.Outcomes[3].State);
    }

    [Fact]
    public async Task CancellationDuringCreatePreservesCalendarAndUncertainEvent()
    {
        using var provider = new Fake { Fail = "cancel" };
        var result = await CalendarSync.Run(provider, "Lessons", [new(), new()], true, default);
        Assert.Equal(130, result.ExitCode);
        Assert.Equal("new", result.CreatedCalendarId);
        Assert.Equal(3, result.Outcomes.Count);
        Assert.Equal("uncertain", result.Outcomes.Last().State);
        Assert.Equal(3, provider.Writes.Count);
    }

    [Fact]
    public async Task ActualAccountAndDestinationLockBlocksAliasesBeforeRemoteStateRead()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using var lease = await LocalFileLock.Acquire(CalendarSync.LockPath(root, "actual@example.test", "Lessons"), default);
            using var provider = new Fake();
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => CalendarSync.Run(provider, "Lessons", [], true, default, root));
            Assert.Equal(0, provider.ListCalls);
            Assert.Empty(provider.Writes);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("primary", false)]
    [InlineData("Lessons", true)]
    public async Task PrimaryNameAndActualPrimaryDestinationAreProhibited(string name, bool primary)
    {
        using var provider = new Fake { Primary = primary };
        await Assert.ThrowsAsync<ArgumentException>(() => CalendarSync.Run(provider, name, [], true, default));
        Assert.Empty(provider.Writes);
    }

    [Theory]
    [InlineData(new[] { "calendar", "sync", "--help" }, 0)]
    [InlineData(new[] { "calendar", "sync", "--output", "x" }, 2)]
    public async Task HelpAndIrrelevantOptionsDoNotLoadSchedule(string[] args, int exit)
        => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task MissingProfileIsCleanJsonConfigurationFailure()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(3, await new CalendarCommands().Sync(new(), new(), new() { Json = true }, new()));
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal("calendar sync", document.RootElement.GetProperty("command").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally { Console.SetOut(original); }
    }

    [Fact]
    public async Task CommandBuildsRealRecurringPayloadAndOutputsOneJsonPreview()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        using var fake = new Fake();
        try
        {
            Console.SetOut(output);
            var command = new FixtureCalendar(fake);
            Assert.Equal(0, await command.Sync(new() { NoCache = true, DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures") },
                new() { Profile = "Nartea Nichita" }, new() { Json = true }, new()));
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal("preview", document.RootElement.GetProperty("status").GetString());
            Assert.NotEmpty(data.GetProperty("desiredEvents").EnumerateArray());
            Assert.Empty(fake.Writes);
        }
        finally { Console.SetOut(original); }
    }

    private sealed class FixtureCalendar(Fake provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x => { x.StudyYear = new(2025); x.Semester = Semester.Sem2; });
        }
        protected override Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
            => Task.FromResult<ICalendarSyncProvider>(provider);
    }

    private sealed class Fake : ICalendarSyncProvider
    {
        public List<string> Writes { get; } = [];
        public string Destination { get; set; } = "old";
        public string? Fail { get; init; }
        public bool Primary { get; init; }
        public bool Missing { get; init; }
        private int _events;
        public int ListCalls { get; private set; }
        public Task<string> GetAccount(CancellationToken token) => Task.FromResult("actual@example.test");
        public Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<CalendarDestination>>(Missing ? [] : [new(Destination, "Lessons", Primary)]);
        }
        public Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string id, CancellationToken token) => Task.FromResult<IReadOnlyList<CalendarEventIdentity>>([new("old-event", "Old")]);
        public Task DeleteCalendar(string id, CancellationToken token) { Writes.Add("delete " + id); return Task.CompletedTask; }
        public Task<string> CreateCalendar(string summary, CancellationToken token)
        {
            Writes.Add("calendar");
            if (Fail == "rejected") throw new Google.GoogleApiException("calendar", "Rejected") { HttpStatusCode = System.Net.HttpStatusCode.BadRequest };
            if (Fail == "calendar") throw new IOException("Response lost after creating calendar");
            return Task.FromResult("new");
        }
        public Task<string> CreateEvent(string id, Event ev, CancellationToken token)
        {
            Writes.Add("event " + id);
            _events++;
            if (Fail == "cancel-second" && _events == 2) throw new OperationCanceledException();
            if (Fail == "event") throw new IOException("Response lost after creating event");
            if (Fail == "cancel") throw new OperationCanceledException();
            return Task.FromResult("event-1");
        }
        public void Dispose() { }
    }
}
