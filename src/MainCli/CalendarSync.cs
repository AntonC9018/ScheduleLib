using System.Collections.Immutable;
using System.Net;
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
    string State,
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
        global::System.Text.Encoding encoding = Encoding.UTF8;
        string text = $"{account}\n{name}";
        string s = (string) text;
        byte[] source = encoding.GetBytes(s);
        byte[] inArray = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(inArray);
        string text2 = $"{toHexString}.lock";
        string path2 = (string) text2;
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
            else
            {
                if (name.Equals("primary", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                return false;
            }
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
            global::System.Collections.Generic.List<global::ScheduleLib.Cli.CalendarPlanStep> list = plan;
            string id2 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string action2 = "delete calendar";
            string calendarId = existing.Id;
            int? desiredEventIndex = null;
            global::ScheduleLib.Cli.CalendarPlanStep item2 = new(Id: id2,
                Action: action2,
                CalendarId: calendarId,
                DesiredEventIndex: desiredEventIndex);
            list.Add(item2);
        }

        global::System.Collections.Generic.List<global::ScheduleLib.Cli.CalendarPlanStep> list2 = plan;
        string id3 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string action3 = "create calendar";
        string? calendarId2 = null;
        int? desiredEventIndex2 = null;
        global::ScheduleLib.Cli.CalendarPlanStep item3 = new(Id: id3,
            Action: action3,
            CalendarId: calendarId2,
            DesiredEventIndex: desiredEventIndex2);
        list2.Add(item3);
        for (var planIndex = 0; planIndex < desired.Count; planIndex++)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.Cli.CalendarPlanStep> list3 = plan;
            string id4 = plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string action4 = "create event";
            string? calendarId3 = null;
            int? desiredEventIndex3 = planIndex;
            global::ScheduleLib.Cli.CalendarPlanStep item4 = new(Id: id4,
                Action: action4,
                CalendarId: calendarId3,
                DesiredEventIndex: desiredEventIndex3);
            list3.Add(item4);
        }

        void Report<TMessage>(
            string id,
            string status,
            object? item,
            TMessage human) where TMessage : struct, ICliMessage
        {
            try
            {
                global::ScheduleLib.Cli.CalendarReport CalculateObj()
                {
                    global::ScheduleLib.Cli.CalendarReport obj = new(Id: id,
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
                global::ScheduleLib.Cli.GeneralCliOutputStreamingException inner =
                    new GeneralCliOutputStreamingException("Calendar output streaming failed.", ex);
                global::System.Exception inner2 = inner;
                throw StreamingFailure(inner2);
            }
        }

        CalendarStreamingException StreamingFailure(Exception inner)
        {
            int exit;
            if (outcomes.Any(x => x.State == "completed"))
            {
                exit = 6;
            }
            else
            {
                exit = 1;
            }

            string[] errors =
            [
                "Output streaming failed. Completed actions remain applied; inspect the destination before another apply.",
            ];
            bool reportRemaining2 = false;
            global::ScheduleLib.Cli.CalendarSyncResult partialResult = Result(
                exit,
                errors,
                reportRemaining: reportRemaining2);
            global::System.Exception inner2 = inner;
            global::ScheduleLib.Cli.CalendarStreamingException calendarStreamingException = new(
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
                State: "planned",
                step.DesiredEventIndex);
            string id2 = step.Id;
            string status2 = outcome.State;
            object item = outcome;
            global::ScheduleLib.Cli.CalendarActionMessage human = new CalendarActionMessage(outcome);
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
            string status2 = outcome.State;
            object item = outcome;
            global::ScheduleLib.Cli.CalendarActionMessage human = new CalendarActionMessage(outcome);
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
            string id5 = (string) text;
            string status3 = "completed";
            object item5 = identity;
            global::ScheduleLib.Cli.CalendarDeletedEventMessage human2 =
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
                string id6 = (string) text2;
                return id6;
            }

            Report(
                id: CalculateId(),
                status: "completed",
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
            return Result(0, []);
        }

        var action = "delete calendar";
        string? eventId = null;
        var attempted = false;
        int? desiredIndex = null;

        static bool IsCancellationTransportError(CancellationToken cancellationToken, Exception error)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (error is not (OperationCanceledException or GooglePreSendTransportException))
            {
                return false;
            }

            return true;
        }

        static bool IsUnauthorizedGoogleApiError(Google.GoogleApiException error)
        {
            if (error.HttpStatusCode != HttpStatusCode.Unauthorized)
            {
                return false;
            }

            return true;
        }

        static bool IsHandledGoogleApiError(Google.GoogleApiException error)
        {
            if (error.HttpStatusCode is not (HttpStatusCode.BadRequest
                or HttpStatusCode.Forbidden
                or HttpStatusCode.NotFound
                or HttpStatusCode.Conflict
                or HttpStatusCode.PreconditionFailed
                or HttpStatusCode.UnprocessableEntity))
            {
                return false;
            }

            return true;
        }

        try
        {
            if (existing is not null)
            {
                token.ThrowIfCancellationRequested();
                attempted = true;
                await provider.DeleteCalendar(existing.Id, token);
                global::ScheduleLib.Cli.CalendarActionOutcome outcome2 = new(
                    Action: action,
                    CalendarId: existing.Id,
                    EventId: null,
                    State: "completed");
                AddOutcome(outcome2);
            }
            action = "create calendar";
            attempted = false;
            token.ThrowIfCancellationRequested();
            attempted = true;
            created = await provider.CreateCalendar(name, token);
            if (string.IsNullOrWhiteSpace(created))
            {
                throw new IOException("Calendar create returned no identity.");
            }

            global::ScheduleLib.Cli.CalendarActionOutcome outcome3 = new(
                Action: action,
                CalendarId: created,
                EventId: null,
                State: "completed");
            AddOutcome(outcome3);
            for (var index = 0; index < desired.Count; index++)
            {
                var ev = desired[index];
                desiredIndex = index;
                action = "create event";
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

                global::ScheduleLib.Cli.CalendarActionOutcome outcome4 = new(
                    Action: action,
                    CalendarId: created,
                    EventId: eventId,
                    State: "completed",
                    desiredIndex);
                AddOutcome(outcome4);
            }
            return Result(0, []);
        }
        catch (CliOutputStreamingException)
        {
            // Rendering already recorded the completed remote outcome above; it
            // must not be reclassified as uncertain/failed or retried.
            throw;
        }
        catch (Exception error) when (IsCancellationTransportError(token, error))
        {
            if (attempted)
            {
                string action5 = action;
                string? calendarId4;
                if (action == "delete calendar")
                {
                    calendarId4 = existing?.Id;
                }
                else
                {
                    calendarId4 = created;
                }

                string? eventId2 = eventId;
                string state = "uncertain";
                int? desiredEventIndex4 = desiredIndex;
                global::ScheduleLib.Cli.CalendarActionOutcome outcome5 = new(
                    Action: action5,
                    CalendarId: calendarId4,
                    EventId: eventId2,
                    State: state,
                    desiredEventIndex4);
                AddOutcome(outcome5);
            }

            return Result(
                130,
                [
                    "Cancelled. Any uncertain action may have completed remotely; inspect the destination before another apply.",
                ]);
        }
        catch (GooglePreSendTransportException)
        {
            var partial = outcomes.Any(x => x.State == "completed");
            string action6 = action;
            string? calendarId5;
            if (action == "delete calendar")
            {
                calendarId5 = existing?.Id;
            }
            else
            {
                calendarId5 = created;
            }

            string? eventId3 = eventId;
            string state2 = "failed";
            int? desiredEventIndex5 = desiredIndex;
            global::ScheduleLib.Cli.CalendarActionOutcome outcome6 = new(
                Action: action6,
                CalendarId: calendarId5,
                EventId: eventId3,
                State: state2,
                desiredEventIndex5);
            AddOutcome(outcome6);
            int exit2;
            if (partial)
            {
                exit2 = 6;
            }
            else
            {
                exit2 = 5;
            }

            string[] errors2 =
            [
                "Google credential refresh transport failed; the action was not sent. Completed actions remain applied.",
            ];
            global::ScheduleLib.Cli.CalendarSyncResult result = Result(
                exit2,
                errors2);
            return result;
        }
        catch (AuthenticationRequiredException)
        {
            return AuthenticationFailure();
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            return AuthenticationFailure();
        }
        catch (Google.GoogleApiException e) when (IsUnauthorizedGoogleApiError(e))
        {
            return AuthenticationFailure();
        }
        catch (Google.GoogleApiException e) when (IsHandledGoogleApiError(e))
        {
            var partial = outcomes.Any(x => x.State == "completed");
            string action7 = action;
            string? calendarId6;
            if (action == "delete calendar")
            {
                calendarId6 = existing?.Id;
            }
            else
            {
                calendarId6 = created;
            }

            string? eventId4 = eventId;
            string state3 = "failed";
            int? desiredEventIndex6 = desiredIndex;
            global::ScheduleLib.Cli.CalendarActionOutcome outcome7 = new(
                Action: action7,
                CalendarId: calendarId6,
                EventId: eventId4,
                State: state3,
                desiredEventIndex6);
            AddOutcome(outcome7);
            int exit3;
            if (partial)
            {
                exit3 = 6;
            }
            else
            {
                exit3 = 5;
            }

            string[] errors3 =
                ["Google Calendar rejected the action; it was not retried. Completed actions remain applied."];
            global::ScheduleLib.Cli.CalendarSyncResult result2 = Result(
                exit3,
                errors3);
            return result2;
        }
        catch (Exception)
        {
            if (attempted)
            {
                string action8 = action;
                string? calendarId7;
                if (action == "delete calendar")
                {
                    calendarId7 = existing?.Id;
                }
                else
                {
                    calendarId7 = created;
                }

                string? eventId5 = eventId;
                string state4 = "uncertain";
                int? desiredEventIndex7 = desiredIndex;
                global::ScheduleLib.Cli.CalendarActionOutcome outcome8 = new(
                    Action: action8,
                    CalendarId: calendarId7,
                    EventId: eventId5,
                    State: state4,
                    desiredEventIndex7);
                AddOutcome(outcome8);
            }

            int exit4;
            if (outcomes.Count > 0)
            {
                exit4 = 6;
            }
            else
            {
                exit4 = 5;
            }

            string[] errors4 =
            [
                "Calendar application failed. An uncertain action may have completed remotely and was not retried; inspect the destination before another apply.",
            ];
            global::ScheduleLib.Cli.CalendarSyncResult result3 = Result(
                exit4,
                errors4);
            return result3;
        }

        CalendarSyncResult AuthenticationFailure()
        {
            var partial = outcomes.Any(x => x.State == "completed");
            string action2 = action;
            string? calendarId;
            if (action == "delete calendar")
            {
                calendarId = existing?.Id;
            }
            else
            {
                calendarId = created;
            }

            string? eventId2 = eventId;
            string state = "failed";
            int? desiredEventIndex = desiredIndex;
            global::ScheduleLib.Cli.CalendarActionOutcome outcome = new(
                Action: action2,
                CalendarId: calendarId,
                EventId: eventId2,
                State: state,
                desiredEventIndex);
            AddOutcome(outcome);

            int CalculateExit()
            {
                int exit;
                if (partial)
                {
                    exit = 6;
                }
                else
                {
                    exit = 4;
                }

                return exit;
            }

            return Result(CalculateExit(),
                [new AuthenticationRequiredException(provider: "google", teacher: profile ?? "").Message]);
        }

        CalendarSyncResult Result(
            int exit,
            string[] errors,
            bool reportRemaining = true)
        {
            bool CalculateConditionResult()
            {
                if (apply)
                {
                    if (exit != 0)
                    {
                        return reportRemaining;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                var finished = outcomes.Select(PlanId)
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var step in plan.Where(step => !finished.Contains(step.Id)))
                {
                    var skipped = new CalendarActionOutcome(
                        Action: step.Action,
                        CalendarId: step.CalendarId,
                        EventId: null,
                        State: "not-attempted",
                        step.DesiredEventIndex);
                    string id2 = step.Id;
                    string status2 = skipped.State;
                    object item = skipped;
                    global::ScheduleLib.Cli.CalendarNotAttemptedMessage human =
                        new CalendarNotAttemptedMessage(step.Action, step.DesiredEventIndex);
                    Report(
                        id: id2,
                        status: status2,
                        item,
                        human);
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
