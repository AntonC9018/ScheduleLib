using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib.Generation;

namespace ScheduleLib.Application.Core;

public sealed record FreeHoursInterval(TimeSlot Start, TimeSlot EndInclusive);
public sealed record FreeHoursDay(DayOfWeek Day, FreeHoursInterval[] Intervals);

/// <summary>Which lessons of a group are treated as occupying their time slot. The two
/// modes are the two branches the desktop task has always computed; they differ only in
/// how lessons addressed to a subgroup, a specialization or an alternative are counted,
/// and both are always reported.</summary>
public enum FreeHoursOccupancyMode
{
    /// <summary>Every lesson the group attends occupies its slot, whatever its partition:
    /// whole-group lessons and lessons addressed to a subgroup, a specialization or an
    /// alternative. A slot is free only when the group has no lesson in it at all.</summary>
    EveryLessonOccupies,
    /// <summary>Only lessons that target the whole group - <see cref="GroupPartitionKey.All"/>,
    /// meaning subgroup, specialization and alternative are all unset - occupy their slot,
    /// plus lessons that carry the legacy <c>opțional</c> subgroup marker without a
    /// specialization. Lessons addressed to a subgroup, a specialization or an alternative
    /// do not occupy anything, so their slots are reported free.</summary>
    OnlyWholeGroupLessonsOccupy,
}

public sealed record FreeHoursSection(string Group, Parity Parity, FreeHoursOccupancyMode Mode, FreeHoursDay[] Days);

[AutoConstructor]
public sealed partial class PrintFreeHoursOfGroupTaskHandler
{
    private readonly Schedule _schedule;
    private readonly DayNameProvider _dayNameProvider;
    private readonly LessonTimeConfig _timeConfig;

    public struct RunParams
    {
        public required string[] Groups;
        public required StringBuilder StringBuilder;
    }

    public void Run(RunParams p)
    {
        foreach (var section in Sections(p.Groups)) Write(section, p.StringBuilder);
    }

    /// <summary>The sections <see cref="Run"/> prints: both parities, both occupancy modes,
    /// for every supplied group, over all weekly periods. Cancellation is observed while
    /// the lessons are enumerated and before the result is handed back.</summary>
    public FreeHoursSection[] Sections(string[] groups, CancellationToken cancellationToken = default)
    {
        var sections = new List<FreeHoursSection>();
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in groups)
            {
                foreach (var mode in new[] { FreeHoursOccupancyMode.OnlyWholeGroupLessonsOccupy, FreeHoursOccupancyMode.EveryLessonOccupies })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sections.Add(Section(group, parity, mode, cancellationToken));
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return sections.ToArray();
    }

    private FreeHoursSection Section(string group, Parity parity, FreeHoursOccupancyMode mode, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groupId = _schedule.Groups
            .WithIndex()
            .Where(x => x.Item.Name == group)
            .Select(x => new GroupId(x.Index))
            .Single();
        var lessons = _schedule.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Groups.Contains(groupId) && x.Date.Parity.IsMatch(parity))
            .Where(x =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (mode == FreeHoursOccupancyMode.EveryLessonOccupies)
                {
                    return true;
                }
                var partition = x.Lesson.GroupPartitionKey;
                if (partition == GroupPartitionKey.All)
                {
                    return true;
                }
                if (partition.SubGroup == SpecialSubGroups.Optional
                    && partition.Specialization == Specialization.All)
                {
                    return true;
                }
                return false;
            });

        var allTimes = _timeConfig.TimeSlots
            .SelectMany(x => new[]
                {
                    DayOfWeek.Monday,
                    DayOfWeek.Tuesday,
                    DayOfWeek.Wednesday,
                    DayOfWeek.Thursday,
                    DayOfWeek.Friday,
                }
                .Select(y => (Day: y, Time: x)));

        var usedTimes = lessons.Select(x => (Day: x.Date.DayOfWeek, Time: x.Date.TimeSlot));
        var unusedTimes = allTimes.Except(usedTimes);

        var days = unusedTimes
            .OrderBy(x => (x.Day, x.Time))
            .GroupBy(x => x.Day)
            .Select(x =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new FreeHoursDay(x.Key, MergeConsecutive(x.Select(y => y.Time)).ToArray());
            })
            .ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        return new(group, parity, mode, days);
    }

    private static IEnumerable<FreeHoursInterval> MergeConsecutive(IEnumerable<TimeSlot> x)
    {
        using var e = x.GetEnumerator();
        if (!e.MoveNext())
        {
            yield break;
        }
        var start = e.Current;
        var prev = start;
        while (true)
        {
            if (!e.MoveNext())
            {
                yield return new(start, prev);
                yield break;
            }
            var c = e.Current;
            if (c.Index - prev.Index > 1)
            {
                yield return new(start, prev);
                start = c;
            }
            prev = c;
        }
    }

    private void Write(FreeHoursSection section, StringBuilder sb)
    {
        var displayHandler = new TimeSlotDisplayHandler();
        var parityDisplay = new ParityDisplayHandler();
        var isOptional = section.Mode == FreeHoursOccupancyMode.OnlyWholeGroupLessonsOccupy;
        sb.AppendLine($"paritatea: {parityDisplay.Get(section.Parity)}, grupa: {section.Group}, optional?: {isOptional}");
        foreach (var day in section.Days)
        {
            sb.Append(_dayNameProvider.GetDayName(day.Day));
            sb.Append(":");

            var listBuilder = new ListStringBuilder(sb, ",");
            foreach (var interval in day.Intervals)
            {
                var start = interval.Start;
                var end = interval.EndInclusive;
                var startTime = _timeConfig.GetTimeSlotInterval(start).Start;
                var endTime = _timeConfig.GetTimeSlotInterval(end).End;
                var duration = endTime - startTime;
                var intervalStr = displayHandler.IntervalDisplay(new TimeSlotInterval(startTime, duration));
                listBuilder.Append(intervalStr);
            }
            sb.AppendLine();
        }
        sb.AppendLine();
    }
}
