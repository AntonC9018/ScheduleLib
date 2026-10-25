using System.Diagnostics.CodeAnalysis;
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

[Command(
    "calendar",
    Description = "Preview or apply named Google Calendar replacement using provisioned authorization.")]
[SuppressMessage(category: "Readability", checkId: "CR1400",
    Justification = "Virtual command seams await architecture review in issue #247.")]
public class CalendarCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) =>
        AppConfiguration.ConfigureServices(services);

    protected virtual Task<ICalendarSyncProvider> Connect(
        IServiceProvider services,
        BuiltGoogleCalendarConfig config,
        CancellationToken token)
    {
        return GoogleCalendarSyncProvider.Connect(
                services,
                config,
                token);
    }

    [Command(
        "sync",
        Description =
            "Preview the configured teacher's filtered recurring events and calendar/ID replacement. --apply recomputes current state and replaces the first calendar with the configured name. Requires auth login google; never opens consent.")]
    public async Task<int> Sync(
        SourceArguments source,
        SettingsArguments settings,
        ResultArguments outputArgs,
        ApplyArguments apply,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "calendar sync",
            outputArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        CalendarSyncResult? data = null;
        var cancellation = context.Cancellation;
        try
        {
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is null)
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    ["A teacher --profile is required. Use config profiles to list identities."]);
            }

            var configured = resolved.Get(GoogleCalendarConfig.Key);
            if (configured?.Credentials is null)
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    ["Google Calendar credentials are not configured for this profile."]);
            }

            if (configured.CalendarName?.Equals("primary", StringComparison.OrdinalIgnoreCase) == true)
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    ["Primary calendars cannot be replaced; configure a named secondary calendar."]);
            }

            var services = context.CreateServices(
                source,
                resolved,
                ConfigureServices);
            await using var container = AppConfiguration.BuildServiceProvider(services);
            await container.InitializeSchedule(cancellation.Token);
            await using var scope = container.CreateAsyncScope();
            var config = scope.ServiceProvider.GetRequiredService<DataProvider<BuiltGoogleCalendarConfig>>()
                .Get();
            if (config is null)
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    ["Google Calendar configuration is missing for this profile."]);
            }

            var teacher = scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
            if (TeacherProfileScheduleLookup.Find(
                    scope.ServiceProvider.GetRequiredService<LookupFacade>(),
                    teacher.TeacherName) is null)
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    [$"Selected teacher {resolved.Profile} is absent from this schedule."]);
            }

            var desired = scope.ServiceProvider.GetRequiredService<UpdateLessonsInGoogleCalendarTaskHandler>()
                .BuildDesiredEvents(cancellation.Token);
            using var provider = await Connect(
                scope.ServiceProvider,
                config,
                cancellation.Token);
            // Planning (reads) completes before mutations; the plan, the read
            // selection and each applied outcome stream as produced with stable
            // plan-order ids.
            data = await CalendarSync.Run(
                provider,
                name: config.CalendarName,
                desired,
                apply.Apply,
                cancellation.Token,
                profile: resolved.Profile,
                onOutcome: context.StreamUpdate);
            return Finish(data.ExitCode, data.Errors);
        }
        catch (CalendarStreamingException ex)
        {
            data = ex.PartialResult;
            return Finish(data.ExitCode, data.Errors);
        }
        catch (Exception error)
        {
            var messages = new GoogleFailureMessages
            {
                Timeout = "Calendar request timed out or was interrupted before application.",
                Network = "Calendar remote state request failed. Check the connection.",
                Busy = "Another calendar operation owns this account/destination.",
                Access = "Calendar inputs or local lock state are inaccessible.",
                Rejected = "Google Calendar rejected the remote state request.",
                Unexpected = "Calendar synchronization failed before application.",
                Io = "Could not read calendar inputs or remote state.",
                IncludeIoDetails = false,
            };
            var failure = CliRemoteFailure.ClassifyGoogle(
                error,
                context,
                settings.Profile,
                messages);
            return Finish(failure.ExitCode, failure.Errors);
        }

        int Finish(int exit, string[] errors)
        {
            var warnings = data?.Warnings ?? [];
            var actions = PlannedActions(data);
            var success = "preview";
            if (apply.Apply)
            {
                success = "applied";
            }
            var status = CliCommandContext.Status(exit, success);
            object? summary = null;
            if (data is not null)
            {
                summary = new
                {
                    data.Account,
                    data.CalendarName,
                    replacedCalendarId = data.ReplacedCalendar?.Id,
                    data.CreatedCalendarId,
                    desiredCount = data.DesiredEvents.Count,
                    deletedCount = data.DeletedEvents.Count,
                    outcomeCount = data.Outcomes.Count,
                };
            }
            return context.Finish(new CliCompletion<CalendarSyncResult?>(
                exit,
                errors,
                data)
            {
                Summary = summary,
                ActionMessages = actions,
                Warnings = warnings,
                Status = status,
                RenderHuman = status =>
                {
                    if (data is not null)
                    {
                        global::ScheduleLib.Cli.CliOutput cliOutput = output;
                        string line = $"{status}: {data.Account} / {data.CalendarName}";
                        cliOutput.WriteHuman(line);
                    }
                },
            });
        }
    }

    private static ICliMessage[] PlannedActions(CalendarSyncResult? data)
    {
        if (data is null)
        {
            return [];
        }
        var actions = new List<ICliMessage>();
        if (data.ReplacedCalendar is { } calendar)
        {
            global::System.Collections.Generic.List<global::ScheduleLib.Cli.ICliMessage> list = actions;
            global::ScheduleLib.Cli.CalendarDeleteAction item = new CalendarDeleteAction(calendar.Id, data.DeletedEvents.Count);
            global::ScheduleLib.Cli.ICliMessage item2 = item;
            list.Add(item2);
        }
        global::System.Collections.Generic.List<global::ScheduleLib.Cli.ICliMessage> list2 = actions;
        global::ScheduleLib.Cli.CalendarCreateAction item3 = new CalendarCreateAction(data.CalendarName);
        global::ScheduleLib.Cli.ICliMessage item4 = item3;
        list2.Add(item4);
        global::System.Collections.Generic.List<global::ScheduleLib.Cli.ICliMessage> list3 = actions;
        global::ScheduleLib.Cli.CalendarCreateEventsAction item5 = new CalendarCreateEventsAction(data.DesiredEvents.Count);
        global::ScheduleLib.Cli.ICliMessage item6 = item5;
        list3.Add(item6);
        return actions.ToArray();
    }
}
