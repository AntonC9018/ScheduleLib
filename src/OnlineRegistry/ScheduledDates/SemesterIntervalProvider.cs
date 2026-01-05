using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ScheduleLib.OnlineRegistry;

public readonly struct GetSemesterIntervalParams
{
    public required Schedule Schedule { get; init; }
    public required GroupId GroupId { get; init; }
    public required Semester Semester { get; init; }
}

public readonly record struct SemesterDateRange
{
    public required DateOnly Start { get; init; }
    public required DateOnly End { get; init; }

    public bool Contains(DateOnly date)
    {
        if (date < Start)
        {
            return false;
        }
        if (date > End)
        {
            return false;
        }
        return true;
    }
}

[InlineArray((int) Semester.Count)]
public struct YearDateRange
{
    private Semester _value;
    public readonly int Count => (int) Semester.Count;
}

public sealed class YearDateRangeBuilderModel
{
    public const int MinYearInExistingDate = 1;
    public Semester Semester = Semester.Invalid;
    public AttendanceMode AttendanceMode = AttendanceMode.Invalid;
    public SemesterDateRange LessonDateRange = new()
    {
        Start = DateOnly.MinValue,
        End = DateOnly.MaxValue,
    };
    public QualificationType QualificationType = QualificationType.Invalid;
    public Grade Grade = Grade.Invalid;
    public int Year = -1;

    public void CopyTo(YearDateRangeBuilderModel other)
    {
        other.Semester = Semester;
        other.AttendanceMode = AttendanceMode;
        other.LessonDateRange = LessonDateRange;
        other.QualificationType = QualificationType;
        other.Grade = Grade;
        other.Year = Year;
    }
}

public sealed class YearDateRangeBuilder
{
    public YearDateRangeBuilderModel Model { get; }

    internal YearDateRangeBuilder(YearDateRangeBuilderModel model)
    {
        Model = model;
    }

    public void Semester(Semester s)
    {
        Model.Semester = s;
    }

    public void AttendanceMode(AttendanceMode mode)
    {
        Model.AttendanceMode = mode;
    }

    public void LessonsStart(DateOnly start)
    {
        Model.LessonDateRange = Model.LessonDateRange with { Start = start };
    }

    public void LessonsEnd(DateOnly end)
    {
        Model.LessonDateRange = Model.LessonDateRange with { End = end };
    }

    public void QualificationType(QualificationType type)
    {
        Model.QualificationType = type;
    }

    public void Grade(Grade grade)
    {
        Debug.Assert(grade.Value <= 3);
        Model.Grade = grade;
    }

    public void Year(int year)
    {
        Debug.Assert(year >= 2000);
        Model.Year = year;
    }

    public void LessonsStart(int month, int day)
    {
        LessonsStart(new DateOnly(year: YearDateRangeBuilderModel.MinYearInExistingDate, month, day));
    }

    public void LessonsEnd(int month, int day)
    {
        LessonsEnd(new DateOnly(year: YearDateRangeBuilderModel.MinYearInExistingDate, month, day));
    }
}

public sealed class YearDateRangeBuilderScope
{
    private readonly SemesterIntervalBuilder _mainBuilder;
    private readonly YearDateRangeBuilder _builder;

    internal YearDateRangeBuilderScope(
        SemesterIntervalBuilder mainBuilder,
        YearDateRangeBuilderModel? model = null)
    {
        _mainBuilder = mainBuilder;
        _builder = new(model ?? new());
    }

    public YearDateRangeBuilder Range(Action<YearDateRangeBuilder>? f = null)
    {
        var prototype = _builder.Model;
        var x = _mainBuilder.Range();
        prototype.CopyTo(x.Model);
        f?.Invoke(x);
        return x;
    }

    public YearDateRangeBuilderScope Scope(Action<YearDateRangeBuilderScope>? f = null)
    {
        var model = new YearDateRangeBuilderModel();
        var prototype = _builder.Model;
        prototype.CopyTo(model);
        var s = new YearDateRangeBuilderScope(_mainBuilder, model);
        f?.Invoke(s);
        return s;
    }

    public void Semester(Semester s) => _builder.Semester(s);
    public void AttendanceMode(AttendanceMode mode) => _builder.AttendanceMode(mode);
    public void LessonsStart(DateOnly start) => _builder.LessonsStart(start);
    public void LessonsEnd(DateOnly end) => _builder.LessonsEnd(end);
    public void QualificationType(QualificationType type) => _builder.QualificationType(type);
    public void Grade(Grade grade) => _builder.Grade(grade);
    public void Year(int year) => _builder.Year(year);
    public void LessonsStart(int month, int day) => _builder.LessonsStart(month, day);
    public void LessonsEnd(int month, int day) => _builder.LessonsEnd(month, day);
}

