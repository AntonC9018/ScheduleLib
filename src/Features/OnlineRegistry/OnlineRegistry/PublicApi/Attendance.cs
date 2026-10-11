using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Helper;
using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.OnlineRegistry;

public enum Attendance
{
    None,
    NotApplicable, // na
    NotPresent, // a
    MotivatedAbsent, // am
    Present, // <empty>
    Grade, // left alone if this is found
}

/// <summary>How attendance-column dates in the header row are used.</summary>
public enum AttendanceDateMode
{
    /// <summary>Dates only bound the attendance columns; lessons map by column order.</summary>
    Ignore,
    /// <summary>Every attendance column must carry a date; lessons bind by date, never by order.</summary>
    ValidateAndBind,
}

/// <summary>An attendance-column date. Year-less dates (dd.MM) match by month/day.</summary>
public readonly record struct AttendanceDate
{
    public required int Month { get; init; }
    public required int Day { get; init; }
    public int? Year { get; init; }

    public bool Matches(DateTime dateTime)
    {
        if (dateTime.Month != Month || dateTime.Day != Day)
        {
            return false;
        }
        return Year is null || dateTime.Year == Year;
    }
}

public static class AttendanceHelper
{
    public static Attendance Parse(ReadOnlySpan<char> value)
    {
        switch (value)
        {
            case "a" or "np":
                return Attendance.NotPresent;
            case "am":
                return Attendance.MotivatedAbsent;
            case "":
                return Attendance.Present;
            case "na":
                return Attendance.NotApplicable;
            default:
                return Attendance.Grade;
        }
    }

    public static string ToStringValue(this Attendance attendance)
    {
        return attendance switch
        {
            Attendance.NotApplicable => "na",
            Attendance.NotPresent => "a",
            Attendance.MotivatedAbsent => "am",
            Attendance.Present => "",
            Attendance.Grade => "grade",
            _ => throw Unreachable(),
        };
    }
}

public readonly record struct StudentIndex(int Index);


public readonly struct StudentAttendanceBuilder(Name name)
{
    internal Name Name { get; } = name;
    internal readonly ImmutableArray<Attendance>.Builder Attendances =
        ImmutableArray.CreateBuilder<Attendance>();

    public void Day(Attendance attendance)
    {
        Attendances.Add(attendance);
    }
}

public readonly struct DayAttendanceList(ImmutableArray<Attendance> values)
{
    public bool IsNull => values == default;
    public ImmutableArray<Attendance> AsArray() => values;
    public Attendance Student(int index) => values[index];
    public int StudentCount => values.Length;
}

public readonly struct AttendanceLists
{
    private readonly ImmutableArray<DayAttendanceList> _values;

    internal AttendanceLists(ImmutableArray<DayAttendanceList> values)
    {
        Debug.Assert(values.None(v => v.IsNull));
        _values = values;
    }

    public DayAttendanceList Day(int index) => _values[index];
    public int DayCount => _values.Length;
    public int StudentCount
    {
        get
        {
            if (_values.Length == 0)
            {
                return 0;
            }
            return Day(0).StudentCount;
        }
    }

    public readonly ImmutableArray<DayAttendanceList> Values => _values;
}

public sealed class StudentAttendanceListBuilder()
{
    private readonly List<StudentAttendanceBuilder> _students = new();
    private readonly HashSet<Name> _allNames = new();
    private List<LessonType>? _lessonTypesPerDay = null;
    private List<AttendanceDate>? _datesPerDay = null;

    private const int NoMaxCount = -1;
    private int _maxCountHint = NoMaxCount;

    public void Clear()
    {
        _lessonTypesPerDay = null;
        _datesPerDay = null;
        _allNames.Clear();
        _students.Clear();
        _maxCountHint = NoMaxCount;
    }

    public void AddLessonTypes(ReadOnlySpan<LessonType> lessonTypes)
    {
        _lessonTypesPerDay ??= new();
        _lessonTypesPerDay.AddRange(lessonTypes);
    }

    public void AddAttendanceDates(ReadOnlySpan<AttendanceDate> dates)
    {
        _datesPerDay ??= new();
        _datesPerDay.AddRange(dates.ToArray());
    }

    public StudentAttendanceBuilder Student(Name name)
    {
        if (!_allNames.Add(name))
        {
            throw new InvalidOperationException("Duplicate student name in attendance data.");
        }
        var s = new StudentAttendanceBuilder(name);
        _students.Add(s);
        return s;
    }

    public void HintMaxCount(int count)
    {
        Debug.Assert(count >= 0);
        _maxCountHint = count;
    }

