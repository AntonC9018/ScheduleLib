using System.Diagnostics;

namespace ScheduleLib;

public readonly record struct Period
{
    /// <summary>
    /// </summary>
    /// <param name="start"></param>
    /// <param name="end">Exclusive</param>
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

public readonly record struct PeriodId(int Value)
{
    public static PeriodId Unspecified => new(-1);
    public bool IsUnspecified => this == Unspecified;
    public bool IsSpecified => !IsUnspecified;
}

public static class PeriodHelper
{
    public static DateOnly GetProjectedEndExclusive(this Period period)
    {
        if (period.End is { } existingEnd)
        {
            return existingEnd;
        }
        return GetStudyYearEnd(period.Start).AddDays(1);
    }

    // This might be different in other countries and stuff, this should be service ideally.
    public static DateOnly GetStudyYearEnd(DateOnly yearDate)
    {
        int year = yearDate.Year;
        if (yearDate.Month >= 9)
        {
            year++;
        }
        var ret = new DateOnly(year: year, month: 8, day: 31);
        return ret;
    }

    public static (DateOnly Start, DateOnly EndExclusive) WholePeriod(this Schedule schedule)
    {
        var min = DateOnly.MaxValue;
        foreach (var period in schedule.Periods)
        {
            if (period.Start < min)
            {
                min = period.Start;
            }
        }

        // Compute semester end, which is 31 august of the year.
        var max = min;
        foreach (var period in schedule.Periods)
        {
            var end = period.GetProjectedEndExclusive();
            if (end > max)
            {
                max = end;
            }
        }

        return new(min, max);
    }

    public static PeriodId LatestPeriodId(this Schedule schedule)
    {
        return new(schedule.Periods.Length - 1);
    }
}

