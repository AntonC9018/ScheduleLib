using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;

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

public readonly struct DayAttendanceBuilder
{
    private readonly ImmutableArray<Attendance>.Builder _builder;

    public DayAttendanceBuilder(int len)
    {
        _builder = ImmutableArray.CreateBuilder<Attendance>(len);

        // It does not say anything about filling with zeros in the docs
        for (int i = 0; i < _builder.Count; i++)
        {
            _builder[i] = Attendance.None;
        }
    }

    public void Set(StudentIndex index, Attendance attendance)
    {
        Debug.Assert(attendance is not Attendance.Grade and not Attendance.None);

        _builder[index.Index] = attendance;
    }

    public ImmutableArray<Attendance> Build(ImmutableArray<Name> context)
    {
        for (int i = 0; i < _builder.Count; i++)
        {
            if (_builder[i] == Attendance.None)
            {
                throw new InvalidOperationException($"Student {context[i]} not initialized");
            }
        }

        var ret = _builder.MoveToImmutable();
        return ret;
    }
}

public readonly struct StudentAttendanceBuilder(Name name)
{
    internal Name? Name { get; } = name;
    internal readonly ImmutableArray<Attendance>.Builder Attendances =
        ImmutableArray.CreateBuilder<Attendance>();

    public void Day(Attendance attendance)
    {
        Attendances.Add(attendance);
    }
}

public sealed class StudentAttendanceListBuilder()
{
    private readonly List<StudentAttendanceBuilder> _students = new();
    private readonly HashSet<Name> _allNames = new();

    private const int NoMaxCount = -1;
    private int _maxCountHint = NoMaxCount;

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

    public (ImmutableArray<Name> Names, ImmutableArray<ImmutableArray<Attendance>> Attendance) Build(
        Attendance? missingDaysFiller)
    {
        var names = ImmutableArray.CreateBuilder<Name>(_students.Count);
        var attendance = ImmutableArray.CreateBuilder<ImmutableArray<Attendance>>(_students.Count);

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
            names.Add(student.Name);
            attendance.Add(student.Attendances.DrainToImmutable());
        }

        return (names.MoveToImmutable(), attendance.MoveToImmutable());
    }
}

public readonly struct AllStudentAttendanceListBuilder()
{
    private readonly Dictionary<StudentsLookupKey, StudentAttendanceListBuilder> _values = new();

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
        var map = new Dictionary<StudentsLookupKey, StudentAttendanceList.AttendanceList>();
        foreach (var (key, builder) in _values)
        {
            var (names, attendance) = builder.Build(missingDaysFiller);
            var value = new StudentAttendanceList.AttendanceList(
                Attendance: attendance,
                StudentNames: NamesInDb.Create(names));
            map.Add(key, value);
        }
        return new(map);
    }
}

public readonly struct StudentAttendanceList
    : IEnumerable<KeyValuePair<StudentsLookupKey, StudentAttendanceList.AttendanceList>>
{
    public readonly record struct AttendanceList(
        ImmutableArray<ImmutableArray<Attendance>> Attendance,
        NamesInDb StudentNames);

    private readonly Dictionary<StudentsLookupKey, AttendanceList> _map;

    public StudentAttendanceList(Dictionary<StudentsLookupKey, AttendanceList> map)
    {
        _map = map;
    }

    public NamesInDb StudentNames(StudentsLookupKey key)
    {
        if (_map.TryGetValue(key, out var list))
        {
            return list.StudentNames;
        }
        return NamesInDb.Empty;
    }

    public ImmutableArray<Attendance> Get(AttendanceLookupKey key)
    {
        var attendanceKey = new StudentsLookupKey
        {
            CourseId = key.CourseId,
            GroupId = key.GroupId,
            SubGroup = key.SubGroup,
            LessonType = key.LessonType,
        };
        if (_map.TryGetValue(attendanceKey, out var list)
            && key.DayIndex < list.Attendance.Length)
        {
            return list.Attendance[key.DayIndex];
        }
        return [];
    }

    public IEnumerator<KeyValuePair<StudentsLookupKey, AttendanceList>> GetEnumerator() => _map.GetEnumerator();
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

        for (int i = 0; i < namesInHtml.Length; i++)
        {
            var parser = new Parser(namesInHtml[i].Name);
            var name = NameHelper.ParseName(ref parser);
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