    public readonly record struct Result
    {
        public readonly ImmutableArray<Name> Names;
        public readonly AttendanceLists Attendance;
        public readonly ImmutableArray<LessonType>? LessonTypes;
        public readonly ImmutableArray<AttendanceDate>? Dates;

        internal Result(
            ImmutableArray<Name> Names,
            AttendanceLists Attendance,
            ImmutableArray<LessonType>? LessonTypes,
            ImmutableArray<AttendanceDate>? Dates)
        {
            Debug.Assert(Names.Length == Attendance.StudentCount);
            if (LessonTypes is { } lt)
            {
                Debug.Assert(lt.Length == Attendance.DayCount);
            }
            if (Dates is { } dates)
            {
                Debug.Assert(dates.Length == Attendance.DayCount);
            }

            this.Names = Names;
            this.Attendance = Attendance;
            this.LessonTypes = LessonTypes;
            this.Dates = Dates;
        }
    }

    public Result Build(
        Attendance? missingDaysFiller)
    {
        var maxDayCount = _students.Select(x => x.Attendances.Count).DefaultIfEmpty(0).Max();
        maxDayCount = Math.Max(maxDayCount, _maxCountHint);
        if (missingDaysFiller is { } f)
        {
            foreach (var student in _students)
            {
                var a = student.Attendances;
                a.Capacity = maxDayCount;

                while (a.Count < maxDayCount)
                {
                    a.Add(f);
                }
            }
        }

        if (_lessonTypesPerDay != null)
        {
            if (maxDayCount > _lessonTypesPerDay.Count)
            {
                throw new InvalidOperationException("If LessonTypes are specified, there must not be more attendances than that.");
            }
        }

        if (_datesPerDay != null)
        {
            if (maxDayCount > _datesPerDay.Count)
            {
                throw new InvalidOperationException("If attendance dates are specified, there must not be more attendances than that.");
            }
        }

        foreach (var student in _students)
        {
            if (student.Name is null)
            {
                throw new InvalidOperationException("Student name not set");
            }
            if (student.Attendances.Count != maxDayCount)
            {
                throw new InvalidOperationException(
                    $"Student {student.Name} has {student.Attendances.Count} days, expected {maxDayCount}");
            }
        }

        var names = ImmutableArray.CreateBuilder<Name>();
        {
            int count = _students.Count;
            names.SetExactSize(count);
            for (int i = 0; i < count; i++)
            {
                names[i] = _students[i].Name;
            }
        }

        var attendance = ImmutableArray.CreateBuilder<DayAttendanceList>(maxDayCount);
        attendance.SetExactSize(maxDayCount);

        var dayBuilder = ImmutableArray.CreateBuilder<Attendance>();
        for (int dayIndex = 0; dayIndex < maxDayCount; dayIndex++)
        {
            dayBuilder.SetExactSize(_students.Count);

            for (int studentIndex = 0; studentIndex < _students.Count; studentIndex++)
            {
                var student = _students[studentIndex];
                Attendance a;
                if (dayIndex < student.Attendances.Count)
                {
                    a = student.Attendances[dayIndex];
                }
                else
                {
                    a = missingDaysFiller ?? Attendance.None;
                }
                dayBuilder[studentIndex] = a;
            }
            attendance[dayIndex] = new(dayBuilder.MoveToImmutable());
        }

        ImmutableArray<LessonType>? lessonTypes = _lessonTypesPerDay is { } t
            ? [.. t]
            : null;
        ImmutableArray<AttendanceDate>? dates = _datesPerDay is { } d
            ? [.. d]
            : null;
        {

            var n = names.MoveToImmutable();
            var a = new AttendanceLists(attendance.MoveToImmutable());
            var ret = new Result(n, a, lessonTypes, dates);
            return ret;
        }
    }
}

