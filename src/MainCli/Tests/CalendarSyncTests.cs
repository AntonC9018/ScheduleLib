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
                if (report.Id == "0" && report.Status == "completed")
                {
                    context.StreamUpdate(report with { Human = new ThrowingMessage() });
                }
            }));
        Assert.Equal(["delete old"], provider.Writes);
        Assert.Equal(6, failure.PartialResult.ExitCode);
        Assert.Equal("completed", Assert.Single(failure.PartialResult.Outcomes).State);
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
                : new(2026,
                    10,
                    18,
                    9,
                    0,
                    0,
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
            Assert.Equal(0, message.Index);
            Assert.Equal("Desired / Știință", message.Summary);
            Assert.Equal(start, message.Start);
            Assert.Equal(end, message.End);
            Assert.Equal(missingDates
                    ? []
                    : new[]
                    {
                        "RRULE:FREQ=WEEKLY;COUNT=2",
                        "EXDATE:20261025T060000Z",
                    },
                message.Recurrence);
            Assert.Equal(expected, message.Format(CultureInfo.CurrentCulture));
            var deleted = Assert.IsType<CalendarDeletedEventMessage>(reports.Single(x => x.Id == "deleted-0").Human);
            Assert.Equal("old-event", deleted.Id);
            Assert.Equal("Old", deleted.Summary);
            Assert.Equal("Delete event old-event: Old", deleted.Format(CultureInfo.CurrentCulture));
            Assert.Equal(["0", "1", "2"], reports.Where(x => x.Status == "planned").Select(x => x.Id));
            Assert.Equal(
                "create event: planned; calendar ; event ; desired index 0",
                reports.Single(x => x.Id == "2").Human.Format(CultureInfo.CurrentCulture));
            Assert.Equal(["index", "summary", "start", "end", "recurrence"],
                item.EnumerateObject().Select(x => x.Name));
            Assert.Equal(start?.ToString("o", CultureInfo.InvariantCulture), item.GetProperty("start").GetString());
            Assert.Equal(end?.ToString("o", CultureInfo.InvariantCulture), item.GetProperty("end").GetString());
            Assert.Equal(missingDates ? JsonValueKind.Null : JsonValueKind.Array,
                item.GetProperty("recurrence").ValueKind);
            Assert.Same(desired, Assert.Single(result.DesiredEvents));
            Assert.Equal("Changed", desired.Summary);
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
            new[]
            {
                "delete changed",
                "calendar",
                "event new",
            },
            provider.Writes);
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
        var result = await CalendarSync.Run(
            provider,
            "Lessons",
            [new(), new()],
            true,
            default);
        Assert.Equal(6, result.ExitCode);
        Assert.Equal(writes, provider.Writes.Count);
        Assert.Equal("uncertain", result.Outcomes.Last().State);
        Assert.Equal("completed", result.Outcomes.First().State);
        Assert.Equal(fail == "calendar" ? null : "new", result.CreatedCalendarId);
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
        Assert.Equal(5, result.ExitCode);
        Assert.Equal("failed", Assert.Single(result.Outcomes).State);
        Assert.Equal("calendar", Assert.Single(provider.Writes));
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
        Assert.Equal(130, result.ExitCode);
        Assert.Equal("event-1", result.Outcomes[2].EventId);
        Assert.Equal(0, result.Outcomes[2].DesiredEventIndex);
        Assert.Equal(1, result.Outcomes[3].DesiredEventIndex);
        Assert.Equal("uncertain", result.Outcomes[3].State);
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
            await using var lease = await LocalFileLock.Acquire(
                CalendarSync.LockPath(
                    root,
                    "actual@example.test",
                    "Lessons"),
                default);
            using var provider = new Fake();
            await Assert.ThrowsAsync<LocalOperationBusyException>(() => CalendarSync.Run(
                provider,
                "Lessons",
                [],
                true,
                default,
                root));
            Assert.Equal(0, provider.ListCalls);
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
        => Assert.Equal(exit, await CliHost.Run(args));

    [Fact]
    public async Task MissingProfileIsCleanJsonConfigurationFailure()
    {
        var original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            Assert.Equal(
                3,
                await new CalendarCommands().Sync(
                    new(),
                    new(),
                    new() { Json = true },
                    new()));
            using var document = System.Text.Json.JsonDocument.Parse(output.ToString());
            Assert.Equal("calendar sync", document.RootElement.GetProperty("command").GetString());
            Assert.Equal(3, document.RootElement.GetProperty("exitCode").GetInt32());
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
                0,
                await command.Sync(
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
            Assert.Equal("preview", document.RootElement.GetProperty("status").GetString());
            Assert.NotEmpty(data.GetProperty("desiredEvents").EnumerateArray());
            var desiredCount = data.GetProperty("desiredEvents").GetArrayLength();
            var calendarName = data.GetProperty("calendarName").GetString();
            Assert.Equal(
                new[]
                {
                    $"Create calendar {calendarName} with a new ID",
                    $"Create {desiredCount} events with new IDs",
                },
                document.RootElement.GetProperty("actions").EnumerateArray().Select(x => x.GetString()));
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
        public List<string> Writes
        {
            get;
        } = [];

        public string Destination
        {
            get;
            set;
        } = "old";

        public string? Fail
        {
            get;
            init;
        }

        public CancellationTokenSource? Cancellation
        {
            get;
            init;
        }

        public bool Primary
        {
            get;
            init;
        }

        public bool Missing
        {
            get;
            init;
        }

        private int _events;

        public int ListCalls
        {
            get;
            private set;
        }

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
                            Destination,
                            "Lessons",
                            Primary),
                    ]);
        }

        public Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string id, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<CalendarEventIdentity>>([new("old-event", "Old")]);

        public Task DeleteCalendar(string id, CancellationToken token)
        {
            Writes.Add("delete " + id);
            return Task.CompletedTask;
        }

        public Task<string> CreateCalendar(string summary, CancellationToken token)
        {
            Writes.Add("calendar");
            if (Fail == "rejected")
            {
                throw new Google.GoogleApiException("calendar", "Rejected")
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
            Writes.Add("event " + id);
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
