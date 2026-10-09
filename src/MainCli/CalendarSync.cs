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

public static class CalendarSync
{
    public static string LockPath(string root, string account, string name) => Path.Combine(root,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "\n" + name))) + ".lock");

    public static async Task<CalendarSyncResult> Run(ICalendarSyncProvider provider, string name,
        IReadOnlyList<Event> desired, bool apply, CancellationToken token, string? lockRoot = null, string? profile = null)
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
                outcomes.Add(new(action, existing.Id, null, "completed"));
            }
            action = "create calendar";
            attempted = false;
            token.ThrowIfCancellationRequested();
            attempted = true;
            created = await provider.CreateCalendar(name, token);
            if (string.IsNullOrWhiteSpace(created)) throw new IOException("Calendar create returned no identity.");
            outcomes.Add(new(action, created, null, "completed"));
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
                outcomes.Add(new(action, created, eventId, "completed", desiredIndex));
            }
            return Result(0, []);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (attempted) outcomes.Add(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "uncertain", desiredIndex));
            return Result(130, ["Cancelled. Any uncertain action may have completed remotely; inspect the destination before another apply."]);
        }
        catch (GooglePreSendTransportException)
        {
            var partial = outcomes.Any(x => x.State == "completed");
            outcomes.Add(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 5, ["Google credential refresh transport failed; the action was not sent. Completed actions remain applied."]);
        }
        catch (AuthenticationRequiredException) { return AuthenticationFailure(); }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException) { return AuthenticationFailure(); }
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode == 401) { return AuthenticationFailure(); }
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode is 400 or 403 or 404 or 409 or 412 or 422)
        {
            var partial = outcomes.Any(x => x.State == "completed");
            outcomes.Add(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 5, ["Google Calendar rejected the action; it was not retried. Completed actions remain applied."]);
        }
        catch (Exception)
        {
            if (attempted) outcomes.Add(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "uncertain", desiredIndex));
            return Result(outcomes.Count > 0 ? 6 : 5, ["Calendar application failed. An uncertain action may have completed remotely and was not retried; inspect the destination before another apply."]);
        }

        CalendarSyncResult AuthenticationFailure()
        {
            var partial = outcomes.Any(x => x.State == "completed");
            outcomes.Add(new(action, action == "delete calendar" ? existing?.Id : created, eventId, "failed", desiredIndex));
            return Result(partial ? 6 : 4, [new AuthenticationRequiredException("google", profile ?? "").Message]);
        }

        CalendarSyncResult Result(int exit, string[] errors) => new(account, name, existing, deleted, desired, created, outcomes, exit, errors, warnings.ToArray());
    }
}
