using System.Collections.Immutable;
using System.Diagnostics;

namespace App;

public sealed class Schedule
{
    public required ImmutableArray<RegularLesson> RegularLessons { get; init; }
    public required ImmutableArray<OneTimeLesson> OneTimeLessons { get; init; }
    public required ImmutableArray<Group> Groups { get; init; }
    public required ImmutableArray<Teacher> Teachers { get; init; }
    public required ImmutableArray<Course> Courses { get; init; }
}

public enum Parity
{
    OddWeek,
    EvenWeek,
    EveryWeek,
}

public struct DefaultLessonTimeConfig
{
    public required LessonTimeConfig Base;
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
    public required TimeSpan LessonDuration;
    public required TimeOnly[] TimeSlotStarts;

    public static DefaultLessonTimeConfig CreateDefault()
    {
        var ret = new LessonTimeConfig
        {
            LessonDuration = TimeSpan.FromMinutes(90),
            TimeSlotStarts = CreateDefaultTimeSlots(),
        };
        return new()
        {
            Base = ret,
        };
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

public record struct RegularLessonDate()
{
    public Parity Parity = Parity.EveryWeek;
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
    public GroupId Group0 = GroupId.Invalid;
    public GroupId Group1 = GroupId.Invalid;
    public GroupId Group2 = GroupId.Invalid;
    public GroupId Group3 = GroupId.Invalid;

    public readonly int Capacity => 4;

    // indexer
    public GroupId this[int index]
    {
        readonly get
        {
            return index switch
            {
                0 => Group0,
                1 => Group1,
                2 => Group2,
                3 => Group3,
                _ => throw new ArgumentOutOfRangeException(nameof(index)),
            };
        }
        set
        {
            switch (index)
            {
                case 0:
                    Group0 = value;
                    break;
                case 1:
                    Group1 = value;
                    break;
                case 2:
                    Group2 = value;
                    break;
                case 3:
                    Group3 = value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    public readonly bool IsSingleGroup => Group1 == GroupId.Invalid;

    public readonly int Count
    {
        get
        {
            for (int i = 0; i < Capacity; i++)
            {
                if (this[i] == GroupId.Invalid)
                {
                    return i;
                }
            }
            return 4;
        }
    }

    public void Add(GroupId id)
    {
        int count = Count;
        if (count == 4)
        {
            Debug.Fail("Can't add more than 3 groups per lesson");
        }
        this[count] = id;
    }

    public readonly Enumerator GetEnumerator()
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

public readonly record struct CourseId(int Id);

public struct Course
{
    public string FullName => Names[0];
    public required string[] Names;
}

public struct LessonData()
{
    public required LessonGroups Groups;

    public required CourseId Course;
    public required TeacherId Teacher;
    public required RoomId Room;
    public required LessonType Type;

    public SubGroupNumber SubGroup = SubGroupNumber.All;
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
    Lab,
    Seminar,
    Curs,
    Unspecified,
    Custom,
}

public readonly record struct SubGroupNumber(int Value)
{
    public static SubGroupNumber All => new(0);
}

public readonly record struct GroupId(int Value) : IComparable<GroupId>
{
    public static GroupId Invalid => new(-1);
    public int CompareTo(GroupId other) => Value.CompareTo(other.Value);
}

public readonly record struct RoomId(string Id);

public readonly record struct TeacherId(int Id)
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

public readonly record struct Faculty(string Name);
public readonly record struct Specialty(string? Name);

public sealed class Group
{
    public required string Name;
    public required int Grade;
    public required QualificationType QualificationType;
    public required Faculty Faculty;
    public Specialty Specialty = default;
    public int SubGroupCount;
    public required Language Language;
}

public sealed class Teacher
{
    public required string Name;
}

public static class AccessorHelper
{
    public static Group Get(this Schedule schedule, GroupId id) => schedule.Groups[id.Value];
    public static Course Get(this Schedule schedule, CourseId id) => schedule.Courses[id.Id];
    public static Teacher Get(this Schedule schedule, TeacherId id) => schedule.Teachers[id.Id];
    public static string Get(this Schedule schedule, RoomId id)
    {
        _ = schedule;
        return id.Id;
    }
}
