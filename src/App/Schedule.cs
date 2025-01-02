using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace App;

public sealed class Schedule
{
    public required List<RegularLesson> RegularLessons;
    public required List<OneTimeLesson> OneTimeLessons;
    public required List<Group> Groups;
    public required List<Teacher> Teachers;
}

public enum Parity
{
    OddWeek,
    EvenWeek,
    EveryWeek,
}

public sealed class LessonTimeConfig
{
    public required TimeSpan LessonDuration;
    public required TimeOnly[] TimeSlotStarts;

    public static LessonTimeConfig CreateDefault()
    {
        var ret = new LessonTimeConfig
        {
            LessonDuration = TimeSpan.FromMinutes(90),
            TimeSlotStarts = CreateDefaultTimeSlots(),
        };
        return ret;
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
            New(13, 00),
            New(15, 00),
            New(16, 45),
            New(18, 30),
        ];
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

public record struct RegularLessonDate
{
    public required Parity Parity;
    public required DayOfWeek DayOfWeek;
    public required TimeSlot TimeSlot;
}

public record struct OneTimeLessonDate
{
    public required DateOnly Date;
    public required TimeSlot TimeSlot;
}

// [StructLayout(LayoutKind.Sequential)]
public struct LessonGroups()
{
    public required GroupId Group0 = new(-1);
    public required GroupId Group1 = new(-1);
    public required GroupId Group2 = new(-1);

    // indexer
    public GroupId this[int index]
    {
        get
        {
            return index switch
            {
                0 => Group0,
                1 => Group1,
                2 => Group2,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
    }

    public int Count
    {
        get
        {
            Debug.Assert(Group0.Value != -1);
            if (Group1.Value == -1)
            {
                return 1;
            }
            if (Group2.Value == -1)
            {
                return 2;
            }
            return 3;
        }
    }

    public void Add(GroupId id)
    {
        if (Group0.Value == -1)
        {
            Group0 = id;
            return;
        }
        if (Group1.Value == -1)
        {
            Group1 = id;
            return;
        }
        if (Group2.Value == -1)
        {
            Group2 = id;
            return;
        }
        Debug.Fail("Can't add more than 3 groups per lesson");
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public struct Enumerator
    {
        private readonly LessonGroups _groups;
        private int _index;

        public Enumerator(LessonGroups groups)
        {
            _groups = groups;
            _index = -1;
        }

        public GroupId Current => _groups[_index];

        public bool MoveNext()
        {
            _index++;
            return _index < _groups.Count;
        }
    }
}

public record struct LessonData
{
    public required TeacherId Teacher;
    public required LessonGroups Groups;
    public required SubGroupNumber SubGroup;
    public required string Room;
    public required LessonType Type;
    public readonly GroupId Group => Groups.Group0;
}

public sealed class RegularLesson
{
    public required LessonData Lesson;
    public required RegularLessonDate Date;
}

public sealed class OneTimeLesson
{
    public required LessonData Lesson;
    public required OneTimeLessonDate Date;
}

public enum LessonType
{
    Unspecified,
    Lab,
    Curs,
    Custom,
}

public record struct SubGroupNumber(int Value)
{
    public static SubGroupNumber All => new(0);
}

public record struct GroupId(int Value)
{
    public static GroupId Invalid => new(-1);
}

public record struct TeacherId(int Id)
{
}

public enum QualificationType
{
    Licenta,
    Master,
    Doctor,
}

public enum Language
{
    Ro,
    Ru,
    En,
    _Count,
}

public record struct Faculty(string Name);
public record struct Specialty(string? Name);

public sealed class Group
{
    public required string Name;
    public required int Grade;
    public required QualificationType QualificationType;
    public required Faculty Faculty;
    public Specialty Specialty = default;
    public required Language Language;
}

public sealed class Teacher
{
    public required string Name;
}