using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using ScheduleLib.Parsing;

namespace ScheduleLib;

public sealed class Schedule
{
    public required ImmutableArray<RegularLesson> RegularLessons { get; init; }
    public required ImmutableArray<OneTimeLesson> OneTimeLessons { get; init; }
    public required ImmutableArray<Group> Groups { get; init; }
    public required ImmutableArray<Teacher> Teachers { get; init; }
    public required ImmutableArray<Course> Courses { get; init; }
    public required ImmutableArray<Period> Periods { get; init; }
}

public record struct RegularLessonDate()
{
    public Parity Parity = Parity.EveryWeek;
    public required DayOfWeek DayOfWeek;
    public required TimeSlot TimeSlot;
    public required PeriodId Period;
}

public record struct OneTimeLessonDate
{
    public required DateOnly Date;
    public required TimeSlot TimeSlot;
}

[InlineArray(_Capacity)]
internal struct LessonGroupsImpl
{
    internal const int _Capacity = 15;
    public GroupId _value;
}
// [StructLayout(LayoutKind.Sequential)]
public struct LessonGroups : IEnumerable<GroupId>, IEquatable<LessonGroups>
{
    private LessonGroupsImpl _impl;

    // indexer
    public GroupId this[int index]
    {
        readonly get => _impl[index];
        set => _impl[index] = value;
    }

    public LessonGroups()
    {
        for (int i = 0; i < Capacity; i++)
        {
            this[i] = GroupId.Invalid;
        }
    }

    public GroupId Group0 => this[0];

    public readonly int Capacity => LessonGroupsImpl._Capacity;

    public readonly bool IsSingleGroup => this[1] == GroupId.Invalid;

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
            return Capacity;
        }
    }

    public void Add(GroupId id)
    {
        int count = Count;
        if (count == Capacity)
        {
            Debug.Fail($"Can't add more than {Capacity} groups per lesson");
        }
        this[count] = id;
    }

    public readonly bool Contains(GroupId id)
    {
        foreach (var groupId in this)
        {
            if (groupId == id)
            {
                return true;
            }
        }
        return false;
    }

    public readonly Enumerator GetEnumerator() => new(this);
    IEnumerator<GroupId> IEnumerable<GroupId>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<GroupId>
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

        public void Dispose()
        {
        }

        public void Reset() => throw new NotImplementedException();
        object? IEnumerator.Current => Current;
    }

    public bool Equals(LessonGroups other) => this == other;

    public override bool Equals(object? o)
    {
        if (o is LessonGroups other)
        {
            return this == other;
        }
        return false;
    }

    public override int GetHashCode()
    {
        int hash = 17;
        for (int i = 0; i < Capacity; i++)
        {
            hash = hash * 31 + this[i].GetHashCode();
        }
        return hash;
    }

    public static bool operator==(in LessonGroups a, in LessonGroups b)
    {
        for (int i = 0; i < a.Capacity; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }
        return true;
    }
    public static bool operator!=(in LessonGroups a, in LessonGroups b) => !(a == b);
}

public readonly record struct CourseId(int Id)
{
    public static CourseId Invalid => new(-1);
    public bool IsInvalid => this == Invalid;
}

public struct Course
{
    public string FullName => Names[0];
    /// <summary>
    /// Sorted from longest to least long.
    /// </summary>
    public required ImmutableArray<string> Names;
}

public struct LessonData()
{
    public required LessonGroups Groups;

    public required CourseId Course;
    public required ImmutableArray<TeacherId> Teachers;
    public required RoomId Room;
    public required LessonType Type;

    public SubGroup SubGroup = SubGroup.All;
    public readonly GroupId Group => Groups.Group0;
}

public sealed class RegularLesson
{
    public required LessonData Lesson;
    public required RegularLessonDate Date;
}

public readonly record struct RegularLessonId(int Id);

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
    Prelegere,
    Unspecified,
    Custom,
    Count,
}

public readonly record struct SubGroup
{
    public readonly string? Value { get; }

    public SubGroup(string? value)
    {
        Debug.Assert(value != "");
        Value = value;
    }

    public static SubGroup All => new(null!);
}

public readonly record struct GroupId(int Value) : IComparable<GroupId>
{
    public static GroupId Invalid => new(-1);
    public bool IsInvalid => this == Invalid;
    public int CompareTo(GroupId other) => Value.CompareTo(other.Value);
}

public readonly record struct RoomId(string? Id)
{
    public static RoomId Invalid => new(null!);
    public bool IsValid => this != Invalid;
}

public readonly record struct TeacherId(int Id)
{
}

public enum QualificationType
{
    Licenta,
    Master,
    Doctor,
    Count,
    Invalid = -1,
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

public readonly record struct Grade(int Value)
{
    public static Grade Invalid => new(-1);
}

public enum AttendanceMode
{
    Zi,
    FrecventaRedusa,
    Count,
    Invalid = -1,
}

public record struct OneForEachAttendanceMode<T>
{
    public required T Zi;
    public required T FrecventaRedusa;

    public T this[AttendanceMode mode]
    {
        readonly get
        {
            return mode switch
            {
                AttendanceMode.Zi => Zi,
                AttendanceMode.FrecventaRedusa => FrecventaRedusa,
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
        }
        set
        {
            switch (mode)
            {
                case AttendanceMode.Zi:
                {
                    Zi = value;
                    break;
                }
                case AttendanceMode.FrecventaRedusa:
                {
                    FrecventaRedusa = value;
                    break;
                }
                default:
                {
                    throw new ArgumentOutOfRangeException(nameof(mode));
                }
            }
        }
    }
}

[Flags]
public enum AttendanceModeFlags
{
    None,
    Zi = 1 << AttendanceMode.Zi,
    FrecventaRedusa = 1 << AttendanceMode.FrecventaRedusa,
}

public static class AttendanceModeFlagsHelper
{
    public static bool Has(this AttendanceModeFlags flags, AttendanceMode mode)
    {
        return (flags & (AttendanceModeFlags) mode) != 0;
    }
}

public sealed class Group
{
    /// <summary>
    /// Does not contain the language
    /// </summary>
    public required string Name;

    public required Grade Grade;
    public required int GroupNumber;
    public required QualificationType QualificationType;
    public required Faculty Faculty;
    public required AttendanceMode AttendanceMode;
    public Specialty Specialty = default;
    public required Language Language;
}

public sealed class Teacher
{
    public required PersonName PersonName;
    public required PersonContacts Contacts;
}


public struct PersonName
{
    public required NameParts<OptionalNamePart> FirstName;
    // One is required
    public required NameParts<string?> LastName;
}

public struct PersonContacts
{
    public string? PersonalEmail;
    public string? CorporateEmail;
    public string? PhoneNumber;
}

public static class ScheduleAccessorHelper
{
    public static Group Get(this Schedule schedule, GroupId id) => schedule.Groups[id.Value];
    public static Course Get(this Schedule schedule, CourseId id) => schedule.Courses[id.Id];
    public static Teacher Get(this Schedule schedule, TeacherId id)
    {
        return schedule.Teachers[id.Id];
    }

    public static string Get(this Schedule schedule, RoomId id)
    {
        Debug.Assert(id.IsValid);
        _ = schedule;
        return id.Id!;
    }
    public static RegularLesson Get(this Schedule schedule, RegularLessonId id) => schedule.RegularLessons[id.Id];

    public static Period Get(this Schedule schedule, PeriodId id)
    {
        Debug.Assert(id.IsSpecified);
        return schedule.Periods[id.Value];
    }
}

