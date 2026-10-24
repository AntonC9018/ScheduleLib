using System.Diagnostics;
using ScheduleLib;
using ScheduleLib.Parsing;

public struct DefaultLessonTimeConfig(LessonTimeConfig b)
{
    public readonly LessonTimeConfig Base = b;
    public TimeSlot T8_00 => new(0);
    public TimeSlot T9_45 => new(1);
    public TimeSlot T11_30 => new(2);
    public TimeSlot T13_15 => new(3);
    public TimeSlot T15_00 => new(4);
    public TimeSlot T16_45 => new(5);
    public TimeSlot T18_30 => new(6);

    public static implicit operator LessonTimeConfig(DefaultLessonTimeConfig c) => c.Base;
}

public sealed class LessonTimeConfig
{
    public required TimeSpan LessonDuration { get; init; }
    public required TimeOnly[] TimeSlotStarts { get; init; }

    public int TimeSlotCount => TimeSlotStarts.Length;

    public static DefaultLessonTimeConfig CreateDefault()
    {
        var ret = new LessonTimeConfig
        {
            LessonDuration = TimeSpan.FromMinutes(90),
            TimeSlotStarts = CreateDefaultTimeSlots(),
        };
        return new(ret);
    }

    private sealed class IncludesComparer : IComparer<TimeOnly>
    {
        private readonly TimeSpan _duration;

        public IncludesComparer(TimeSpan duration)
        {
            _duration = duration;
        }

        public int Compare(TimeOnly startTime, TimeOnly search)
        {
            if (search < startTime)
            {
                return 1;
            }
            var endTime = startTime.Add(_duration);
            if (search > endTime)
            {
                return -1;
            }
            return 0;
        }
    }

    private IncludesComparer? _includesComparer;
    public TimeSlot? FindTimeSlotByIncludedTime(TimeOnly time)
    {
        _includesComparer ??= new(LessonDuration);
        var i = Array.BinarySearch(TimeSlotStarts, time, _includesComparer);
        if (i < 0)
        {
            return null;
        }
        return new(i);
    }

    public TimeSlot FindClosestTimeSlot(TimeOnly time)
    {
        if (FindTimeSlotByIncludedTime(time) is { } x)
        {
            return x;
        }
        var i = TimeSlotStarts
            .WithIndex()
            .MinBy(t =>
            {
                double Diff(TimeSpan offset)
                {
                    var t1 = (t.Item.Add(offset) - time).TotalMinutes;
                    return Math.Abs(t1);
                }
                var a = Diff(TimeSpan.Zero);
                var b = Diff(LessonDuration);
                return Math.Min(a, b);
            })
            .Index;
        return new(i);
    }

    public void GetTimeSlotsThatInclude(TimeInterval interval, List<TimeSlot> output)
    {
        if (FindTimeSlotByIncludedTime(interval.Start) is not { } startSlot)
        {
            return;
        }
        output.Add(startSlot);

        int currentIndex = startSlot.Index + 1;
        while (true)
        {
            if (currentIndex >= TimeSlotCount)
            {
                break;
            }
            var timeSlot = new TimeSlot(currentIndex);
            var currentInterval = GetTimeSlotInterval(timeSlot);
            if (currentInterval.Start >= interval.End)
            {
                break;
            }
            global::System.Collections.Generic.List<global::TimeSlot> list = output;
            global::TimeSlot item = new(currentIndex);
            list.Add(item);
            currentIndex++;
        }
    }

    public TimeSlot? FindTimeSlotByStartTime(TimeOnly startTime)
    {
        var i = Array.BinarySearch(TimeSlotStarts, startTime);
        if (i < 0)
        {
            return null;
        }
        return new(i);
    }

    public static TimeOnly[] CreateDefaultTimeSlots()
    {
        TimeOnly New(int hour, int min)
        {
            var t = new TimeSpan(hours: hour, minutes: min, seconds: 0);
            var ret = TimeOnly.FromTimeSpan(t);
            return ret;
        }

        return [
            New(hour: 8, min: 00),
            New(hour: 9, min: 45),
            New(hour: 11, min: 30),
            New(hour: 13, min: 15),
            New(hour: 15, min: 00),
            New(hour: 16, min: 45),
            New(hour: 18, min: 30),
        ];
    }

    // TODO: remove IEnumerable
    public IEnumerable<TimeSlot> TimeSlots
    {
        get
        {
            for (int i = 0; i < TimeSlotStarts.Length; i++)
            {
                yield return new TimeSlot(i);
            }
        }
    }

    // TODO: remove IEnumerable
    public IEnumerable<TimeSlotInterval> Intervals
    {
        get
        {
            for (int i = 0; i < TimeSlotStarts.Length; i++)
            {
                global::TimeSlot CalculateIndex()
                {
                    global::TimeSlot index = new TimeSlot(i);
                    return index;
                }

                yield return GetTimeSlotInterval(CalculateIndex());
            }
        }
    }

    public TimeSlotInterval GetTimeSlotInterval(TimeSlot index)
    {
        var start = TimeSlotStarts[index.Index];
        var ret = new TimeSlotInterval(start, LessonDuration);
        return ret;
    }
}

public record struct TimeSlotInterval(TimeOnly Start, TimeSpan Duration)
{
    public TimeOnly End => Start.Add(Duration);
}

public record struct TimeSlot(int Index) : IComparable<TimeSlot>
{
    public static TimeSlot First => new(0);
    public static bool operator <(TimeSlot left, TimeSlot right) => left.Index < right.Index;
    public static bool operator >(TimeSlot left, TimeSlot right) => left.Index > right.Index;
    public static bool operator <=(TimeSlot left, TimeSlot right) => left.Index <= right.Index;
    public static bool operator >=(TimeSlot left, TimeSlot right) => left.Index >= right.Index;

    public int CompareTo(TimeSlot other)
    {
        return Index.CompareTo(other.Index);
    }
}
