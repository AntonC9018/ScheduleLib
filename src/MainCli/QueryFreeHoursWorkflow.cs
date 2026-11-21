using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public sealed record FreeHoursQuery(ImmutableArray<string> Groups);

public sealed class QueryFreeHoursWorkflow(
    Schedule schedule,
    LessonTimeConfig time,
    PrintFreeHoursOfGroupTaskHandler handler,
    FreeHoursQuery query)
{
    public static void Register(IServiceCollection services) => services.AddSeed<FreeHoursQuery>();

    // groupsValidated runs once every group name has passed, before any section is computed.
    public FreeHoursResult Run(Action groupsValidated, CancellationToken cancellationToken)
    {
        var names = query.Groups.ToArray();
        var known = schedule.EnumerateGroups()
            .Select(x => x.Item.Name)
            .ToArray();
        var unknown = names.Where(name => !known.Contains(name, StringComparer.Ordinal))
            .ToArray();
        if (unknown.Length > 0)
        {
            throw new UnknownGroupsException(unknown: unknown, known: known);
        }

        bool IsAmbiguous(string name) => known.Count(x => x == name) > 1;

        var ambiguous = names.Where(IsAmbiguous)
            .ToArray();
        if (ambiguous.Length > 0)
        {
            throw new AmbiguousGroupsException(ambiguous);
        }

        groupsValidated();
        var sections = handler.Sections(names, cancellationToken)
            .Select(ToSection)
            .ToArray();
        return new FreeHoursResult(names, sections);
    }

    private FreeHoursSectionResult ToSection(FreeHoursSection section)
    {
        var days = section.Days.Select(ToDay)
            .ToArray();
        return new(
            Group: section.Group,
            Parity: section.Parity.ToString(),
            Mode: section.Mode.ToString(),
            Days: days);
    }

    private FreeHoursDayResult ToDay(FreeHoursDay day)
    {
        var intervals = day.Intervals.Select(ToInterval)
            .ToArray();
        return new(day.Day.ToString(), intervals);
    }

    private FreeHoursIntervalResult ToInterval(FreeHoursInterval interval)
    {
        var start = time.GetTimeSlotInterval(interval.Start).Start
            .ToString("HH:mm");
        var end = time.GetTimeSlotInterval(interval.EndInclusive).End
            .ToString("HH:mm");
        return new(Start: start, End: end);
    }
}

public sealed class UnknownGroupsException(IReadOnlyList<string> unknown, IReadOnlyList<string> known)
    : ArgumentException(Describe(unknown: unknown, known: known))
{
    public IReadOnlyList<string> Unknown { get; } = unknown;

    private static string Describe(IReadOnlyList<string> unknown, IReadOnlyList<string> known)
    {
        string DescribeOne(string name)
        {
            var suggestion = known.FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (suggestion is null)
            {
                return $"Unknown group '{name}'.";
            }

            return $"Unknown group '{name}'. Did you mean '{suggestion}'?";
        }

        return string.Join(" ", unknown.Select(DescribeOne));
    }
}

public sealed class AmbiguousGroupsException(IReadOnlyList<string> ambiguous)
    : ArgumentException($"Group name(s) match more than one group: {string.Join(", ", ambiguous)}.")
{
    public IReadOnlyList<string> Ambiguous { get; } = ambiguous;
}
