using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using Event = Google.Apis.Calendar.v3.Data.Event;

namespace ScheduleLib.Cli;

public sealed record CalendarDestination(string Id, string Summary, bool Primary);

public sealed record CalendarEventIdentity(string Id, string? Summary);

public sealed record CalendarActionOutcome(
    string Action,
    string? CalendarId,
    string? EventId,
    ActionState State,
    int? DesiredEventIndex = null);

public sealed record CalendarSyncResult(
    string Account,
    string CalendarName,
    CalendarDestination? ReplacedCalendar,
    IReadOnlyList<CalendarEventIdentity> DeletedEvents,
    IReadOnlyList<Event> DesiredEvents,
    string? CreatedCalendarId,
    IReadOnlyList<CalendarActionOutcome> Outcomes,
    int ExitCode,
    string[] Errors,
    string[] Warnings);

/// <summary>Mutations execute once: a failed response may conceal a successful remote create.</summary>
public interface ICalendarSyncProvider : IDisposable
{
    Task<string> GetAccount(CancellationToken token);
    Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token);
    Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string calendarId, CancellationToken token);
    Task DeleteCalendar(string calendarId, CancellationToken token);
    Task<string> CreateCalendar(string summary, CancellationToken token);

    Task<string> CreateEvent(
        string calendarId,
        Event desired,
        CancellationToken token);
}

/// <summary>
/// A rendering/output failure after a completed remote mutation. The remote
/// outcome already recorded in <see cref="PartialResult"/> must not be retried
/// or reclassified; callers stop further mutations and report it as-is.
/// </summary>
public sealed class CalendarStreamingException(CalendarSyncResult PartialResult, Exception? Inner = null)
    : CliOutputStreamingException("Calendar output streaming failed.", Inner)
{
    public CalendarSyncResult PartialResult { get; } = PartialResult;
}

public static class CalendarSync
{
    public static string LockPath(
        string root,
        string account,
        string name)
    {
        string path1 = root;
        var encoding = Encoding.UTF8;
        string text = $"{account}\n{name}";
        string s = text;
        byte[] source = encoding.GetBytes(s);
        byte[] inArray = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(inArray);
        string text2 = $"{toHexString}.lock";
        string path2 = text2;
        string combine = Path.Combine(
            path1,
            path2);
        return combine;
    }

    public static async Task<CalendarSyncResult> Run(
        ICalendarSyncProvider provider,
        string name,
        IReadOnlyList<Event> desired,
        bool apply,
        CancellationToken token,
        string? lockRoot = null,
        string? profile = null,
        Action<CalendarReport>? onOutcome = null)
    {
        bool CalculateConditionResult2()
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return true;
            }

