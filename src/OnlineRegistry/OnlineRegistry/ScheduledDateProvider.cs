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
    public required StudyWeek[] StudyWeeks { private get; init; }

    public IEnumerable<DateOnly> Dates(GetScheduledDatesParams p)
    {
        var e = new DayEnumerator(StudyWeeks, p.Day);
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

        while (true)
        {
            var ret = e.Date;
            if (ret >= p.To)
            {
                break;
            }
            if (IsParityMatch())
            {
                yield return ret;
            }

            if (!e.MoveNext())
            {
                yield break;
            }
            continue;

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
                        Debug.Fail("Impossible value of parity");
                        return false;
                    }
                }
            }
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
