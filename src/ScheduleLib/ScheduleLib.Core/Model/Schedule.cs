using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib.Helper;
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

    public static readonly Schedule Empty = new()
    {
        WeeklyLessons = [],
        OneTimeLessons = [],
        Groups = [],
        Teachers = [],
        Courses = [],
        Periods = [],
    };
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

    private readonly AnyLessonId _id;
    private readonly Schedule _arrays;

    internal AnyLessonAccessor(AnyLessonId id, Schedule arrays)
    {
        _id = id;
        _arrays = arrays;
    }
    public AnyLessonAccessor(WeeklyLessonId id, Schedule arrays)
    {
        _id = new(LessonRegularity.Weekly, id.Id);
        _arrays = arrays;
    }
    public AnyLessonAccessor(OneTimeLessonId id, Schedule arrays)
    {
        _id = new(LessonRegularity.OneTime, id.Id);
        _arrays = arrays;
    }

    public bool IsWeekly => Regularity == LessonRegularity.Weekly;
    public bool IsOneTime => Regularity == LessonRegularity.OneTime;
    public LessonRegularity Regularity => _id.Regularity;
    public WeeklyLessonAccessor? Weekly
    {
        get
        {
            if (!IsWeekly)
            {
                return null;
            }
            return WeeklyLessonAccessor.Create(_id.Id, _arrays.WeeklyLessons);
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
            return OneTimeLessonAccessor.Create(_id.Id, _arrays.OneTimeLessons);
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

    public AnyLessonId Id => _id;
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

        public AnyLessonAccessor Current => new(
            new AnyLessonId(_regularity, _index),
            _arrays);
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

    public readonly GroupId Group0 => this[0];

    public readonly int Capacity => LessonGroupsImpl._Capacity;

    public readonly bool IsSingleGroup => this[1] == GroupId.Invalid;

    public readonly bool IsEmpty => Group0.IsInvalid;

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
        var count = a.Count;
        if (count != b.Count)
        {
            return false;
        }
        for (int i = 0; i < count; i++)
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

    private readonly SequenceComparableImmutableArray<TeacherId> _teachers;
    public required ImmutableArray<TeacherId> Teachers
    {
        get => _teachers.Array;
        init => _teachers = new(value);
    }

    public required RoomId Room;
    public required LessonType Type;

    public SubGroup SubGroup = SubGroup.All;
    public Specialization Specialization = Specialization.All;
    public Alternative Alternative = Alternative.All;
    public readonly GroupId Group => Groups.Group0;
}

/// <summary>
/// One student-partition axis of a <see cref="GroupPartitionKey"/>.
/// Closed set on purpose: no extensible-dimension support, just an abstraction
/// so the per-site dimension loops stay unified.
/// </summary>
public enum PartitionDimension
{
    SubGroup,
    Specialization,
    Alternative,
}

/// <summary>
/// The value on one <see cref="PartitionDimension"/>. Wraps a string for now;
/// every dimension type converts implicitly. A null value means "all".
/// </summary>
public readonly record struct PartitionKey
{
    public readonly string? Value { get; }

    public PartitionKey(string? value)
    {
        Debug.Assert(value != "");
        Value = value;
    }

    public static implicit operator PartitionKey(SubGroup subGroup) => new(subGroup.Value);
    public static implicit operator PartitionKey(Specialization specialization) => new(specialization.Value);
    public static implicit operator PartitionKey(Alternative alternative) => new(alternative.Value);
}

public static class PartitionDimensions
{
    /// <summary>
    /// Every dimension in storage order.
    /// </summary>
    public static ReadOnlySpan<PartitionDimension> All => [
        PartitionDimension.SubGroup,
        PartitionDimension.Specialization,
        PartitionDimension.Alternative,
    ];

    /// <summary>
    /// Alternative first, then specialization, then subgroup.
    /// </summary>
    public static ReadOnlySpan<PartitionDimension> DisplayOrder => [
        PartitionDimension.Alternative,
        PartitionDimension.Specialization,
        PartitionDimension.SubGroup,
    ];
}

/// <summary>
/// Combines the subgroup, specialization and alternative values a lesson targets
/// into one identity. Used wherever equality or grouping must consider all fields.
/// Not serialized.
/// </summary>
public readonly record struct GroupPartitionKey(SubGroup SubGroup, Specialization Specialization, Alternative Alternative = default)
{
    public static GroupPartitionKey All => new(SubGroup.All, Specialization.All, Alternative.All);
}

public static class LessonDataExtensions
{
    extension(in LessonData lesson)
    {
        public GroupPartitionKey GroupPartitionKey => new(lesson.SubGroup, lesson.Specialization, lesson.Alternative);
    }

    extension(in GroupPartitionKey key)
    {
        /// <summary>
        /// The key's value on one partition dimension.
        /// </summary>
        public PartitionKey GetPartitionDimension(PartitionDimension dimension) => dimension switch
        {
            PartitionDimension.SubGroup => key.SubGroup,
            PartitionDimension.Specialization => key.Specialization,
            PartitionDimension.Alternative => key.Alternative,
            _ => throw Unreachable(),
        };

        /// <summary>
        /// Alternative first, then the specialization, then the subgroup.
        /// Null when the key targets everything.
        /// </summary>
        public string? ToDisplayString(string separator = ", ")
        {
            var parts = new List<string>(3);
            if (key.Alternative.Value is { } a)
            {
                parts.Add(a);
            }
            if (key.Specialization.Value is { } s)
            {
                parts.Add(s);
            }
            if (key.SubGroup.Value is { } subGroup)
            {
                parts.Add(subGroup);
            }
            return parts.Count == 0 ? null : string.Join(separator, parts);
        }
    }
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

public readonly record struct AnyLessonId(LessonRegularity Regularity, int Id);
public readonly record struct WeeklyLessonId(int Id)
{
    public AnyLessonId AsAny() => new(LessonRegularity.Weekly, Id);
}
public readonly record struct OneTimeLessonId(int Id)
{
    public AnyLessonId AsAny() => new(LessonRegularity.OneTime, Id);
}

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
    None = -1,
    Lab,
    Seminar,
    Curs,
    Prelegere,
    Exam,
    Consultation,
    Custom,
    Unspecified,
    Count,
}

public readonly record struct SubGroup
{
    public static SubGroup CreateNumeric(int i) => new(NumberHelper.ToRoman(i));
    public readonly string? Value { get; }

    public SubGroup(string? value)
    {
        Debug.Assert(value != "");
        Value = value;
    }

    public static SubGroup All => new(null!);
}

/// <summary>
/// A restriction of a lesson to one specialization of study.
/// <see cref="All"/> means the lesson carries no specialization restriction.
/// Backed by a string, because schedule sources may add new names over time.
/// Known values live as extension properties on this type in ScheduleDefaults;
/// only the unset marker stays here because Core logic depends on it.
/// </summary>
public readonly record struct Specialization
{
    public readonly string? Value { get; }

    public Specialization(string? value)
    {
        Debug.Assert(value != "");
        Value = value;
    }

    public static Specialization All => new(null!);
}

/// <summary>
/// A restriction of a lesson to one alternative of a student choice dimension,
/// such as elective courses between which students pick one.
/// <see cref="All"/> means the lesson carries no alternative restriction.
/// Backed by a string, because the choices are configured per schedule.
/// </summary>
public readonly record struct Alternative
{
    public readonly string? Value { get; }

    public Alternative(string? value)
    {
        Debug.Assert(value != "");
        Value = value;
    }

    public static Alternative All => new(null!);
}

/// <summary>
/// Thin forwards over the built-in specialization values: the canonical
/// known-value accessors are extension properties on <see cref="Specialization"/>
/// in ScheduleDefaults, but prefix matching and classification run inside Core
/// (and OnlineRegistry cannot reference ScheduleDefaults back), so the
/// aggregate lookup surface stays here.
/// </summary>
public static class Specializations
{
    public static readonly ImmutableArray<Specialization> AllKnown = [
        new("AG"),
        new("Algoritmica Grafurilor"),
        new("CV"),
        new("DezvoltareaAplicatiilor"),
        new("DJ"),
        new("GA2D"),
        new("GA3D"),
        new("Logica"),
        new("React"),
        new("Spring"),
        new("SSI"),
        new("UI"),
    ];

    public static bool TryFromValue(string? value, out Specialization specialization)
    {
        if (value is null)
        {
            specialization = default;
            return false;
        }
        foreach (var candidate in AllKnown)
        {
            if (string.Equals(candidate.Value, value, StringComparison.Ordinal))
            {
                specialization = candidate;
                return true;
            }
        }
        specialization = default;
        return false;
    }
}

public static class SpecialSubGroups
{
    // The meaning of these historical labels is unresolved.
    // They stay ordinary subgroup values: don't split their digits and don't treat them as specializations.
    public static readonly ImmutableArray<SubGroup> Legacy = [
        new("S1"),
        new("S11"),
        new("S12"),
        new("S21"),
        new("S22"),
        new("S23"),
        new("GA"),
        new("GA1"),
        new("GA2"),
        new("WR"),
        new("WR1"),
        new("WR2"),
        new("SF"),
        new("UI-1"),
        new("UI-2"),
    ];

    public static readonly ImmutableArray<SubGroup> AllSpecial = [
        Optional,
        Beginners,
        NonBeginners,
        Ru,
        Ro,
        Eng,
        ..Legacy,
    ];
    // Legacy marker for an unspecified specialization subgroup. New schedules should name the specialization.
    public static SubGroup Optional => new("opțional");
    public static SubGroup Beginners => new("începători");
    public static SubGroup NonBeginners => new("nuîncepători");
    public static SubGroup Ru => new("ru");
    public static SubGroup Ro => new("ro");
    public static SubGroup Eng => new("eng");

    // Group headers may name any known label. Specialization values are transported as subgroups
    // here and get classified into Specialization during builder processing.
    // Specializations must come before the legacy values, so that prefixes like "ui" keep matching
    // the specialization instead of "UI-1".
    private static readonly ImmutableArray<SubGroup> PrefixCandidates = CreatePrefixCandidates();
    private static ImmutableArray<SubGroup> CreatePrefixCandidates()
    {
        var builder = ImmutableArray.CreateBuilder<SubGroup>();
        builder.Add(Optional);
        builder.Add(Beginners);
        builder.Add(NonBeginners);
        builder.Add(Ru);
        builder.Add(Ro);
        builder.Add(Eng);
        foreach (var specialization in Specializations.AllKnown)
        {
            builder.Add(new(specialization.Value!));
        }
        builder.AddRange(Legacy);
        // MoveToImmutable would only work while the item count happens to equal the
        // builder capacity, so copy instead.
        return builder.ToImmutable();
    }

    public static bool TryFromNamePrefix(ReadOnlySpan<char> value, out SubGroup subGroup)
    {
        value = value.Trim();
        if (value.EndsWith('.'))
        {
            value = value[..^1];
        }

        const int minimumPrefixLength = 2;
        if (value.Length < minimumPrefixLength)
        {
            subGroup = default;
            return false;
        }

        foreach (var candidate in PrefixCandidates)
        {
            if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(candidate.Value!, value))
            {
                subGroup = candidate;
                return true;
            }
        }

        foreach (var candidate in PrefixCandidates)
        {
            if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(candidate.Value!, value))
            {
                subGroup = candidate;
                return true;
            }
        }

        subGroup = default;
        return false;
    }

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

    public static bool IsLanguageSubGroup(this SubGroup subGroup)
    {
        return subGroup == Ro || subGroup == Ru || subGroup == Eng;
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
    None = -1,
    Ro,
    Ru,
    En,
    Count,
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
    Dual,
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
    // As an idea: save the origin of the value (stack trace + way to see it)
    public required PersonName PersonName;
    public required PersonContacts Contacts;
}

public struct PersonName
{
    public required NameParts<OptionalNamePart> FirstName;
    // One is required
    public required LastName LastName;

    public NameFields AsNameFields()
    {
        var nameFields = new NameFields
        {
            FirstName = FirstName.Map(x => x.Longer),
            LastName = LastName,
        };
        return nameFields;
    }

    public override string ToString()
    {
        var f = AsNameFields();
        return f.ToString();
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
    public static AnyLessonAccessor Get(this Schedule schedule, AnyLessonId id)
    {
        return new AnyLessonAccessor(id, schedule);
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

    public static TimeSlot GetTimeSlot(this AnyLessonAccessor lesson)
    {
        if (lesson.Weekly is { } weekly)
        {
            return weekly.Date.TimeSlot;
        }
        if (lesson.OneTime is { } oneTime)
        {
            return oneTime.Date.TimeSlot;
        }
        throw Unreachable();
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