public sealed class SemesterIntervalBuilder
{
    private List<YearDateRangeBuilderModel> _models = new();

    public YearDateRangeBuilderScope Scope(Action<YearDateRangeBuilderScope>? f = null)
    {
        var scope = new YearDateRangeBuilderScope(this);
        f?.Invoke(scope);
        return scope;
    }

    public YearDateRangeBuilder Range(Action<YearDateRangeBuilder>? f = null)
    {
        var model = new YearDateRangeBuilderModel();
        _models.Add(model);
        var builder = new YearDateRangeBuilder(model);
        f?.Invoke(builder);
        return builder;
    }

    public SemesterIntervalProvider Build()
    {
        foreach (var model in _models)
        {
            if (model.Year <= 0)
            {
                continue;
            }
            ref var d = ref model.LessonDateRange;
            d = new SemesterDateRange
            {
                Start = d.Start.WithYearIfNoYear(model.Year),
                End = d.End.WithYearIfNoYear(model.Year),
            };
        }
        foreach (var model in _models)
        {
            if (model.Semester == Semester.Invalid)
            {
                throw new InvalidOperationException("Semester not specified");
            }
            if (model.AttendanceMode == AttendanceMode.Invalid)
            {
                throw new InvalidOperationException("AttendanceMode not specified");
            }
            if (model.QualificationType == QualificationType.Invalid)
            {
                throw new InvalidOperationException("QualificationType not specified");
            }
            ref var d = ref model.LessonDateRange;
            if (d.Start == DateOnly.MinValue)
            {
                throw new InvalidOperationException("Start date not specified");
            }
            if (d.End == DateOnly.MaxValue)
            {
                throw new InvalidOperationException("End date not specified");
            }
            if (model.Year <= 0)
            {
                if (d.Start.Year == YearDateRangeBuilderModel.MinYearInExistingDate)
                {
                    throw new InvalidOperationException("Year not specified");
                }
                if (d.End.Year == YearDateRangeBuilderModel.MinYearInExistingDate)
                {
                    throw new InvalidOperationException("Year not specified");
                }
            }
            if (d.Start > d.End)
            {
                throw new InvalidOperationException("Start date is after end date");
            }
        }

        Dictionary<DateRangeKey, SemesterDateRange> map = new(_models.Count);
        foreach (var model in _models)
        {
            var key = new DateRangeKey(
                model.Grade,
                model.Semester,
                model.AttendanceMode,
                model.QualificationType);
            if (map.TryAdd(key, model.LessonDateRange))
            {
                continue;
            }

            throw new InvalidOperationException($"Two range configurations found for the same key: {key}");
        }

        var ranges = new YearDateRanges(map);
        return new(ranges);
    }
}

internal record struct DateRangeKey(
    Grade grade,
    Semester Semester,
    AttendanceMode AttendanceMode,
    QualificationType QualificationType)
{
}

internal readonly struct YearDateRanges
{
    private readonly Dictionary<DateRangeKey, SemesterDateRange> _values;

    public YearDateRanges(Dictionary<DateRangeKey, SemesterDateRange> map)
    {
        _values = map;
    }

    public SemesterDateRange Get(DateRangeKey key)
    {
        return _values[key];
    }
}

public sealed class SemesterIntervalProvider
{
    internal YearDateRanges Ranges { get; }

    internal SemesterIntervalProvider(YearDateRanges ranges)
    {
        Ranges = ranges;
    }

    public SemesterDateRange GetSemesterInterval(GetSemesterIntervalParams p)
    {
        var group = p.Schedule.Get(p.GroupId);
        var grade = group.Grade;
        var semester = p.Semester;
        var key = new DateRangeKey(
            grade,
            semester,
            group.AttendanceMode,
            group.QualificationType);
        var ret = Ranges.Get(key);
        return ret;
    }
}

file static class Helper
{
    public static DateOnly WithYear(this DateOnly d, int year) => new(year, d.Month, d.Day);
    public static DateOnly WithYearIfNoYear(this DateOnly d, int year)
    {
        if (d.Year == YearDateRangeBuilderModel.MinYearInExistingDate)
        {
            return d.WithYear(year);
        }
        return d;
    }
}
