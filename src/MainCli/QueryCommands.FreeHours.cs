using CommandDotNet;
using ScheduleLib.Application.Core;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public sealed class FreeHoursArguments : IArgumentModel
{
    [Option(
        "group",
        Description =
            "Group name from the schedule, for example IA2301. Repeat it for several groups. Unknown or ambiguous names fail before execution.")]
    public string[] Groups { get; set; } = [];
}

public sealed record FreeHoursIntervalResult(string Start, string End);

public sealed record FreeHoursDayResult(string Day, FreeHoursIntervalResult[] Intervals);

public sealed record FreeHoursSectionResult(
    string Group,
    string Parity,
    string Mode,
    FreeHoursDayResult[] Days);

public sealed record FreeHoursResult(string[] Groups, FreeHoursSectionResult[] Sections);

[Command(
    "free-hours",
    Description =
        "Free time slots of the supplied groups. Existing defaults are preserved and always apply: both parities (even and odd weeks), both occupancy modes, and every weekly period rather than only the latest one. The two modes are 'EveryLessonOccupies' - every lesson the group attends occupies its slot, whichever subgroup, specialization or alternative it addresses - and 'WholeGroupAndUnspecializedOptionalLessonsOccupy' - whole-group lessons and lessons with the legacy opțional subgroup marker and no specialization occupy their slot, regardless of alternative; other partitioned lessons leave their slot free. Days are the Monday-Friday slots of the configured lesson times, and consecutive free slots are merged into one interval. No teacher profile is required.")]
public sealed class QueryFreeHoursCommand(ScopeForkFactory<QueryFreeHoursWorkflow> workflows)
{
    [DefaultCommand]
    public async Task<int> Run(
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

            var names = requested.Distinct(StringComparer.Ordinal)
                .ToArray();
            if (names.Length != requested.Length)
            {
                warnings.Add("Repeated group names were reported once.");
            }

            var sourceSelection = ScheduleSourceSelection.From(source);
            var settingsSelection = SettingsSelection.From(settings);
            var query = new FreeHoursQuery(names);
            await using var scope = await workflows.OpenAsync(
                inputs => inputs.Add(settingsSelection)
                    .Add(sourceSelection)
                    .Add(query),
                cancellation.Token);
            var result = scope.Handler.Run(cancellation.Token);
            warnings.Add("Free hours inspect every weekly period, not only the latest one.");
            cancellation.Token.ThrowIfCancellationRequested();
            data = result;
            // Sections require full validation, so this batch is atomic; stream each
            // section as it is reported rather than repeating them in the summary.
            for (var i = 0; i < data.Sections.Length; i++)
            {
                var section = data.Sections[i];
                var sectionId = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                context.StreamItem(
                    id: sectionId,
                    status: ActionState.Completed,
                    section,
                    renderHuman: () => RenderSection(section));
            }
            return Finish(CliExit.Success, []);
        }
        catch (Exception error)
        {
            var failure = context.ClassifyInputFailure(error);
            return Finish(failure.ExitCode, failure.Errors);
        }

        void RenderSection(FreeHoursSectionResult section)
        {
            output.WriteHuman($"{section.Group}, {section.Parity} week, {Describe(section.Mode)}");
            foreach (var day in section.Days)
            {
                var line =
                    $"  {day.Day}: {string.Join(", ", day.Intervals.Select(x => $"{x.Start}-{x.End}"))}";
                output.WriteHuman(line);
            }
        }

        int Finish(int exit, string[] errors)
        {
            var status = CliCommandContext.Status(exit);
            var jsonStatus = CliCommandContext.Status(exit, cancellation: false);
            return context.Finish(new CliCompletion<FreeHoursResult?>(
                exit,
                errors,
                data)
            {
                Summary = data is null
                    ? null
                    : new
                    {
                        groups = data.Groups,
                        sectionCount = data.Sections.Length,
                    },
                Status = status,
                JsonStatus = jsonStatus,
            });
        }

        static string Describe(string mode)
        {
            if (mode != nameof(FreeHoursOccupancyMode.WholeGroupAndUnspecializedOptionalLessonsOccupy))
            {
                return "every lesson occupies its slot";
            }

            return "whole-group lessons and unspecialized optional lessons occupy their slot";
        }
    }
}
