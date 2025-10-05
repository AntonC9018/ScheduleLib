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
    Present, // <empty>
    Grade, // left alone if this is found
}

public static class AttendanceHelper
{
    public static Attendance Parse(string value)
    {
        switch (value)
        {
            case "a":
                return Attendance.NotPresent;
            case null or "":
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

public readonly struct StudentAttendanceListBuilder()
{
    private readonly List<StudentAttendanceBuilder> _students = new();
    private readonly HashSet<Name> _allNames = new();

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

    public (ImmutableArray<Name> Names, ImmutableArray<ImmutableArray<Attendance>> Attendance) Build()
    {
        var names = ImmutableArray.CreateBuilder<Name>(_students.Count);
        var attendance = ImmutableArray.CreateBuilder<ImmutableArray<Attendance>>(_students.Count);

        int dayCount = -1;
        foreach (var student in _students)
        {
            if (student.Name is null)
            {
                throw new InvalidOperationException("Student name not set");
            }
            if (dayCount != -1
                && student.Attendances.Count != dayCount)
            {
                throw new InvalidOperationException(
                    $"Inconsistent day count for student {student.Name}: {student.Attendances.Count} (expected {dayCount})");
            }
            if (dayCount == -1)
            {
                dayCount = student.Attendances.Count;
            }
            names.Add(student.Name);
            attendance.Add(student.Attendances.MoveToImmutable());
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

    public StudentAttendanceList Build()
    {
        var map = new Dictionary<StudentsLookupKey, StudentAttendanceList.AttendanceList>();
        foreach (var (key, builder) in _values)
        {
            var (names, attendance) = builder.Build();
            var value = new StudentAttendanceList.AttendanceList(
                Attendance: attendance,
                StudentNames: NamesInDb.Create(names));
            map.Add(key, value);
        }
        return new(map);
    }
}

public readonly struct StudentAttendanceList
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
        };
        if (_map.TryGetValue(attendanceKey, out var list))
        {
            return list.Attendance[key.DayIndex];
        }
        return [];
    }
}

public readonly record struct OptionalIndex(int Value)
{
    public static OptionalIndex Invalid => new(-1);
    public bool IsInvalid => Value < 0;
}

public readonly struct NamesInDb
{
    private readonly Dictionary<Name, int> _map;

    public static readonly NamesInDb Empty = Create([]);

    public static NamesInDb Create(ImmutableArray<Name> arr)
    {
        var ret = new Dictionary<Name, int>();
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

    public NamesInDb(Dictionary<Name, int> map)
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
            dbToHtmlIndexMap[i] = remappedIndex;

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

