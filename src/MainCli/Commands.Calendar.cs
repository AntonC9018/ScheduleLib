using System.Text.Json;
using Anton.LayeredData.Retrieval;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public CalendarCommands Calendar { get; set; } = new();
}

[Command("calendar", Description = "Preview or apply named Google Calendar replacement using provisioned authorization.")]
public class CalendarCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) => AppConfiguration.ConfigureServices(services);
    protected virtual Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
        => GoogleCalendarSyncProvider.Connect(services, config, token);

    [Command("sync", Description = "Preview the configured teacher's filtered recurring events and calendar/ID replacement. --apply recomputes current state and replaces the first calendar with the configured name. Requires auth login google; never opens consent.")]
    public async Task<int> Sync(SourceArguments source, SettingsArguments settings, ResultArguments output, ApplyArguments apply, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        CalendarSyncResult? data = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is null) return Finish(3, ["A teacher --profile is required. Use config profiles to list identities."]);
            var configured = resolved.Get(GoogleCalendarConfig.Key);
            if (configured?.Credentials is null) return Finish(3, ["Google Calendar credentials are not configured for this profile."]);
            if (configured.CalendarName?.Equals("primary", StringComparison.OrdinalIgnoreCase) == true)
                return Finish(3, ["Primary calendars cannot be replaced; configure a named secondary calendar."]);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolved.ProjectDirectory);
            resolved.ConfigureServices(services);
            await using var container = AppConfiguration.BuildServiceProvider(services);
            await container.InitializeSchedule(cancellation.Token);
            await using var scope = container.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>().Get();
            if (config is null) return Finish(3, ["Google Calendar configuration is missing for this profile."]);
            var teacher = scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
            if (scope.ServiceProvider.GetRequiredService<LookupFacade>().Teacher(teacher.TeacherName.ToNameModel()) is null)
                return Finish(3, [$"Selected teacher {resolved.Profile} is absent from this schedule."]);
            var desired = scope.ServiceProvider.GetRequiredService<UpdateLessonsInGoogleCalendarTaskHandler>().BuildDesiredEvents(cancellation.Token);
            using var provider = await Connect(scope.ServiceProvider, config, cancellation.Token);
            data = await CalendarSync.Run(provider, config.CalendarName, desired, apply.Apply, cancellation.Token);
            return Finish(data.ExitCode, data.Errors);
        }
        catch (AuthenticationRequiredException e) { return Finish(4, [e.Message]); }
        catch (OperationCanceledException) { return Finish(130, ["Cancelled."]); }
        catch (LocalOperationBusyException) { return Finish(7, ["Another calendar operation owns this account/destination."]); }
        catch (JsonException e) { return Finish(3, [e.Message]); }
        catch (ArgumentException e) { return Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return Finish(3, [e.Message]); }
        catch (PlatformNotSupportedException e) { return Finish(8, [e.Message]); }
        catch (IOException) { return Finish(5, ["Could not read calendar inputs or remote state."]); }
        catch (UnauthorizedAccessException) { return Finish(5, ["Calendar inputs or local lock state are inaccessible."]); }
        catch (Google.GoogleApiException) { return Finish(5, ["Google Calendar rejected the remote state request."]); }
        catch (Exception) { return Finish(1, ["Calendar synchronization failed before application."]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, string[] errors)
        {
            var warnings = data?.Warnings ?? [];
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            var actions = data is null ? [] : (data.ReplacedCalendar is null ? Array.Empty<string>()
                : new[] { $"Delete calendar {data.ReplacedCalendar.Id} and its {data.DeletedEvents.Count} events" })
                .Concat(new[] { $"Create calendar {data.CalendarName} with a new ID", $"Create {data.DesiredEvents.Count} events with new IDs" }).ToArray();
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : exit == 6 ? "partial" : "failed";
            if (output.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<CalendarSyncResult?>(1, "calendar sync", runId, status,
                    exit, [], actions, warnings, errors, data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (data is not null)
            {
                Console.WriteLine($"{status}: {data.Account} / {data.CalendarName}");
                foreach (var action in actions) Console.WriteLine(action);
                foreach (var ev in data.DeletedEvents) Console.WriteLine($"Delete event {ev.Id}: {ev.Summary}");
                for (var index = 0; index < data.DesiredEvents.Count; index++)
                {
                    var ev = data.DesiredEvents[index];
                    Console.WriteLine($"Create event [{index}]: {ev.Summary}; {ev.Start.DateTimeDateTimeOffset}–{ev.End.DateTimeDateTimeOffset}; {string.Join(", ", ev.Recurrence ?? [])}");
                }
                foreach (var outcome in data.Outcomes) Console.WriteLine($"{outcome.Action}: {outcome.State}; calendar {outcome.CalendarId}; event {outcome.EventId}; desired index {outcome.DesiredEventIndex}");
            }
            return exit;
        }
    }
}
