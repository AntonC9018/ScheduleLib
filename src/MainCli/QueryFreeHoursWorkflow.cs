using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

/// <summary>The distinct, non-empty group names of one <c>query free-hours</c> invocation. A seed.</summary>
public sealed record FreeHoursQuery(IReadOnlyList<string> Groups);

/// <summary>The free hours of the groups of a <see cref="FreeHoursQuery"/>, over every weekly period. A workflow root.</summary>
public sealed class QueryFreeHoursWorkflow(
    Schedule schedule,
    LessonTimeConfig time,
    PrintFreeHoursOfGroupTaskHandler handler,
    FreeHoursQuery query)
{
    public static void Register(IServiceCollection services) => services.AddSeed<FreeHoursQuery>();

    /// <summary>Computes every section. Unknown or ambiguous group names fail before any is computed.</summary>
    public FreeHoursResult Run(CancellationToken cancellationToken)
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

/// <summary>A free-hours query names groups the schedule does not have.</summary>
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

/// <summary>A free-hours query names groups that match more than one group of the schedule.</summary>
public sealed class AmbiguousGroupsException(IReadOnlyList<string> ambiguous)
    : ArgumentException($"Group name(s) match more than one group: {string.Join(", ", ambiguous)}.")
{
    public IReadOnlyList<string> Ambiguous { get; } = ambiguous;
}
