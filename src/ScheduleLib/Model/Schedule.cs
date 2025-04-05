using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

public enum Parity
{
    OddWeek,
    EvenWeek,
    EveryWeek,
}

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
    public required TimeSpan LessonDuration;
    public required TimeOnly[] TimeSlotStarts;

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
public record struct LessonGroups() : IEnumerable<GroupId>
{
    public GroupId Group0 = GroupId.Invalid;
    public GroupId Group1 = GroupId.Invalid;
    public GroupId Group2 = GroupId.Invalid;
    public GroupId Group3 = GroupId.Invalid;
    public GroupId Group4 = GroupId.Invalid;
    public GroupId Group5 = GroupId.Invalid;

    public readonly int Capacity => 6;

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
                4 => Group4,
                5 => Group5,
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
                case 4:
                    Group4 = value;
                    break;
                case 5:
                    Group5 = value;
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
            return Capacity;
        }
    }

    public void Add(GroupId id)
    {
        int count = Count;
        if (count == Capacity)
        {
            Debug.Fail("Can't add more than 3 groups per lesson");
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
}

public readonly record struct CourseId(int Id);

public struct Course
{
    public string FullName => Names[0];
    /// <summary>
    /// Sorted from longest to least long.
    /// </summary>
    public required string[] Names;
}

public struct LessonData()
{
    public required LessonGroups Groups;

    public required CourseId Course;
    public required ImmutableArray<TeacherId> Teachers;
    public required RoomId Room;
    public required LessonType Type;
    public required PeriodId Period;

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
    Unspecified,
    Custom,
}

public readonly record struct SubGroup(string? Value)
{
    public static SubGroup All => new(null!);
}

public readonly record struct GroupId(int Value) : IComparable<GroupId>
{
    public static GroupId Invalid => new(-1);
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

public readonly record struct Grade(int Value);
public enum AttendanceMode
{
    Zi,
    FrecventaRedusa,
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

public struct OptionalFirstNamePart
{
    public required string? Full;
    public required string? Short;

    public readonly string? Longer
    {
        get
        {
            if (Full is { } full)
            {
                return full;
            }
            if (Short is { } shortName)
            {
                return shortName;
            }
            return null;
        }
    }
    public readonly bool IsNull => Full is null && Short is null;
}

public struct FirstNameParts<T>()
{
    public required T A;
    public required T B;
}

public enum FirstNamePartIndex
{
    A,
    B,
    Count,
}

// This amount of boilerplate is seriously concerning.
// This should just work automatically, time to write a source gen.
public static class FirstNameHelper
{
    public ref struct RefEnumerable<T>
    {
        internal readonly ref FirstNameParts<T> _parts;

        public RefEnumerable(ref FirstNameParts<T> parts)
        {
            _parts = ref parts;
        }
    }

    public static RefEnumerable<T> AsRef<T>(this ref FirstNameParts<T> parts)
    {
        return new(ref parts);
    }

    public static RefEnumerator<T> GetEnumerator<T>(this RefEnumerable<T> parts)
    {
        return new(ref parts._parts);
    }

    public static Enumerator<T> GetEnumerator<T>(this FirstNameParts<T> parts)
    {
        return new(parts);
    }

    public struct EnumeratorState()
    {
        private int _value = -1;

        public ref T GetRef<T>(ref FirstNameParts<T> parts)
        {
            return ref FirstNameHelper.GetRef(parts, (FirstNamePartIndex) _value);
        }

        public bool MoveNext()
        {
            _value++;
            return _value < 2;
        }
    }

    public ref struct RefEnumerator<T>
    {
        private readonly ref FirstNameParts<T> _parts;
        private EnumeratorState _enumeratorState;

        public RefEnumerator(ref FirstNameParts<T> parts)
        {
            _parts = ref parts;
            _enumeratorState = new();
        }

        public ref T Current => ref _enumeratorState.GetRef(ref _parts);
        public bool MoveNext() => _enumeratorState.MoveNext();
    }

    public struct Enumerator<T>
    {
        private readonly FirstNameParts<T> _parts;
        private EnumeratorState _enumeratorState;

        public Enumerator(FirstNameParts<T> parts)
        {
            _parts = parts;
            _enumeratorState = new();
        }

        public T Current => _enumeratorState.GetRef(ref Unsafe.AsRef(in _parts));
        public bool MoveNext() => _enumeratorState.MoveNext();
    }

    public static FirstNameParts<U> Map<T, U>(this FirstNameParts<T> n, Func<T, U> map)
    {
        var ret = default(FirstNameParts<U>);
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            var a = i.GetRef(ref n);
            ref var b = ref i.GetRef(ref ret);
            b = map(a);
        }
        return ret;
    }

    public static void Update<T, U>(
        this ref FirstNameParts<T> a,
        FirstNameParts<U> input,
        Func<T, U, T> update)
    {
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            ref var fa = ref i.GetRef(ref a);
            var fb = i.GetRef(ref input);
            fa = update(fa, fb);
        }
    }

    public static bool All<T>(this FirstNameParts<T> a, Func<T, bool> pred)
    {
        foreach (var x in a)
        {
            if (!pred(x))
            {
                return false;
            }
        }
        return true;
    }

    public static bool Any<T>(this FirstNameParts<T> a, Func<T, bool> pred)
    {
        foreach (var x in a)
        {
            if (pred(x))
            {
                return true;
            }
        }
        return false;
    }

    public static bool EachEquals<T, U>(this FirstNameParts<T> a, FirstNameParts<U> b, Func<T, U, bool> pred)
    {
        var i = new EnumeratorState();
        while (i.MoveNext())
        {
            var fa = i.GetRef(ref a);
            var fb = i.GetRef(ref b);
            if (!pred(fa, fb))
            {
                return false;
            }
        }
        return true;
    }

    private static ref T GetRef<T>(in FirstNameParts<T> parts, FirstNamePartIndex index)
    {
        ref var p = ref Unsafe.AsRef(in parts);
        return ref p.Ref(index);
    }

    public static ref T Ref<T>(this ref FirstNameParts<T> n, FirstNamePartIndex index)
    {
        switch (index)
        {
            case FirstNamePartIndex.A:
                return ref n.A;
            case FirstNamePartIndex.B:
                return ref n.B;
            default:
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    public static T Get<T>(this FirstNameParts<T> n, FirstNamePartIndex index)
    {
        return n.Ref(index);
    }

    public static FirstNameParts<string?> Longer(this FirstNameParts<OptionalFirstNamePart> name)
    {
        return name.Map(x => x.Longer);
    }
}

public struct PersonName
{
    public required FirstNameParts<OptionalFirstNamePart> FirstName;
    public required string LastName;
}

public struct PersonContacts
{
    public string? PersonalEmail;
    public string? CorporateEmail;
    public string? PhoneNumber;
}

public record struct PeriodId(int Value)
{
    public static PeriodId Unspecified => new(-1);
    public bool IsUnspecified => this == Unspecified;
    public bool IsSpecified => !IsUnspecified;
}

public record struct Period
{
    public Period(DateOnly start, DateOnly end = default)
    {
        if (end != default)
        {
            Debug.Assert(start <= end);
        }

        _end = end;
        Start = start;
    }

    public readonly DateOnly Start;
    public readonly DateOnly _end;

    public readonly DateOnly? End
    {
        get
        {
            if (_end == default)
            {
                return null;
            }
            return _end;
        }
    }
}

public static class AccessorHelper
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

