using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed class FreeHoursArguments : IArgumentModel
{
    [Option(
        "group",
        Description =
            "Group name from the schedule, for example IA2301. Repeat it for several groups. Unknown or ambiguous names fail before execution.")]
    public string[] Groups
    {
        get;
        set;
    } = [];
}

public sealed record FreeHoursIntervalResult(string Start, string End);

public sealed record FreeHoursDayResult(string Day, FreeHoursIntervalResult[] Intervals);

public sealed record FreeHoursSectionResult(
    string Group,
    string Parity,
    string Mode,
    FreeHoursDayResult[] Days);

public sealed record FreeHoursResult(string[] Groups, FreeHoursSectionResult[] Sections);

public partial class QueryCommands
{
    [Command(
        "free-hours",
        Description =
            "Free time slots of the supplied groups. Existing defaults are preserved and always apply: both parities (even and odd weeks), both occupancy modes, and every weekly period rather than only the latest one. The two modes are 'EveryLessonOccupies' - every lesson the group attends occupies its slot, whichever subgroup, specialization or alternative it addresses - and 'WholeGroupAndUnspecializedOptionalLessonsOccupy' - whole-group lessons and lessons with the legacy opțional subgroup marker and no specialization occupy their slot, regardless of alternative; other partitioned lessons leave their slot free. Days are the Monday-Friday slots of the configured lesson times, and consecutive free slots are merged into one interval. No teacher profile is required.")]
    public async Task<int> FreeHours(
        FreeHoursArguments groups,
        SourceArguments source,
        ResultArguments outputArgs,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        using var context = new CliCommandContext(
            "query free-hours",
            outputArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        var warnings = context.Warnings;
        FreeHoursResult? data = null;
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var requested = groups.Groups;
            if (requested.Length == 0)
            {
                return Finish(CliExit.InvalidArguments, ["At least one --group is required."]);
            }

            if (requested.Any(string.IsNullOrWhiteSpace))
            {
                return Finish(CliExit.InvalidArguments, ["Group names must not be empty."]);
            }

            var names = requested.Distinct(StringComparer.Ordinal).ToArray();
            if (names.Length != requested.Length)
            {
                warnings.Add("Repeated group names were reported once.");
            }

            using var resolvedSettings = await CliSettings.Load(
                settings ?? new(),
                cancellationToken: cancellation.Token);
            var services = context.CreateServices(
                source,
                resolvedSettings,
                ConfigureServices);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            var known = schedule.EnumerateGroups().Select(x => x.Item.Name).ToArray();
            var unknown = names.Where(name => !known.Contains(name, StringComparer.Ordinal)).ToArray();
            if (unknown.Length > 0)
            {
                return Finish(CliExit.InvalidArguments, [UnknownGroups(unknown, known)]);
            }

            var ambiguous = names.Where(name => known.Count(x => x == name) > 1).ToArray();
            if (ambiguous.Length > 0)
            {
                return Finish(
                    CliExit.InvalidArguments,
                    [$"Group name(s) match more than one group: {string.Join(", ", ambiguous)}."]);
            }

            warnings.Add("Free hours inspect every weekly period, not only the latest one.");
            var handler = scope.ServiceProvider.GetRequiredService<PrintFreeHoursOfGroupTaskHandler>();
            var sections = handler.Sections(names, cancellation.Token);
            var time = scope.ServiceProvider.GetRequiredService<LessonTimeConfig>();
            var resultSections = sections.Select(ToSection).ToArray();
            var result = new FreeHoursResult(names, resultSections);

            FreeHoursSectionResult ToSection(FreeHoursSection section)
            {
                var days = section.Days.Select(ToDay).ToArray();
                return new(
                    Group: section.Group,
                    Parity: section.Parity.ToString(),
                    Mode: section.Mode.ToString(),
                    Days: days);
            }

            FreeHoursDayResult ToDay(FreeHoursDay day)
            {
                var intervals = day.Intervals.Select(ToInterval).ToArray();
                return new(day.Day.ToString(), intervals);
            }

            FreeHoursIntervalResult ToInterval(FreeHoursInterval interval)
            {
                var start = time.GetTimeSlotInterval(interval.Start).Start.ToString("HH:mm");
                var end = time.GetTimeSlotInterval(interval.EndInclusive).End.ToString("HH:mm");
                return new(Start: start, End: end);
            }

            cancellation.Token.ThrowIfCancellationRequested();
            data = result;
            // Sections require full validation, so this batch is atomic; stream each
            // section as it is reported rather than repeating them in the summary.
            for (var i = 0; i < data.Sections.Length; i++)
            {
                var section = data.Sections[i];
                context.StreamItem(
                    i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "completed",
                    section,
                    renderHuman: () =>
                    {
                        output.WriteHuman($"{section.Group}, {section.Parity} week, {Describe(section.Mode)}");
                        foreach (var day in section.Days)
                        {
                            output.WriteHuman(
                                $"  {day.Day}: {string.Join(", ", day.Intervals.Select(x => $"{x.Start}-{x.End}"))}");
                        }
                    });
            }
            return Finish(CliExit.Success, []);
        }
        catch (Exception error)
        {
            var failure = context.ClassifyInputFailure(error);
            return Finish(failure.ExitCode, failure.Errors);
        }

        int Finish(int exit, string[] errors)
        {
            var status = CliCommandContext.Status(exit);
            var jsonStatus = CliCommandContext.Status(exit, cancellation: false);
            return context.Finish(
                new CliCompletion<FreeHoursResult?>(
                    exit,
                    errors,
                    data)
                {
                    Summary = data is null ? null : new { groups = data.Groups, sectionCount = data.Sections.Length },
                    Status = status,
                    JsonStatus = jsonStatus,
                });
        }

        static string Describe(string mode) =>
            mode == nameof(FreeHoursOccupancyMode.WholeGroupAndUnspecializedOptionalLessonsOccupy)
                ? "whole-group lessons and unspecialized optional lessons occupy their slot"
                : "every lesson occupies its slot";

        static string UnknownGroups(string[] unknown, string[] known) =>
            string.Join(
                " ",
                unknown.Select(name =>
                {
                    var suggestion = known.FirstOrDefault(x => string.Equals(
                        x,
                        name,
                        StringComparison.OrdinalIgnoreCase));
                    return $"Unknown group '{name}'." + (suggestion is null ? "" : $" Did you mean '{suggestion}'?");
                }));
    }
}
