using System.Diagnostics;

namespace ScheduleLib.OnlineRegistry;

public struct GetScheduledDatesParams()
{
    public required Parity Parity { get; set; }
    public required DayOfWeek Day { get; set; }
    public DateOnly From { get; set; } = DateOnly.MinValue;
    public DateOnly To { get; set; } = DateOnly.MaxValue;
}

public interface IAllScheduledDateProvider
{
    IEnumerable<DateOnly> Dates(GetScheduledDatesParams p);
}

public sealed class ManualAllScheduledDateProvider
    : IAllScheduledDateProvider
{
    private readonly StudyWeek[] _studyWeeks;
    private readonly HolidayPeriod[] _holidays;

    public ManualAllScheduledDateProvider(
        StudyWeek[] studyWeeks,
        HolidayPeriod[] holidays)
    {
        Debug.Assert(studyWeeks.IsSorted(x => x.MondayDate));
        Debug.Assert(holidays.IsSorted(x => x.Start));

        _studyWeeks = studyWeeks;
        _holidays = holidays;
    }

    public IEnumerable<DateOnly> Dates(GetScheduledDatesParams p)
    {
        var e = new DayEnumerator(_studyWeeks, p.Day);
        while (true)
        {
            if (!e.MoveNext())
            {
                yield break;
            }
            if (e.Date < p.From)
            {
                continue;
            }
            break;
        }

        var holidayE = new HolidayChecker(_holidays);

        while (true)
        {
            var ret = e.Date;
            if (ret >= p.To)
            {
                break;
            }
            if (ShouldYield())
            {
                yield return ret;
            }
            if (!e.MoveNext())
            {
                yield break;
            }
            continue;

            bool ShouldYield()
            {
                if (!IsParityMatch())
                {
                    return false;
                }
                if (holidayE.OrderedCheckIsHoliday(ret))
                {
                    return false;
                }
                return true;
            }

            bool IsParityMatch()
            {
                switch (p.Parity)
                {
                    case Parity.EveryWeek:
                    {
                        return true;
                    }
                    case Parity.OddWeek:
                    {
                        return e.Week.IsOddWeek;
                    }
                    case Parity.EvenWeek:
                    {
                        return !e.Week.IsOddWeek;
                    }
                    default:
                    {
                        throw UnreachableHelper.Unreachable();
                    }
                }
            }
        }
    }
}

file struct HolidayChecker
{
    private int _index;
    private readonly HolidayPeriod[] _holidays;

    public HolidayChecker(HolidayPeriod[] holidays)
    {
        Debug.Assert(holidays.IsSorted(x => x.Start));

        _index = 0;
        _holidays = holidays;
    }

    private bool HasValue => _index < _holidays.Length;

    public bool OrderedCheckIsHoliday(DateOnly d)
    {
        while (true)
        {
            if (!HasValue)
            {
                return false;
            }

            var current = _holidays[_index];
            if (current.Start > d)
            {
                return false;
            }
            Debug.Assert(current.Start >= d);
            if (current.EndExclusive < d)
            {
                return true;
            }
            _index++;
        }
    }
}

file struct DayEnumerator
{
    private int _index;
    private readonly StudyWeek[] _weeks;
    private readonly DayOfWeek _day;

    public DayEnumerator(StudyWeek[] weeks, DayOfWeek day)
    {
        _index = -1;
        _weeks = weeks;
        _day = day;
    }

    public bool MoveNext()
    {
        _index++;
        if (_index >= _weeks.Length)
        {
            return false;
        }
        return true;
    }

    public readonly StudyWeek Week => _weeks[_index];
    public readonly DateOnly Date
    {
        get
        {
            const int weekdayCount = 7;
            var offset = (_day - DayOfWeek.Monday + weekdayCount) % weekdayCount;
            var ret = Week.MondayDate.AddDays(offset);
            return ret;
        }
    }
}