            if (name.Equals("primary", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        bool conditionResult2 = CalculateConditionResult2();


        if (conditionResult2)
        {
            throw new ArgumentException("Primary calendars cannot be replaced; configure a named secondary calendar.");
        }

        var account = await provider.GetAccount(token);

        string CalculatePath1()
        {
            string path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return path1;
        }

        await using var lease = apply
            ? await LocalFileLock.Acquire(
                LockPath(
                    root: lockRoot
                    ?? Path.Combine(
                        CalculatePath1(),
                        "ScheduleLib",
                        "remote-locks",
                        "calendar"),
                    account: account,
                    name: name),
                token)
            : null;
        // Every invocation recomputes. Apply reads the current destination only after owning its lock.
        var calendars = await provider.ListCalendars(token);
        var existing = calendars.FirstOrDefault(x => x.Summary == name);

        bool CheckCondition()
        {
            if (existing?.Primary == true)
            {
                return true;
            }

            if (existing?.Id == account)
            {
                return true;
            }

            if (existing?.Id == "primary")
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new ArgumentException("The matching destination is the primary calendar; replacement is prohibited.");
        }

        var deleted = existing is null ? [] : await provider.ListEvents(existing.Id, token);
        var warnings = new List<string>
        {
            "Replacement creates a new calendar ID and new event IDs; no rollback is provided.",
        };
        if (desired.Count == 0)
        {
            warnings.Add("No desired events: apply still replaces the calendar with an empty calendar.");
        }

        if (calendars.Count(x => x.Summary == name) > 1)
        {
            warnings.Add(
                "Multiple calendars share this name; only the first matching calendar is replaced, preserving existing behavior.");
        }

        var outcomes = new List<CalendarActionOutcome>();
        string? created = null;
        // Stable plan-order ids shared by preview and apply and by each action's
        // planned record and its later terminal transition.
        var plan = new List<CalendarPlanStep>();
        if (existing is not null)
        {
            var list = plan;
            string id2 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string action2 = CalendarActions.DeleteCalendar;
            string calendarId = existing.Id;
            int? desiredEventIndex = null;
            CalendarPlanStep item2 = new(
                Id: id2,
                Action: action2,
                CalendarId: calendarId,
                DesiredEventIndex: desiredEventIndex);
            list.Add(item2);
        }

        var list2 = plan;
        string id3 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string action3 = CalendarActions.CreateCalendar;
        string? calendarId2 = null;
        int? desiredEventIndex2 = null;
        CalendarPlanStep item3 = new(
            Id: id3,
            Action: action3,
            CalendarId: calendarId2,
            DesiredEventIndex: desiredEventIndex2);
        list2.Add(item3);
        for (var planIndex = 0; planIndex < desired.Count; planIndex++)
        {
            var list3 = plan;
            string id4 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string action4 = CalendarActions.CreateEvent;
            string? calendarId3 = null;
            int? desiredEventIndex3 = planIndex;
            CalendarPlanStep item4 = new(
                Id: id4,
                Action: action4,
                CalendarId: calendarId3,
                DesiredEventIndex: desiredEventIndex3);
            list3.Add(item4);
        }

        void Report<TMessage>(
            string id,
            ActionState status,
            object? item,
            TMessage human) where TMessage : struct, ICliMessage
        {
            try
            {
                CalendarReport CalculateObj()
                {
                    CalendarReport obj = new(
                        Id: id,
                        Status: status,
                        item,
                        human);
                    return obj;
                }

                onOutcome?.Invoke(CalculateObj());
            }
            catch (CliOutputStreamingException ex)
            {
                throw StreamingFailure(ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var inner =
                    new GeneralCliOutputStreamingException("Calendar output streaming failed.", ex);
                Exception inner2 = inner;
                throw StreamingFailure(inner2);
            }
        }

        CalendarStreamingException StreamingFailure(Exception inner)
        {
            int exit = RemoteSyncOutcomes.StreamingFailureExit(outcomes.Select(x => x.State));
            string[] errors =
            [
                "Output streaming failed. Completed actions remain applied; inspect the destination before another apply.",
            ];
            bool reportRemaining2 = false;
            var partialResult = Result(
                exit,
                errors,
                reportRemaining: reportRemaining2);
            var inner2 = inner;
            CalendarStreamingException calendarStreamingException = new(
                partialResult,
                inner2);
            return calendarStreamingException;
        }

        void ReportPlanned(CalendarPlanStep step)
        {
            var outcome = new CalendarActionOutcome(
                Action: step.Action,
                CalendarId: step.CalendarId,
                EventId: null,
                State: ActionState.Planned,
                step.DesiredEventIndex);
            string id2 = step.Id;
            var status2 = outcome.State;
            object item = outcome;
            var human = new CalendarActionMessage(outcome);
            Report(
                id: id2,
                status: status2,
                item,
                human);
        }

        void AddOutcome(CalendarActionOutcome outcome)
        {
            // The completed remote outcome is recorded before rendering so a
            // rendering failure cannot change it or trigger another mutation.
            outcomes.Add(outcome);
            string id2 = PlanId(outcome);
            var status2 = outcome.State;
            object item = outcome;
            var human = new CalendarActionMessage(outcome);
            Report(
                id: id2,
                status: status2,
                item,
                human);
        }

        string PlanId(CalendarActionOutcome outcome)
        {
            foreach (var step in plan)
            {
                if (step.Action == outcome.Action && step.DesiredEventIndex == outcome.DesiredEventIndex)
                {
                    return step.Id;
                }
            }
            return outcomes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // Selection content streams as items in both modes; the final summary
        // keeps counts only and never repeats these arrays.
        foreach (var step in plan)
        {
            ReportPlanned(step);
        }

        static string? FormatCalendarTimestamp(DateTimeOffset? value) =>
            value?.ToString("o", System.Globalization.CultureInfo.InvariantCulture);

        for (var deletedIndex = 0; deletedIndex < deleted.Count; deletedIndex++)
        {
            var identity = deleted[deletedIndex];
            string toString = deletedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string text = $"deleted-{toString}";
            string id5 = text;
            var status3 = ActionState.Completed;
            object item5 = identity;
            var human2 =
                new CalendarDeletedEventMessage(Id: identity.Id, Summary: identity.Summary);
            Report(
                id: id5,
                status: status3,
                item5,
                human2);
        }
        for (var index = 0; index < desired.Count; index++)
        {
            var ev = desired[index];

            string CalculateId()
            {
                string toString2 = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string text2 = $"desired-{toString2}";
                string id6 = text2;
                return id6;
            }

            Report(
                id: CalculateId(),
                status: ActionState.Completed,
                new
                {
                    index,
                    summary = ev.Summary,
                    start = FormatCalendarTimestamp(ev.Start?.DateTimeDateTimeOffset),
                    end = FormatCalendarTimestamp(ev.End?.DateTimeDateTimeOffset),
                    recurrence = ev.Recurrence,
                },
                new CalendarDesiredEventMessage(
                    index,
                    ev.Summary,
                    Start: ev.Start?.DateTimeDateTimeOffset,
                    End: ev.End?.DateTimeDateTimeOffset,
                    (ev.Recurrence ?? []).ToImmutableArray()));
        }
        if (!apply)
        {
            return Result(CliExit.Success, []);
        }

        var action = CalendarActions.DeleteCalendar;
        string? eventId = null;
        var attempted = false;
        int? desiredIndex = null;

        try
        {
            if (existing is not null)
            {
                token.ThrowIfCancellationRequested();
                attempted = true;
                await provider.DeleteCalendar(existing.Id, token);
                AddOutcome(new CalendarActionOutcome(
                    Action: action,
                    CalendarId: existing.Id,
                    EventId: null,
                    State: ActionState.Completed));
            }
            action = CalendarActions.CreateCalendar;
            attempted = false;
            token.ThrowIfCancellationRequested();
            attempted = true;
            created = await provider.CreateCalendar(name, token);
            if (string.IsNullOrWhiteSpace(created))
            {
                throw new IOException("Calendar create returned no identity.");
            }

            AddOutcome(new CalendarActionOutcome(
                Action: action,
                CalendarId: created,
                EventId: null,
                State: ActionState.Completed));
            for (var index = 0; index < desired.Count; index++)
            {
                var ev = desired[index];
                desiredIndex = index;
                action = CalendarActions.CreateEvent;
                eventId = null;
                attempted = false;
                token.ThrowIfCancellationRequested();
                attempted = true;
                eventId = await provider.CreateEvent(
                    created,
                    ev,
                    token);
                if (string.IsNullOrWhiteSpace(eventId))
                {
                    throw new IOException("Event create returned no identity.");
                }

                AddOutcome(new CalendarActionOutcome(
                    Action: action,
                    CalendarId: created,
                    EventId: eventId,
                    State: ActionState.Completed,
                    desiredIndex));
            }
            return Result(CliExit.Success, []);
        }
        catch (CliOutputStreamingException)
        {
            // Rendering already recorded the completed remote outcome above; it
            // must not be reclassified as uncertain/failed or retried.
            throw;
        }
        catch (Exception error) when (RemoteSyncOutcomes.IsCancellationTransportError(token, error))
        {
            // Calendar records only an attempted interrupted action; the remaining
            // plan, including an unattempted current action, is reported by Result.
            if (attempted)
            {
                AddCurrentOutcome(ActionState.Uncertain);
            }

            return Result(
                CliExit.Cancelled,
                [
                    "Cancelled. Any uncertain action may have completed remotely; inspect the destination before another apply.",
                ]);
        }
        catch (GooglePreSendTransportException)
        {
            var effects = AnyCompleted();
            AddCurrentOutcome(ActionState.Failed);
            return Result(
                CliExit.AfterEffects(CliExit.IoFailure, effects),
                [RemoteSyncOutcomes.CredentialRefreshTransportError]);
        }
        catch (AuthenticationRequiredException)
        {
            return AuthenticationFailure();
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            return AuthenticationFailure();
        }
        catch (Google.GoogleApiException e) when (RemoteSyncOutcomes.IsUnauthorized(e))
        {
            return AuthenticationFailure();
        }
        catch (Google.GoogleApiException e) when (RemoteSyncOutcomes.IsRejected(e))
        {
            var effects = AnyCompleted();
            AddCurrentOutcome(ActionState.Failed);
            return Result(
                CliExit.AfterEffects(CliExit.IoFailure, effects),
                ["Google Calendar rejected the action; it was not retried. Completed actions remain applied."]);
        }
        catch (Exception)
        {
            // Calendar records nothing for an unattempted action here, and any
            // recorded outcome (including uncertain) makes the exit partial.
            if (attempted)
            {
                AddCurrentOutcome(ActionState.Uncertain);
            }

            return Result(
                CliExit.AfterEffects(CliExit.IoFailure, effects: outcomes.Count > 0),
                [
                    "Calendar application failed. An uncertain action may have completed remotely and was not retried; inspect the destination before another apply.",
                ]);
        }

        bool AnyCompleted() => RemoteSyncOutcomes.AnyCompleted(outcomes.Select(x => x.State));

        void AddCurrentOutcome(ActionState state)
        {
            string? calendarId = action == CalendarActions.DeleteCalendar ? existing?.Id : created;
            AddOutcome(new CalendarActionOutcome(
                Action: action,
                CalendarId: calendarId,
                EventId: eventId,
                State: state,
                desiredIndex));
        }

        CalendarSyncResult AuthenticationFailure()
        {
            var effects = AnyCompleted();
            AddCurrentOutcome(ActionState.Failed);
            return Result(
                CliExit.AfterEffects(CliExit.AuthenticationRequired, effects),
                [new AuthenticationRequiredException(provider: "google", teacher: profile ?? "").Message]);
        }

        CalendarSyncResult Result(
            int exit,
            string[] errors,
            bool reportRemaining = true)
        {
            if (apply && exit != CliExit.Success && reportRemaining)
            {
                var finished = outcomes.Select(PlanId)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var step in plan.Where(step => !finished.Contains(step.Id)))
                {
                    var skipped = new CalendarActionOutcome(
                        Action: step.Action,
                        CalendarId: step.CalendarId,
                        EventId: null,
                        State: ActionState.NotAttempted,
                        step.DesiredEventIndex);
                    Report(
                        id: step.Id,
                        status: skipped.State,
                        skipped,
                        new CalendarNotAttemptedMessage(step.Action, step.DesiredEventIndex));
                }
            }
            return new(
                Account: account,
                CalendarName: name,
                existing,
                deleted,
                desired,
                CreatedCalendarId: created,
                outcomes,
                exit,
                Errors: errors,
                Warnings: warnings.ToArray());
        }
    }
}

/// <summary>The wire names of the calendar replacement actions.</summary>
public static class CalendarActions
{
    public const string DeleteCalendar = "delete calendar";
    public const string CreateCalendar = "create calendar";
    public const string CreateEvent = "create event";
}
