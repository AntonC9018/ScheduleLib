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

    private const int NoMaxCount = -1;
    private int _maxCountHint = NoMaxCount;

    public void Clear()
    {
        _lessonTypesPerDay = null;
        _allNames.Clear();
        _students.Clear();
    }

    public void AddLessonTypes(ReadOnlySpan<LessonType> lessonTypes)
    {
        _lessonTypesPerDay ??= new();
        _lessonTypesPerDay.AddRange(lessonTypes);
    }

    public StudentAttendanceBuilder Student(Name name)
    {
        if (!_allNames.Add(name))
        {
            throw new InvalidOperationException($"Duplicate student name: {name}");
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

        internal Result(
            ImmutableArray<Name> Names,
            AttendanceLists Attendance,
            ImmutableArray<LessonType>? LessonTypes)
        {
            Debug.Assert(Names.Length == Attendance.StudentCount);
            if (LessonTypes is { } lt)
            {
                Debug.Assert(lt.Length == Attendance.DayCount);
            }

            this.Names = Names;
            this.Attendance = Attendance;
            this.LessonTypes = LessonTypes;
        }
    }

    public Result Build(
        Attendance? missingDaysFiller)
    {
        var maxDayCount = _students.Max(x => x.Attendances.Count);
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
        {

            var n = names.MoveToImmutable();
            var a = new AttendanceLists(attendance.MoveToImmutable());
            var ret = new Result(n, a, lessonTypes);
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

                foreach (var lessonType in lessonTypes)
                {
                    var perLessonBuilder = perLessonTypeBuilders[lessonType];
                    Debug.Assert(perLessonBuilder != null);

                    var key1 = key with
                    {
                        LessonType = lessonType,
                    };

                    var value = new StudentAttendanceList.BetterAttendances(
                        Attendance: new(perLessonBuilder.DrainToImmutable()),
                        StudentNames: namesDb);
                    map.Add(key1, value);
                }
            }
            else
            {
                var value = new StudentAttendanceList.BetterAttendances(
                    Attendance: x.Attendance,
                    StudentNames: namesDb);
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
        NamesInDb StudentNames);

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
        return NamesInDb.Empty;
    }

    public DayAttendanceList Get(AttendanceLookupKey key)
    {
        var attendanceKey = new StudentsLookupKey(
            courseId: key.CourseId,
            groups: key.Groups.Value.Ordered(),
            subGroup: key.SubGroup,
            lessonType: key.LessonType);
        // TODO: Should work for any subset of the groups, currently it does not.
        if (_map.TryGetValue(attendanceKey, out var list)
            && key.DayIndex < list.Attendance.DayCount)
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
        List<Name> outNotFoundIndices)
    {
        var dbToHtmlIndexMap = new OptionalIndex[namesInDb.Count];
        var count = namesInHtml.Length;
        Array.Fill(dbToHtmlIndexMap, OptionalIndex.Invalid);

        for (int i = 0; i < namesInHtml.Length; i++)
        {
            var parser = new Parser(namesInHtml[i].Name);
            var name = NameHelper.Parse(ref parser);
            var remappedIndex = namesInDb.NameToIndex(name);
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