public readonly struct AllStudentAttendanceListBuilder()
{
    private readonly Dictionary<StudentsLookupKey, StudentAttendanceListBuilder> _values = new();

    public (StudentAttendanceListBuilder Builder, bool Existed) TryList(StudentsLookupKey key)
    {
        ref var x = ref CollectionsMarshal.GetValueRefOrAddDefault(_values, key, out bool exists);
        if (!exists)
        {
            x = new();
        }
        return (x!, exists);
    }

    public StudentAttendanceListBuilder List(
        StudentsLookupKey key,
        Action<StudentAttendanceListBuilder>? b = null)
    {
        var builder = new StudentAttendanceListBuilder();
        _values.Add(key, builder);
        b?.Invoke(builder);
        return builder;
    }

    public StudentAttendanceList Build(
        Attendance? missingDaysFiller = null)
    {
        var map = new Dictionary<StudentsLookupKey, StudentAttendanceList.BetterAttendances>();
        foreach (var (key, builder) in _values)
        {
            var x = builder.Build(missingDaysFiller);
            var namesDb = NamesInDb.Create(x.Names);

            {
                var lessonTypeNotInKey = key.LessonType == LessonType.None;
                var lessonTypesSpecified = x.LessonTypes != null;
                if (lessonTypeNotInKey != lessonTypesSpecified)
                {
                    throw new InvalidOperationException("LessonType in the builder key must be null only if specifying the lesson types per lesson");
                }
            }

            if (x.LessonTypes is { } lessonTypes)
            {
                // TODO:
                // the only immutable array allocation should happen here,
                // it's also happening in StudentAttendanceListBuilder.Build
                using var buffer = OneForEach
                    .Enum<LessonType>()
                    .RentArray<ImmutableArray<DayAttendanceList>.Builder?>();
                var perLessonTypeBuilders = buffer.Span;
                perLessonTypeBuilders.Clear();

                for (int day = 0; day < lessonTypes.Length; day++)
                {
                    var lessonType = lessonTypes[day];
                    ref var perLessonBuilder = ref perLessonTypeBuilders[lessonType];
                    if (perLessonBuilder == null)
                    {
                        perLessonBuilder = ImmutableArray.CreateBuilder<DayAttendanceList>();
                    }

                    var attendanceList = x.Attendance.Day(day);
                    perLessonBuilder.Add(attendanceList);
                }

                foreach (var lessonType in lessonTypes.Distinct())
                {
                    var perLessonBuilder = perLessonTypeBuilders[lessonType];
                    Debug.Assert(perLessonBuilder != null);

                    var key1 = key with
                    {
                        LessonType = lessonType,
                    };

                    var value = new StudentAttendanceList.BetterAttendances(
                        Attendance: new(perLessonBuilder.DrainToImmutable()),
                        StudentNames: namesDb,
                        Dates: x.Dates is { } dates
                            ? lessonTypes.Select((type, index) => (Type: type, Index: index))
                                .Where(column => column.Type == lessonType)
                                .Select(column => dates[column.Index]).ToImmutableArray()
                            : null);
                    map.Add(key1, value);
                }
            }
            else
            {
                var value = new StudentAttendanceList.BetterAttendances(
                    Attendance: x.Attendance,
                    StudentNames: namesDb,
                    Dates: x.Dates);
                map.Add(key, value);
            }
        }
        return new(map);
    }
}

