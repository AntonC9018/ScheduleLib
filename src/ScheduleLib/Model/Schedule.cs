using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib.Parsing;

namespace ScheduleLib;

public sealed class Schedule
{
    public required ImmutableArray<WeeklyLesson> WeeklyLessons { get; init; }
    public required ImmutableArray<OneTimeLesson> OneTimeLessons { get; init; }
    public required ImmutableArray<Group> Groups { get; init; }
    public required ImmutableArray<Teacher> Teachers { get; init; }
    public required ImmutableArray<Course> Courses { get; init; }
    public required ImmutableArray<Period> Periods { get; init; }
}

public readonly struct Accessor<T, TId> : IAccessor<Accessor<T, TId>, T>
{
    static Accessor()
    {
        ScheduleAccessorHelper.AssertIsInt<TId>();
    }

    public readonly TId Id;
    private readonly ImmutableArray<T> Items;

    public Accessor(TId id, ImmutableArray<T> items)
    {
        Id = id;
        Items = items;
    }

    public readonly ref readonly T Item
    {
        get
        {
            var id = Id;
            var index = Unsafe.As<TId, int>(ref id);
            return ref Items.AsSpan()[index];
        }
    }

    public static Accessor<T, TId> Create(int index, ImmutableArray<T> arr)
    {
        var id = Unsafe.As<int, TId>(ref index);
        return Create(id, arr);
    }
    public static Accessor<T, TId> Create(TId id, ImmutableArray<T> arr)
    {
        return new(id, arr);
    }
}

[AutoConstructor]
public readonly partial struct RefAccessor<T, TRef, TId>
    : IAccessor<RefAccessor<T, TRef, TId>, T>
    where TRef : ILessonRef<TRef, T>, allows ref struct
{
    private readonly Accessor<T, TId> _impl;

    public TRef Item => TRef.Create(in _impl.Item);
    public TId Id => _impl.Id;

    public static RefAccessor<T, TRef, TId> Create(int index, ImmutableArray<T> arr)
    {
        return new(Accessor<T, TId>.Create(index, arr));
    }
    public static RefAccessor<T, TRef, TId> Create(TId id, ImmutableArray<T> arr)
    {
        return new(Accessor<T, TId>.Create(id, arr));
    }
}

public interface IAccessor<TSelf, T> where TSelf : IAccessor<TSelf, T>
{
    static abstract TSelf Create(int index, ImmutableArray<T> arr);
}

public readonly record struct ScheduleObjectEnumerable<T, TAccessor>
    : IEnumerable<TAccessor>
    where TAccessor : IAccessor<TAccessor, T>
{
    private readonly ImmutableArray<T> _objects;

    public ScheduleObjectEnumerable(ImmutableArray<T> objects)
    {
        _objects = objects;
    }

    public TAccessor First()
    {
        using var e = GetEnumerator();
        if (!e.MoveNext())
        {
            throw new InvalidOperationException();
        }
        return e.Current;
    }

    public Enumerator GetEnumerator() => new(_objects);
    IEnumerator<TAccessor> IEnumerable<TAccessor>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<TAccessor>
    {
        private readonly ImmutableArray<T> _objects;
        private int _index;

        public Enumerator(ImmutableArray<T> objects)
        {
            _objects = objects;
            _index = -1;
        }

        public bool MoveNext()
        {
            _index++;
            return _index < _objects.Length;
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }

        object? IEnumerator.Current => Current;

        public readonly TAccessor Current
        {
            get
            {
                Debug.Assert(_index >= 0 && _index < _objects.Length);
                int index = _index;
                var ret = TAccessor.Create(index, _objects);
                return ret;
            }
        }

        void IDisposable.Dispose()
        {
        }
    }

}

