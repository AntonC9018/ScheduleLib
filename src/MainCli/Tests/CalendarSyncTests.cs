using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;
using Event = Google.Apis.Calendar.v3.Data.Event;

public sealed class CalendarSyncTests
{
    [Fact]
    public async Task HumanFormattingFailurePreservesCompletedMutationAndStopsApply()
    {
        using var provider = new Fake();
        using var context = new CliCommandContext("calendar sync",
            new(),
            default);
        var failure = await Assert.ThrowsAsync<CalendarStreamingException>(() => CalendarSync.Run(
            provider,
            "Lessons",
            [new()],
            true,
            default,
            onOutcome: report =>
            {
                bool CheckCondition()
                {
                    if (!(report.Id == "0"))
                    {
                        return false;
                    }

                    if (!(report.Status == "completed"))
                    {
                        return false;
                    }

                    return true;
                }

                if (CheckCondition())
                {
                    context.StreamUpdate(report with { Human = new ThrowingMessage() });
                }
            }));
        Assert.Equal(expected: ["delete old"], actual: provider.Writes);
        Assert.Equal(expected: 6, actual: failure.PartialResult.ExitCode);
        Assert.Equal(expected: "completed", actual: Assert.Single(failure.PartialResult.Outcomes).State);
        Assert.IsType<InvalidOperationException>(failure.InnerException!.InnerException);
    }

    private readonly record struct ThrowingMessage : ICliMessage
    {
        public string Format(IFormatProvider provider) =>
            throw new InvalidOperationException("synthetic formatting failure");
    }

    [Theory]
    [InlineData("en-US", false)]
    [InlineData("ro-RO", false)]
    [InlineData("ro-RO", true)]
    public async Task ReportsKeepStructuredSelectionSnapshotsAndOriginalText(string cultureName, bool missingDates)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var provider = new Fake();
            DateTimeOffset? start = missingDates
                ? null
                : new(year: 2026,
                    month: 10,
                    day: 18,
                    hour: 9,
                    minute: 0,
                    second: 0,
                    TimeSpan.FromHours(3));
            var end = start?.AddMinutes(90);
            var recurrence = new List<string>
            {
                "RRULE:FREQ=WEEKLY;COUNT=2",
                "EXDATE:20261025T060000Z",
            };
            var desired = new Event
            {
                Summary = "Desired / Știință",
                Start = new() { DateTimeDateTimeOffset = start },
                End = new() { DateTimeDateTimeOffset = end },
                Recurrence = missingDates ? null : recurrence,
            };
            var expected =
                $"Create event [0]: {desired.Summary}; {start}–{end}; {string.Join(", ", desired.Recurrence ?? [])}";
            var reports = new List<CalendarReport>();
            JsonElement item = default;
            var result = await CalendarSync.Run(
                provider,
                "Lessons",
                [desired],
                false,
                default,
                onOutcome: report =>
                {
                    reports.Add(report);
                    if (report.Id != "desired-0")
                    {
                        return;
                    }
                    item = JsonSerializer.SerializeToElement(report.Item);
                    // A callback can retain the message and mutate the SDK payload.
                    // Only human presentation data is snapshotted; apply still owns the original payload.
                    desired.Summary = "Changed";
                    desired.Start.DateTimeDateTimeOffset = start?.AddDays(1);
                    desired.End.DateTimeDateTimeOffset = end?.AddDays(1);
                    recurrence.Clear();
                });