public readonly struct StudentAttendanceList
    : IEnumerable<KeyValuePair<StudentsLookupKey, StudentAttendanceList.BetterAttendances>>
{
    public readonly record struct BetterAttendances(
        AttendanceLists Attendance,
        NamesInDb StudentNames,
        ImmutableArray<AttendanceDate>? Dates = null);

    private readonly Dictionary<StudentsLookupKey, BetterAttendances> _map;

    public StudentAttendanceList(Dictionary<StudentsLookupKey, BetterAttendances> map)
    {
        _map = map;
    }

    public NamesInDb StudentNames(StudentsLookupKey key)
    {
        key = key.WithGroups(key.Groups.Ordered());

        if (_map.TryGetValue(key, out var list))
        {
            return list.StudentNames;
        }
        if (key.Groups.IsSingleGroup || key.Groups.IsEmpty) return NamesInDb.Empty;
        return NamesInDb.Create([.. ParentLists(key).SelectMany(list => list.Attendance.StudentNames.OrderBy(student => student.Value)
            .Select(student => student.Key))]);
    }

    private IEnumerable<(GroupId Parent, BetterAttendances Attendance)> ParentLists(StudentsLookupKey key)
    {
        foreach (var parent in key.Groups.Ordered())
            if (_map.TryGetValue(key.WithGroups(new LessonGroups { parent }), out var list)) yield return (parent, list);
    }

    public DayAttendanceList Get(AttendanceLookupKey key)
    {
        var attendanceKey = new StudentsLookupKey(
            courseId: key.CourseId,
            groups: key.Groups.Value.Ordered(),
            groupPartition: key.GroupPartition,
            lessonType: key.LessonType);
        if (_map.TryGetValue(attendanceKey, out var list)) return GetDay(key, list);
        if (attendanceKey.Groups.IsSingleGroup || attendanceKey.Groups.IsEmpty) return new([]);
        var attendance = ImmutableArray.CreateBuilder<Attendance>();
        foreach (var parent in ParentLists(attendanceKey))
        {
            // Separate sheets require dates: a shared audience's ordinal does not identify a parent's day.
            var active = key.ActiveGroups is not { } activeGroups || activeGroups.Contains(parent.Parent);
            var day = active && parent.Attendance.Dates is not null ? GetDay(key, parent.Attendance).AsArray() : [];
            if (day.Length == parent.Attendance.StudentNames.Count) attendance.AddRange(day);
            else attendance.AddRange(Enumerable.Repeat(Attendance.None, parent.Attendance.StudentNames.Count));
        }
        return new(attendance.ToImmutable());
    }

    private static DayAttendanceList GetDay(AttendanceLookupKey key, BetterAttendances list)
    {
        if (list.Dates is { } dates)
        {
            // Date-bound sheet: bind by scheduled occurrence date, never by column order.
            int found = -1;
            for (int i = 0; i < dates.Length; i++)
            {
                if (!dates[i].Matches(key.DateTime))
                {
                    continue;
                }
                if (found >= 0)
                {
                    // Ambiguous: the same date appears twice. Leave unchanged
                    // rather than writing attendance to the wrong occurrence.
                    return new([]);
                }
                found = i;
            }
            if (found < 0 || found >= list.Attendance.DayCount)
            {
                // No attendance column for this scheduled date: leave unchanged.
                return new([]);
            }
            return list.Attendance.Day(found);
        }
        if (key.DayIndex < list.Attendance.DayCount)
        {
            return list.Attendance.Day(key.DayIndex);
        }
        return new([]);
    }

    public IEnumerator<KeyValuePair<StudentsLookupKey, BetterAttendances>> GetEnumerator() => _map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public readonly record struct OptionalIndex(int Value)
{
    public static OptionalIndex Invalid => new(-1);
    public bool IsInvalid => Value < 0;
}

public readonly struct NamesInDb : IEnumerable<KeyValuePair<Name, int>>
{
    private readonly Dictionary<Name, int> _map;

    public static readonly NamesInDb Empty = Create([]);

    public static NamesInDb Create(ImmutableArray<Name> arr)
    {
        var ret = new Dictionary<Name, int>(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        for (int i = 0; i < arr.Length; i++)
        {
            var name = arr[i];
            if (!ret.TryAdd(name, i))
            {
                throw new InvalidOperationException($"Duplicate name in DB: {name}");
            }
        }
        return new(ret);
    }

    private NamesInDb(Dictionary<Name, int> map)
    {
        _map = map;
    }

    public OptionalIndex NameToIndex(Name name)
    {
        if (_map.TryGetValue(name, out int i))
        {
            return new(i);
        }
        return OptionalIndex.Invalid;
    }

    public int Count
    {
        get
        {
            return _map.Count;
        }
    }

    public IEnumerator<KeyValuePair<Name, int>> GetEnumerator() => _map.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal readonly struct StudentNameRemapHelper
{
    private readonly int Count;
    private readonly OptionalIndex[] DbToHtmlIndexMap;

    internal StudentNameRemapHelper(int count, OptionalIndex[] dbToHtmlIndexMap)
    {
        Count = count;
        DbToHtmlIndexMap = dbToHtmlIndexMap;
    }

    internal static StudentNameRemapHelper Create(
        HtmlStudent[] namesInHtml,
        NamesInDb namesInDb,
        List<Name> outNotFoundIndices,
        IReadOnlyDictionary<string, string>? studentNames = null)
    {
        var aliases = new Dictionary<Name, Name>(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var alias in studentNames ?? new Dictionary<string, string>())
            aliases.Add(NameHelper.Parse(alias.Key), NameHelper.Parse(alias.Value));
        var dbToHtmlIndexMap = new OptionalIndex[namesInDb.Count];
        var count = namesInHtml.Length;
        Array.Fill(dbToHtmlIndexMap, OptionalIndex.Invalid);

        for (int i = 0; i < namesInHtml.Length; i++)
        {
            var parser = new SequenceReader(namesInHtml[i].Name);
            var name = NameHelper.Parse(ref parser);
            var remappedIndex = namesInDb.NameToIndex(aliases.GetValueOrDefault(name, name));
            if (!remappedIndex.IsInvalid)
            {
                dbToHtmlIndexMap[remappedIndex.Value] = new(i);
            }

            if (remappedIndex.IsInvalid
                && !namesInHtml[i].IsExpelled
                // If no students are given, just ignore this completely.
                && namesInDb.Count != 0)
            {
                outNotFoundIndices.Add(name);
            }
        }
        return new(count, dbToHtmlIndexMap);
    }

    public Attendance[] RemapToHtml(ImmutableArray<Attendance> attendanceInDb)
    {
        var result = new Attendance[Count];
        for (int i = 0; i < attendanceInDb.Length; i++)
        {
            var outIndex = DbToHtmlIndexMap[i];
            if (!outIndex.IsInvalid)
            {
                result[outIndex.Value] = attendanceInDb[i];
            }
        }
        return result;
    }
}
