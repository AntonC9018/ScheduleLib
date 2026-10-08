using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib.Generation;

namespace ScheduleLib.Application.Core;

public sealed record FreeHoursInterval(TimeSlot Start, TimeSlot EndInclusive);
public sealed record FreeHoursDay(DayOfWeek Day, FreeHoursInterval[] Intervals);
public sealed record FreeHoursSection(string Group, Parity Parity, bool IncludesOptional, FreeHoursDay[] Days);

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

    /// <summary>The sections <see cref="Run"/> prints: both parities, both partition-inclusion
    /// modes, for every supplied group, over all weekly periods.</summary>
    public FreeHoursSection[] Sections(string[] groups, CancellationToken cancellationToken = default)
    {
        var sections = new List<FreeHoursSection>();
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in groups)
            {
                foreach (var isOptional in new[] { true, false })
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    sections.Add(Section(group, parity, isOptional));
                }
            }
        }
        return sections.ToArray();
    }

    private FreeHoursSection Section(string group, Parity parity, bool isOptional)
    {
        var groupId = _schedule.Groups
            .WithIndex()
            .Where(x => x.Item.Name == group)
            .Select(x => new GroupId(x.Index))
            .Single();
        var lessons = _schedule.EnumerateWeeklyLessons()
            .Where(x => x.Lesson.Groups.Contains(groupId) && x.Date.Parity.IsMatch(parity))
            .Where(x =>
            {
                if (!isOptional)
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
            .Select(x => new FreeHoursDay(x.Key, MergeConsecutive(x.Select(y => y.Time)).ToArray()))
            .ToArray();
        return new(group, parity, isOptional, days);
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
        sb.AppendLine($"paritatea: {parityDisplay.Get(section.Parity)}, grupa: {section.Group}, optional?: {section.IncludesOptional}");
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
