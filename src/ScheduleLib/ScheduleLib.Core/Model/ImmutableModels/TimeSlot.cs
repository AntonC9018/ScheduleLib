using System.Diagnostics;

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
            New(8, 00),
            New(9, 45),
            New(11, 30),
            New(13, 15),
            New(15, 00),
            New(16, 45),
            New(18, 30),
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
                yield return GetTimeSlotInterval(new TimeSlot(i));
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
    public static bool operator<(TimeSlot left, TimeSlot right) => left.Index < right.Index;
    public static bool operator>(TimeSlot left, TimeSlot right) => left.Index > right.Index;
    public static bool operator<=(TimeSlot left, TimeSlot right) => left.Index <= right.Index;
    public static bool operator>=(TimeSlot left, TimeSlot right) => left.Index >= right.Index;

    public int CompareTo(TimeSlot other)
    {
        return Index.CompareTo(other.Index);
    }
}
