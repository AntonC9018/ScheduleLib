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
    public async Task<int> Sync(SourceArguments source, SettingsArguments settings, ResultArguments outputArgs, ApplyArguments apply, CancellationToken cancellationToken = default)
    {
        const string command = "calendar sync";
        var runId = Guid.NewGuid().ToString("N");
        if (CliOutput.ExclusiveError(outputArgs) is { } exclusive)
        {
            Console.Error.WriteLine(exclusive);
            return 2;
        }
        var output = new CliOutput(command, runId, outputArgs);
        output.EmitStart();
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
            if (output.Jsonl) CliLogging.ReplaceWithJsonl(services, output);
            CliAmbient.Current = output.Jsonl ? output : null;
            await using var container = AppConfiguration.BuildServiceProvider(services);
            await container.InitializeSchedule(cancellation.Token);
            await using var scope = container.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>().Get();
            if (config is null) return Finish(3, ["Google Calendar configuration is missing for this profile."]);
            var teacher = scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
            if (TeacherProfileScheduleLookup.Find(scope.ServiceProvider.GetRequiredService<LookupFacade>(), teacher.TeacherName) is null)
                return Finish(3, [$"Selected teacher {resolved.Profile} is absent from this schedule."]);
            var desired = scope.ServiceProvider.GetRequiredService<UpdateLessonsInGoogleCalendarTaskHandler>().BuildDesiredEvents(cancellation.Token);
            using var provider = await Connect(scope.ServiceProvider, config, cancellation.Token);
            // Planning (reads) completes before mutations; the plan, the read
            // selection and each applied outcome stream as produced with stable
            // plan-order ids.
            data = await CalendarSync.Run(provider, config.CalendarName, desired, apply.Apply, cancellation.Token, profile: resolved.Profile,
                onOutcome: StreamUpdate);
            return Finish(data.ExitCode, data.Errors);
        }
        catch (CalendarStreamingException ex) { data = ex.PartialResult; return Finish(data.ExitCode, data.Errors); }
        catch (CliOutputStreamingException) { return Finish(1, ["Output streaming failed."]); }
        catch (AuthenticationRequiredException e) { return Finish(4, [e.Message]); }
        catch (Exception error) when (cancellation.IsCancellationRequested
            && error is OperationCanceledException or GooglePreSendTransportException) { return Finish(130, ["Cancelled."]); }
        catch (OperationCanceledException) { return Finish(5, ["Calendar request timed out or was interrupted before application."]); }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException) { return Finish(4, [new AuthenticationRequiredException("google", settings.Profile ?? "").Message]); }
        catch (HttpRequestException) { return Finish(5, ["Calendar remote state request failed. Check the connection."]); }
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
        catch (Google.GoogleApiException e) when ((int)e.HttpStatusCode == 401) { return Finish(4, [new AuthenticationRequiredException("google", settings.Profile ?? "").Message]); }
        catch (Google.GoogleApiException) { return Finish(5, ["Google Calendar rejected the remote state request."]); }
        catch (Exception) { return Finish(1, ["Calendar synchronization failed before application."]); }
        finally { Console.CancelKeyPress -= cancel; CliAmbient.Current = null; }

        void StreamUpdate((string Id, string Status, object? Item, string Human) update)
        {
            try
            {
                output.WriteHuman(update.Human);
                output.EmitItem(update.Id, update.Status, update.Item);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new CliOutputStreamingException("Output streaming failed.", ex);
            }
        }

        int Finish(int exit, string[] errors)
        {
            var warnings = data?.Warnings ?? [];
            foreach (var error in errors) output.WriteError(error);
            foreach (var warning in warnings) output.WriteWarning(warning);
            var actions = data is null ? [] : (data.ReplacedCalendar is null ? Array.Empty<string>()
                : new[] { $"Delete calendar {data.ReplacedCalendar.Id} and its {data.DeletedEvents.Count} events" })
                .Concat(new[] { $"Create calendar {data.CalendarName} with a new ID", $"Create {data.DesiredEvents.Count} events with new IDs" }).ToArray();
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : exit == 6 ? "partial" : "failed";
            if (outputArgs.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<CalendarSyncResult?>(1, command, runId, status,
                    exit, [], actions, warnings, errors, data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (output.Jsonl)
            {
                output.EmitFinal(status, exit, warnings, errors, data is null ? null : new
                {
                    data.Account,
                    data.CalendarName,
                    replacedCalendarId = data.ReplacedCalendar?.Id,
                    data.CreatedCalendarId,
                    desiredCount = data.DesiredEvents.Count,
                    deletedCount = data.DeletedEvents.Count,
                    outcomeCount = data.Outcomes.Count,
                });
            }
            else if (data is not null)
            {
                // Planned actions and the read selection already streamed above;
                // the final summary does not repeat them.
                output.WriteHuman($"{status}: {data.Account} / {data.CalendarName}");
            }
            return exit;
        }
    }
}