// Should be source-generated as well.
// This is a crazy amount of complex boilerplate.
public readonly struct AnyLessonAccessor
{
    static AnyLessonAccessor()
    {
        ScheduleAccessorHelper.AssertIsInt<WeeklyLessonId>();
        ScheduleAccessorHelper.AssertIsInt<OneTimeLessonId>();
    }

    private readonly int _id;
    private readonly LessonRegularity _tag;
    private readonly Schedule _arrays;

    internal AnyLessonAccessor(int id, LessonRegularity tag, Schedule arrays)
    {
        _id = id;
        _tag = tag;
        _arrays = arrays;
    }
    public AnyLessonAccessor(WeeklyLessonId id, Schedule arrays)
    {
        _id = ScheduleAccessorHelper.ToStoredId(id);
        _tag = LessonRegularity.Weekly;
        _arrays = arrays;
    }
    public AnyLessonAccessor(OneTimeLessonId id, Schedule arrays)
    {
        _id = ScheduleAccessorHelper.ToStoredId(id);
        _tag = LessonRegularity.OneTime;
        _arrays = arrays;
    }

    public bool IsWeekly => _tag == LessonRegularity.Weekly;
    public bool IsOneTime => _tag == LessonRegularity.OneTime;
    public LessonRegularity Regularity => _tag;
    public WeeklyLessonAccessor? Weekly
    {
        get
        {
            if (!IsWeekly)
            {
                return null;
            }
            return WeeklyLessonAccessor.Create(_id, _arrays.WeeklyLessons);
        }
    }
    public OneTimeLessonAccessor? OneTime
    {
        get
        {
            if (!IsOneTime)
            {
                return null;
            }
            return OneTimeLessonAccessor.Create(_id, _arrays.OneTimeLessons);
        }
    }

    public readonly ref readonly LessonData Lesson
    {
        get
        {
            {
                if (Weekly is { } x)
                {
                    return ref x.Ref.Lesson;
                }
            }
            {
                if (OneTime is { } x)
                {
                    return ref x.Ref.Lesson;
                }
            }
            throw Unreachable();
        }
    }
}

public readonly record struct AllLessonsEnumerable
    : IEnumerable<AnyLessonAccessor>
{
    private readonly Schedule _arrays;

    public AllLessonsEnumerable(Schedule arrays)
    {
        _arrays = arrays;
    }

    public Enumerator GetEnumerator() => new(_arrays);
    IEnumerator<AnyLessonAccessor> IEnumerable<AnyLessonAccessor>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<AnyLessonAccessor>
    {
        private LessonRegularity _regularity;
        private int _index;
        private Schedule _arrays;

        public Enumerator(Schedule arrays)
        {
            _arrays = arrays;
            _regularity = LessonRegularity.Weekly;
            _index = -1;
        }

        public AnyLessonAccessor Current => new(_index, _regularity, _arrays);
        public bool MoveNext()
        {
            _index++;

            while (true)
            {
                int len;
                switch (_regularity)
                {
                    case LessonRegularity.Weekly:
                    {
                        var arr = _arrays.WeeklyLessons;
                        len = arr.Length;
                        break;
                    }
                    case LessonRegularity.OneTime:
                    {
                        var arr = _arrays.OneTimeLessons;
                        len = arr.Length;
                        break;
                    }
                    default:
                    {
                        return false;
                    }
                }

                if (_index < len)
                {
                    return true;
                }
                _regularity++;
                _index = 0;
            }
        }

        public void Dispose()
        {
        }
        public void Reset() => throw new NotSupportedException();
        object? IEnumerator.Current => Current;
    }
}

public record struct WeeklyLessonDate()
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
    internal const int _Capacity = 16;
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

    public readonly LessonGroups Ordered()
    {
        var copy = this;
        var span = MemoryMarshal.CreateSpan(ref copy._impl._value, Count);
        span.Sort((a, b) => a.Value - b.Value);
        return copy;
    }

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

public static class LessonGroupsHelper
{
    // TODO: reuse sb
    public static string ToString(this in LessonGroups groups, Schedule schedule)
    {
        StringBuilder groupName = new();
        var groupList = new ListStringBuilder(groupName, ", ");
        foreach (var groupId in groups)
        {
            var name = schedule.Get(groupId).Name;
            groupList.Append(name);
        }
        return groupName.ToString();
    }

    public static bool IsSetEquals(this in LessonGroups a, in LessonGroups b)
    {
        foreach (var groupId in a)
        {
            if (!b.Contains(groupId))
            {
                return false;
            }
        }
        foreach (var groupId in b)
        {
            if (!a.Contains(groupId))
            {
                return false;
            }
        }
        return true;
    }

    public static bool IsSubSetOf(this in LessonGroups a, in LessonGroups b)
    {
        foreach (var groupId in a)
        {
            if (!b.Contains(groupId))
            {
                return false;
            }
        }
        return true;
    }
}

public readonly record struct CourseId(int Id)
{
    public static CourseId Invalid => new(-1);
    public bool IsInvalid => this == Invalid;
}

public record struct Course
{
    public string FullName => Names[0];
    /// <summary>
    /// Sorted from longest to least long.
    /// </summary>
    public required ImmutableArray<string> Names;
}