            var message = Assert.IsType<CalendarDesiredEventMessage>(reports.Single(x => x.Id == "desired-0").Human);
            Assert.Equal(expected: 0, actual: message.Index);
            Assert.Equal(expected: "Desired / Știință", actual: message.Summary);
            Assert.Equal(expected: start, actual: message.Start);
            Assert.Equal(expected: end, actual: message.End);
            Assert.Equal(expected: missingDates
                    ? []
                    : new[]
                    {
                        "RRULE:FREQ=WEEKLY;COUNT=2",
                        "EXDATE:20261025T060000Z",
                    },
                actual: message.Recurrence);
            Assert.Equal(expected: expected, actual: message.Format(CultureInfo.CurrentCulture));
            var deleted = Assert.IsType<CalendarDeletedEventMessage>(reports.Single(x => x.Id == "deleted-0").Human);
            Assert.Equal(expected: "old-event", actual: deleted.Id);
            Assert.Equal(expected: "Old", actual: deleted.Summary);
            Assert.Equal(expected: "Delete event old-event: Old", actual: deleted.Format(CultureInfo.CurrentCulture));
            Assert.Equal(expected: ["0", "1", "2"], actual: reports.Where(x => x.Status == "planned").Select(x => x.Id));
            Assert.Equal(
                expected: "create event: planned; calendar ; event ; desired index 0",
                actual: reports.Single(x => x.Id == "2").Human.Format(CultureInfo.CurrentCulture));
            Assert.Equal(expected: ["index", "summary", "start", "end", "recurrence"],
                actual: item.EnumerateObject().Select(x => x.Name));
            Assert.Equal(expected: start?.ToString("o", CultureInfo.InvariantCulture), actual: item.GetProperty("start").GetString());
            Assert.Equal(expected: end?.ToString("o", CultureInfo.InvariantCulture), actual: item.GetProperty("end").GetString());
            Assert.Equal(expected: missingDates ? JsonValueKind.Null : JsonValueKind.Array,
                actual: item.GetProperty("recurrence").ValueKind);
            Assert.Same(expected: desired, actual: Assert.Single(result.DesiredEvents));
            Assert.Equal(expected: "Changed", actual: desired.Summary);
            Assert.Empty(provider.Writes);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task PreviewReportsReplacementAndEventsWithoutWrites()
    {
        using var provider = new Fake();
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new() { Summary = "Desired" }],
            false,
            default);
        Assert.Equal(expected: "actual@example.test", actual: result.Account);
        Assert.Equal(expected: "old", actual: result.ReplacedCalendar!.Id);
        Assert.Equal(expected: "old-event", actual: Assert.Single(result.DeletedEvents).Id);
        Assert.Equal(expected: "Desired", actual: Assert.Single(result.DesiredEvents).Summary);
        Assert.Empty(provider.Writes);
        Assert.Empty(result.Outcomes);
    }

    [Fact]
    public async Task ApplyRecomputesRemoteStateAndReportsNewIds()
    {
        using var provider = new Fake();
        await CalendarSync.Run(
            provider,
            "Lessons",
            [],
            false,
            default);
        provider.Destination = "changed";
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new()],
            true,
            default);
        Assert.Equal(
            expected: new[]
            {
                "delete changed",
                "calendar",
                "event new",
            },
            actual: provider.Writes);
        Assert.Equal(expected: "new", actual: result.CreatedCalendarId);
        Assert.Equal(expected: "event-1", actual: result.Outcomes.Last().EventId);
        Assert.Equal(expected: 0, actual: result.ExitCode);
    }

    [Theory]
    [InlineData("calendar", 2)]
    [InlineData("event", 3)]
    public async Task UncertainCreatesAreNotRetriedAndPreserveCompletedOutcomes(string fail, int writes)
    {
        using var provider = new Fake { Fail = fail };
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new(), new()],
            true,
            default);
        Assert.Equal(expected: 6, actual: result.ExitCode);
        Assert.Equal(expected: writes, actual: provider.Writes.Count);
        Assert.Equal(expected: "uncertain", actual: result.Outcomes.Last().State);
        Assert.Equal(expected: "completed", actual: result.Outcomes.First().State);
        Assert.Equal(expected: fail == "calendar" ? null : "new", actual: result.CreatedCalendarId);
    }

    [Fact]
    public async Task RejectedFirstCreateIsFailureBeforePartialApplication()
    {
        using var provider = new Fake
        {
            Fail = "rejected",
            Missing = true,
        };
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new()],
            true,
            default);
        Assert.Equal(expected: 5, actual: result.ExitCode);
        Assert.Equal(expected: "failed", actual: Assert.Single(result.Outcomes).State);
        Assert.Equal(expected: "calendar", actual: Assert.Single(provider.Writes));
    }

    [Fact]
    public async Task CancellationAfterOneEventKeepsItsIdAndIdentifiesTheNextUncertainEvent()
    {
        using var cancel = new CancellationTokenSource();
        using var provider = new Fake
        {
            Fail = "cancel-second",
            Cancellation = cancel,
        };
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new(), new()],
            true,
            cancel.Token);
        Assert.Equal(expected: 130, actual: result.ExitCode);
        Assert.Equal(expected: "event-1", actual: result.Outcomes[2].EventId);
        Assert.Equal(expected: 0, actual: result.Outcomes[2].DesiredEventIndex);
        Assert.Equal(expected: 1, actual: result.Outcomes[3].DesiredEventIndex);
        Assert.Equal(expected: "uncertain", actual: result.Outcomes[3].State);
    }

    [Fact]
    public async Task CancellationDuringCreatePreservesCalendarAndUncertainEvent()
    {
        using var cancel = new CancellationTokenSource();
        using var provider = new Fake
        {
            Fail = "cancel",
            Cancellation = cancel,
        };
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new(), new()],
            true,
            cancel.Token);
        Assert.Equal(expected: 130, actual: result.ExitCode);
        Assert.Equal(expected: "new", actual: result.CreatedCalendarId);
        Assert.Equal(expected: 3, actual: result.Outcomes.Count);
        Assert.Equal(expected: "uncertain", actual: result.Outcomes.Last().State);
        Assert.Equal(expected: 3, actual: provider.Writes.Count);
    }

    [Fact]
    public async Task ActualAccountAndDestinationLockBlocksAliasesBeforeRemoteStateRead()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            await using var lease = await LocalFileLock.Acquire(
                CalendarSync.LockPath(
                    root: root,
                    account: "actual@example.test",
                    name: "Lessons"),
                default);
            using var provider = new Fake();
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => CalendarSync.Run(
                provider,
                name: "Lessons",
                [],
                true,
                default,
                lockRoot: root));
            Assert.Equal(expected: 0, actual: provider.ListCalls);
            Assert.Empty(provider.Writes);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("primary", false)]
    [InlineData("Lessons", true)]
    public async Task PrimaryNameAndActualPrimaryDestinationAreProhibited(string name, bool primary)
    {
        using var provider = new Fake { Primary = primary };
        await Assert.ThrowsAsync<ArgumentException>(() => CalendarSync.Run(
            provider,
            name,
            [],
            true,
            default));
        Assert.Empty(provider.Writes);
    }

    [Theory]
    [InlineData(
        new[]
        {
            "calendar",
            "sync",
            "--help",
        },
        0)]
    [InlineData(
        new[]
        {
            "calendar",
            "sync",
            "--output",
            "x",
        },
        2)]
    public async Task HelpAndIrrelevantOptionsDoNotLoadSchedule(string[] args, int exit)
        => Assert.Equal(expected: exit, actual: await CliHost.Run(args));

    [Fact]
    public async Task MissingProfileIsCleanJsonConfigurationFailure()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                expected: 3,
                actual: await new CalendarCommands().Sync(
                    new(),
                    new(),
                    new() { Json = true },
                    new()));
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal(expected: "calendar sync", actual: document.RootElement.GetProperty("command").GetString());
            Assert.Equal(expected: 3, actual: document.RootElement.GetProperty("exitCode").GetInt32());
        }
        finally
        {
            Console.SetOut(original);
        }
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
            Assert.Equal(
                expected: 0,
                actual: await command.Sync(
                    new()
                    {
                        NoCache = true,
                        DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                    },
                    new() { Profile = "Nartea Nichita" },
                    new() { Json = true },
                    new()));
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(expected: "preview", actual: document.RootElement.GetProperty("status").GetString());
            Assert.NotEmpty(data.GetProperty("desiredEvents").EnumerateArray());
            var desiredCount = data.GetProperty("desiredEvents").GetArrayLength();
            var calendarName = data.GetProperty("calendarName").GetString();
            Assert.Equal(
                expected: new[]
                {
                    $"Create calendar {calendarName} with a new ID",
                    $"Create {desiredCount} events with new IDs",
                },
                actual: document.RootElement.GetProperty("actions").EnumerateArray().Select(x => x.GetString()));
            Assert.Empty(fake.Writes);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class FixtureCalendar(Fake provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x => x.EnrichWithFullNames = false);
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }

        protected override Task<ICalendarSyncProvider> Connect(
            IServiceProvider services,
            BuiltGoogleCalendarConfig config,
            CancellationToken token)
            => Task.FromResult<ICalendarSyncProvider>(provider);
    }

    private sealed class Fake : ICalendarSyncProvider
    {
        public List<string> Writes { get; } = [];

        public string Destination { get; set; } = "old";

        public string? Fail { get; init; }

        public CancellationTokenSource? Cancellation { get; init; }

        public bool Primary { get; init; }

        public bool Missing { get; init; }

        private int _events;

        public int ListCalls { get; private set; }

        public Task<string> GetAccount(CancellationToken token) => Task.FromResult("actual@example.test");

        public Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
        {
            ListCalls++;
            return Task.FromResult<IReadOnlyList<CalendarDestination>>(
                Missing
                    ? []
                    :
                    [
                        new(
                            Id: Destination,
                            Summary: "Lessons",
                            Primary),
                    ]);
        }

        public Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string id, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<CalendarEventIdentity>>([new(Id: "old-event", Summary: "Old")]);

        public Task DeleteCalendar(string id, CancellationToken token)
        {
            Writes.Add((string) $"delete {id}");
            return Task.CompletedTask;
        }

        public Task<string> CreateCalendar(string summary, CancellationToken token)
        {
            Writes.Add("calendar");
            if (Fail == "rejected")
            {
                throw new Google.GoogleApiException(serviceName: "calendar", message: "Rejected")
                {
                    HttpStatusCode = System.Net.HttpStatusCode.BadRequest,
                };
            }

            if (Fail == "calendar")
            {
                throw new IOException("Response lost after creating calendar");
            }

            return Task.FromResult("new");
        }

        public Task<string> CreateEvent(
            string id,
            Event ev,
            CancellationToken token)
        {
            Writes.Add((string) $"event {id}");
            _events++;
            if (Fail == "cancel-second" && _events == 2)
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(token);
            }
            if (Fail == "event")
            {
                throw new IOException("Response lost after creating event");
            }

            if (Fail == "cancel")
            {
                Cancellation!.Cancel();
                throw new OperationCanceledException(token);
            }
            return Task.FromResult("event-1");
        }

        public void Dispose()
        {
        }
    }
}
