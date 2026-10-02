using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib.Generation;

namespace ScheduleLib.Application.Core;

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
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in p.Groups)
            {
                foreach (var isOptional in new[] { true, false })
                {
                    var displayHandler = new TimeSlotDisplayHandler();
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

                    var orderedTimes = unusedTimes.OrderBy(x => (x.Day, x.Time));
                    var byDay = orderedTimes
                        .GroupBy(x => x.Day)
                        .Select(x => (Day: x.Key, Times: MergeConsecutive(x.Select(y => y.Time))));

                    var parityDisplay = new ParityDisplayHandler();
                    p.StringBuilder.AppendLine($"paritatea: {parityDisplay.Get(parity)}, grupa: {group}, optional?: {isOptional}");
                    foreach (var day in byDay)
                    {
                        p.StringBuilder.Append(_dayNameProvider.GetDayName(day.Day));
                        p.StringBuilder.Append(":");

                        var listBuilder = new ListStringBuilder(p.StringBuilder, ",");
                        foreach (var time in day.Times)
                        {
                            var start = time.Start;
                            var end = time.EndInclusive;
                            var startTime = _timeConfig.GetTimeSlotInterval(start).Start;
                            var endTime = _timeConfig.GetTimeSlotInterval(end).End;
                            var duration = endTime - startTime;
                            var intervalStr = displayHandler.IntervalDisplay(new TimeSlotInterval(startTime, duration));
                            listBuilder.Append(intervalStr);
                        }
                        p.StringBuilder.AppendLine();
                    }
                    p.StringBuilder.AppendLine();
                    continue;


                    IEnumerable<(TimeSlot Start, TimeSlot EndInclusive)> MergeConsecutive(IEnumerable<TimeSlot> x)
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
                                yield return (start, prev);
                                yield break;
                            }
                            var c = e.Current;
                            if (c.Index - prev.Index > 1)
                            {
                                yield return (start, prev);
                                start = c;
                            }
                            prev = c;
                        }
                    }
                }
            }
        }
    }
}