public record struct LessonData()
{
    // Always ordered with .Ordered()
    public required LessonGroups Groups;

    public required CourseId Course;
    public required ImmutableArray<TeacherId> Teachers;
    public required RoomId Room;
    public required LessonType Type;

    public SubGroup SubGroup = SubGroup.All;
    public readonly GroupId Group => Groups.Group0;
}

public enum LessonRegularity
{
    Weekly,
    OneTime,
    Count,
}

public struct OneForEachLessonRegularity<T>
{
    public T Weekly;
    public T OneTime;
}

public static class LessonRegularityHelper
{
    public static ref T Ref<T>(this ref OneForEachLessonRegularity<T> x, LessonRegularity r)
    {
        switch (r)
        {
            case LessonRegularity.OneTime: return ref x.OneTime;
            case LessonRegularity.Weekly: return ref x.Weekly;
            default: throw Unreachable();
        }
    }
}

public readonly record struct AnyLessonId(LessonRegularity Regulariy, int Id);
public readonly record struct WeeklyLessonId(int Id);
public readonly record struct OneTimeLessonId(int Id);

public record struct LessonBase
{
    public required LessonData Data;
}
public record struct WeeklyLesson
{
    public required LessonBase Base;
    public required WeeklyLessonDate Date;
}
public record struct OneTimeLesson
{
    public required LessonBase Base;
    public required OneTimeLessonDate Date;
}

public interface ILessonRef<TSelf, T>
    where TSelf : ILessonRef<TSelf, T>, allows ref struct
{
    static abstract TSelf Create(ref readonly T lesson);
}

public readonly ref struct WeeklyLessonRef : ILessonRef<WeeklyLessonRef, WeeklyLesson>
{
    private readonly ref readonly WeeklyLesson _lesson;

    public WeeklyLessonRef(ref readonly WeeklyLesson lesson)
    {
        _lesson = ref lesson;
    }

    public readonly ref readonly LessonData Lesson => ref _lesson.Base.Data;
    public readonly ref readonly WeeklyLessonDate Date => ref _lesson.Date;

    public static WeeklyLessonRef Create(ref readonly WeeklyLesson lesson) => new(in lesson);
}

public readonly ref struct OneTimeLessonRef : ILessonRef<OneTimeLessonRef, OneTimeLesson>
{
    private readonly ref readonly OneTimeLesson _lesson;

    public OneTimeLessonRef(ref readonly OneTimeLesson lesson)
    {
        _lesson = ref lesson;
    }

    public readonly ref readonly LessonData Lesson => ref _lesson.Base.Data;
    public readonly ref readonly OneTimeLessonDate Date => ref _lesson.Date;
    public static OneTimeLessonRef Create(ref readonly OneTimeLesson lesson) => new(in lesson);
}

public enum LessonType
{
    Lab,
    Seminar,
    Curs,
    Prelegere,
    Custom,
    Unspecified,
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

public static class SpecialSubGroups
{
    public static readonly ImmutableArray<SubGroup> AllSpecial = [
        Optional,
        Beginners,
        Ru,
        Ro,
        Eng,
    ];
    public static SubGroup Optional => new("opțional");
    public static SubGroup Beginners => new("începători");
    public static SubGroup Ru => new("ru");
    public static SubGroup Ro => new("ro");
    public static SubGroup Eng => new("eng");
    public static SubGroup FromLanguage(Language lang)
    {
        return lang switch
        {
            Language.Ro => Ro,
            Language.Ru => Ru,
            Language.En => Eng,
            _ => throw new ArgumentOutOfRangeException(nameof(lang)),
        };
    }
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
    public required LastName LastName;

    public override string ToString()
    {
        var nameFields = new NameFields
        {
            FirstName = FirstName.Map(x => x.Longer),
            LastName = LastName,
        };
        return nameFields.ToString();
    }
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
    public static WeeklyLessonAccessor Get(this Schedule schedule, WeeklyLessonId id)
    {
        return WeeklyLessonAccessor.Create(id, schedule.WeeklyLessons);
    }
    public static OneTimeLessonAccessor Get(this Schedule schedule, OneTimeLessonId id)
    {
        return OneTimeLessonAccessor.Create(id, schedule.OneTimeLessons);
    }

    public static Period Get(this Schedule schedule, PeriodId id)
    {
        Debug.Assert(id.IsSpecified);
        return schedule.Periods[id.Value];
    }

