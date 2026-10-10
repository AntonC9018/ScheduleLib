using System.Security.Cryptography;
using System.Text;
using Google.Apis.Calendar.v3.Data;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using Event = Google.Apis.Calendar.v3.Data.Event;

namespace ScheduleLib.Cli;

public sealed record CalendarDestination(string Id, string Summary, bool Primary);
public sealed record CalendarEventIdentity(string Id, string? Summary);
public sealed record CalendarActionOutcome(string Action, string? CalendarId, string? EventId, string State, int? DesiredEventIndex = null);
public sealed record CalendarSyncResult(string Account, string CalendarName, CalendarDestination? ReplacedCalendar,
    IReadOnlyList<CalendarEventIdentity> DeletedEvents, IReadOnlyList<Event> DesiredEvents, string? CreatedCalendarId,
    IReadOnlyList<CalendarActionOutcome> Outcomes, int ExitCode, string[] Errors, string[] Warnings);

/// <summary>Mutations execute once: a failed response may conceal a successful remote create.</summary>
public interface ICalendarSyncProvider : IDisposable
{
    Task<string> GetAccount(CancellationToken token);
    Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token);
    Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string calendarId, CancellationToken token);
    Task DeleteCalendar(string calendarId, CancellationToken token);
    Task<string> CreateCalendar(string summary, CancellationToken token);
    Task<string> CreateEvent(string calendarId, Event desired, CancellationToken token);
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
    public static string LockPath(string root, string account, string name) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + name))) + ".lock");

    public static async Task<CalendarSyncResult> Run(ICalendarSyncProvider provider, string name,
        IReadOnlyList<Event> desired, bool apply, CancellationToken token, string? lockRoot = null, string? profile = null,
        Action<(string Id, string Status, object? Item, string Human)>? onOutcome = null)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Equals("primary", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Primary calendars cannot be replaced; configure a named secondary calendar.");
        var account = await provider.GetAccount(token);
        await using var lease = apply ? await LocalFileLock.Acquire(LockPath(lockRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "remote-locks", "calendar"), account, name), token) : null;
        // Every invocation recomputes. Apply reads the current destination only after owning its lock.
        var calendars = await provider.ListCalendars(token);
        var existing = calendars.FirstOrDefault(x => x.Summary == name);
        if (existing?.Primary == true || existing?.Id == account || existing?.Id == "primary")
            throw new ArgumentException("The matching destination is the primary calendar; replacement is prohibited.");
        var deleted = existing is null ? [] : await provider.ListEvents(existing.Id, token);
        var warnings = new List<string> { "Replacement creates a new calendar ID and new event IDs; no rollback is provided." };
        if (desired.Count == 0) warnings.Add("No desired events: apply still replaces the calendar with an empty calendar.");
        if (calendars.Count(x => x.Summary == name) > 1) warnings.Add("Multiple calendars share this name; only the first matching calendar is replaced, preserving existing behavior.");
        var outcomes = new List<CalendarActionOutcome>();
        string? created = null;
        // Stable plan-order ids shared by preview and apply and by each action's
        // planned record and its later terminal transition.
        var plan = new List<(string Id, string Action, string? CalendarId, int? DesiredEventIndex)>();
        if (existing is not null)
            plan.Add((Id: plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Action: "delete calendar", CalendarId: existing.Id, DesiredEventIndex: null));
        plan.Add((Id: plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Action: "create calendar", CalendarId: null, DesiredEventIndex: null));
        for (var planIndex = 0; planIndex < desired.Count; planIndex++)
            plan.Add((Id: plan.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), Action: "create event", CalendarId: null, DesiredEventIndex: planIndex));
        void Report(string id, string status, object? item, string human)
        {
            try
            {
                onOutcome?.Invoke((Id: id, Status: status, Item: item, Human: human));
            }
            catch (CliOutputStreamingException ex) { throw StreamingFailure(ex); }
            catch (Exception ex) when (ex is not OperationCanceledException) { throw StreamingFailure(new CliOutputStreamingException("Calendar output streaming failed.", ex)); }
        }
        CalendarStreamingException StreamingFailure(Exception inner) => new(Result(
            outcomes.Any(x => x.State == "completed") ? 6 : 1,
            ["Output streaming failed. Completed actions remain applied; inspect the destination before another apply."], reportRemaining: false), inner);
        void ReportPlanned((string Id, string Action, string? CalendarId, int? DesiredEventIndex) step)
        {
            var outcome = new CalendarActionOutcome(step.Action, step.CalendarId, null, "planned", step.DesiredEventIndex);
            Report(step.Id, outcome.State, outcome,
                $"{outcome.Action}: {outcome.State}; calendar {outcome.CalendarId}; event {outcome.EventId}; desired index {outcome.DesiredEventIndex}");
        }
        void AddOutcome(CalendarActionOutcome outcome)
        {
            // The completed remote outcome is recorded before rendering so a
            // rendering failure cannot change it or trigger another mutation.
            outcomes.Add(outcome);
            Report(PlanId(outcome), outcome.State, outcome,
                $"{outcome.Action}: {outcome.State}; calendar {outcome.CalendarId}; event {outcome.EventId}; desired index {outcome.DesiredEventIndex}");
        }
        string PlanId(CalendarActionOutcome outcome)
        {
            foreach (var step in plan)
            {
                if (step.Action == outcome.Action && step.DesiredEventIndex == outcome.DesiredEventIndex)
                    return step.Id;
            }
            return outcomes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        // Selection content streams as items in both modes; the final summary
        // keeps counts only and never repeats these arrays.
        foreach (var step in plan) ReportPlanned(step);
        for (var deletedIndex = 0; deletedIndex < deleted.Count; deletedIndex++)
        {
            var identity = deleted[deletedIndex];
            Report("deleted-" + deletedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), "completed", identity,
                $"Delete event {identity.Id}: {identity.Summary}");
        }
        for (var index = 0; index < desired.Count; index++)
        {
            var ev = desired[index];
            Report("desired-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), "completed",
                new { index, summary = ev.Summary, start = ev.Start?.DateTimeDateTimeOffset?.ToString("o", System.Globalization.CultureInfo.InvariantCulture), end = ev.End?.DateTimeDateTimeOffset?.ToString("o", System.Globalization.CultureInfo.InvariantCulture), recurrence = ev.Recurrence },
                $"Create event [{index}]: {ev.Summary}; {ev.Start?.DateTimeDateTimeOffset}–{ev.End?.DateTimeDateTimeOffset}; {string.Join(", ", ev.Recurrence ?? [])}");
        }
        if (!apply) return Result(0, []);
        var action = "delete calendar";
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
                AddOutcome(new(action, existing.Id, null, "completed"));
            }
            action = "create calendar";
            attempted = false;
            token.ThrowIfCancellationRequested();
            attempted = true;
            created = await provider.CreateCalendar(name, token);
            if (string.IsNullOrWhiteSpace(created)) throw new IOException("Calendar create returned no identity.");
            AddOutcome(new(action, created, null, "completed"));
            for (var index = 0; index < desired.Count; index++)
            {
                var ev = desired[index];
                desiredIndex = index;
                action = "create event";
                eventId = null;
                attempted = false;
                token.ThrowIfCancellationRequested();
                attempted = true;
                eventId = await provider.CreateEvent(created, ev, token);
                if (string.IsNullOrWhiteSpace(eventId)) throw new IOException("Event create returned no identity.");
                AddOutcome(new(action, created, eventId, "completed", desiredIndex));
            }
            return Result(0, []);
        }
        catch (CliOutputStreamingException)
        {
            // Rendering already recorded the completed remote outcome above; it
            // must not be reclassified as uncertain/failed or retried.
            throw;
        }
        catch (Exception error) when (token.IsCancellationRequested
            && error is OperationCanceledException or GooglePreSendTransportException)
        {
            if (attempted) AddOutcome(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "uncertain", desiredIndex));
            return Result(130, ["Cancelled. Any uncertain action may have completed remotely; inspect the destination before another apply."]);
        }
        catch (GooglePreSendTransportException)
        {
            var partial = outcomes.Any(x => x.State == "completed");
            AddOutcome(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 5, ["Google credential refresh transport failed; the action was not sent. Completed actions remain applied."]);
        }
        catch (AuthenticationRequiredException) { return AuthenticationFailure(); }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException) { return AuthenticationFailure(); }
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode == 401) { return AuthenticationFailure(); }
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 403 or 404 or 409 or 412 or 422)
        {
            var partial = outcomes.Any(x => x.State == "completed");
            AddOutcome(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 5, ["Google Calendar rejected the action; it was not retried. Completed actions remain applied."]);
        }
        catch (Exception)
        {
            if (attempted) AddOutcome(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "uncertain", desiredIndex));
            return Result(outcomes.Count > 0 ? 6 : 5, ["Calendar application failed. An uncertain action may have completed remotely and was not retried; inspect the destination before another apply."]);
        }

        CalendarSyncResult AuthenticationFailure()
        {
            var partial = outcomes.Any(x => x.State == "completed");
            AddOutcome(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 4, [new AuthenticationRequiredException("google", profile ?? "").Message]);
        }

        CalendarSyncResult Result(int exit, string[] errors, bool reportRemaining = true)
        {
            if (apply && exit != 0 && reportRemaining)
            {
                var finished = outcomes.Select(PlanId).ToHashSet(StringComparer.Ordinal);
                foreach (var step in plan.Where(step => !finished.Contains(step.Id)))
                {
                    var skipped = new CalendarActionOutcome(step.Action, step.CalendarId, null, "not-attempted", step.DesiredEventIndex);
                    Report(step.Id, skipped.State, skipped, $"{step.Action}: not-attempted; desired index {step.DesiredEventIndex}");
                }
            }
            return new(account, name, existing, deleted, desired, created, outcomes, exit, errors, warnings.ToArray());
        }
    }
}