    // TODO: Find a way to automatically generate wrapper types for these so they aren't as complex.
    public static ScheduleObjectEnumerable<Course, Accessor<Course, CourseId>> EnumerateCourses(this Schedule schedule)
    {
        return new(schedule.Courses);
    }
    public static ScheduleObjectEnumerable<Group, Accessor<Group, GroupId>> EnumerateGroups(this Schedule schedule)
    {
        return new(schedule.Groups);
    }
    public static ScheduleObjectEnumerable<Teacher, Accessor<Teacher, TeacherId>> EnumerateTeachers(this Schedule schedule)
    {
        return new(schedule.Teachers);
    }
    public static ScheduleObjectEnumerable<Period, Accessor<Period, PeriodId>> EnumeratePeriods(this Schedule schedule)
    {
        return new(schedule.Periods);
    }
    public static ScheduleObjectEnumerable<WeeklyLesson, WeeklyLessonAccessor> EnumerateWeeklyLessons(this Schedule schedule)
    {
        return new(schedule.WeeklyLessons);
    }
    public static ScheduleObjectEnumerable<OneTimeLesson, OneTimeLessonAccessor> EnumerateOneTimeLessons(this Schedule schedule)
    {
        return new(schedule.OneTimeLessons);
    }
    public static AllLessonsEnumerable EnumerateAllLessons(this Schedule schedule)
    {
        return new(schedule);
    }

    internal static void AssertIsInt<T>()
    {
        Debug.Assert(Marshal.SizeOf<T>() == sizeof(int));
    }
    internal static int ToStoredId<TId>(TId id)
    {
        return Unsafe.As<TId, int>(ref id);
    }

    // TODO: Move this to cached service
    public static SubGroupsByGroup SubGroupsByGroup(this Schedule schedule)
    {
        var result = new SubGroupsByGroup();
        foreach (var group in schedule.EnumerateGroups())
        {
            result.Add(group.Id, new());
        }
        foreach (var lesson in schedule.EnumerateAllLessons())
        {
            foreach (var g in lesson.Lesson.Groups)
            {
                result[g].Add(lesson.Lesson.SubGroup);
            }
        }
        return result;
    }
}

public sealed class SubGroupsByGroup : Dictionary<GroupId, HashSet<SubGroup>>
{
}

[AutoConstructor]
public readonly partial struct WeeklyLessonAccessor : IAccessor<WeeklyLessonAccessor, WeeklyLesson>
{
    private readonly RefAccessor<WeeklyLesson, WeeklyLessonRef, WeeklyLessonId> _impl;

    public readonly WeeklyLessonId Id => _impl.Id;
    public readonly WeeklyLessonRef Ref => _impl.Item;
    public readonly ref readonly LessonData Lesson => ref Ref.Lesson;
    public readonly ref readonly WeeklyLessonDate Date => ref Ref.Date;

    public static WeeklyLessonAccessor Create(WeeklyLessonId id, ImmutableArray<WeeklyLesson> arr)
    {
        var r = RefAccessor<WeeklyLesson, WeeklyLessonRef, WeeklyLessonId>.Create(id, arr);
        return new(r);
    }
    public static WeeklyLessonAccessor Create(int index, ImmutableArray<WeeklyLesson> arr)
    {
        var r = RefAccessor<WeeklyLesson, WeeklyLessonRef, WeeklyLessonId>.Create(index, arr);
        return new(r);
    }
}

[AutoConstructor]
public readonly partial struct OneTimeLessonAccessor : IAccessor<OneTimeLessonAccessor, OneTimeLesson>
{
    private readonly RefAccessor<OneTimeLesson, OneTimeLessonRef, OneTimeLessonId> _impl;

    public readonly OneTimeLessonId Id => _impl.Id;
    public readonly OneTimeLessonRef Ref => _impl.Item;
    public readonly ref readonly LessonData Lesson => ref Ref.Lesson;
    public readonly ref readonly OneTimeLessonDate Date => ref Ref.Date;

    public static OneTimeLessonAccessor Create(OneTimeLessonId id, ImmutableArray<OneTimeLesson> arr)
    {
        var r = RefAccessor<OneTimeLesson, OneTimeLessonRef, OneTimeLessonId>.Create(id, arr);
        return new(r);
    }
    public static OneTimeLessonAccessor Create(int index, ImmutableArray<OneTimeLesson> arr)
    {
        var r = RefAccessor<OneTimeLesson, OneTimeLessonRef, OneTimeLessonId>.Create(index, arr);
        return new(r);
    }
}

