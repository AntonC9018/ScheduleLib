using System.Collections;
using System.Diagnostics;
using AutoConstructor.Attributes;

namespace ScheduleLib.OnlineRegistry;

public struct GetScheduledDatesParams()
{
    public required Parity Parity { get; set; }
    public required DayOfWeek Day { get; set; }
    public DateOnly From { get; set; } = DateOnly.MinValue;
    public DateOnly To { get; set; } = DateOnly.MaxValue;
}

public readonly record struct ScheduledItem
{
    public readonly DateOnly First;
    public readonly DateOnly Last;
    public readonly int Count;
    public readonly int DayInterval;

    public bool IsRepeated => Count > 1;

    public static ScheduledItem CreateSingle(DateOnly date)
    {
        return new(
            first: date,
            last: date,
            count: 1,
            interval: 0);
    }

    public static ScheduledItem CreateRepeated(
        DateOnly first,
        DateOnly last,
        int count,
        int interval)
    {
        return new(
            first: first,
            last: last,
            count: count,
            interval: interval);
    }

    public ScheduledItem(
        DateOnly first,
        DateOnly last,
        int count,
        int interval)
    {
        Debug.Assert(count >= 1);
        if (count > 1)
        {
            Debug.Assert(interval >= 1);
            Debug.Assert(last > first);
            Debug.Assert(last.DayNumber - first.DayNumber == interval * count - 1);
        }
        else
        {
            Debug.Assert(last == first);
            Debug.Assert(interval == 0);
        }

        First = first;
        Last = last;
        Count = count;
        DayInterval = interval;
    }

    public Enumerator GetEnumerator() => new(this);

    public struct Enumerator : IEnumerator<DateOnly>
    {
        private readonly ScheduledItem _item;
        public DateOnly Current { get; private set; }
        private int _count;

        public Enumerator(ScheduledItem item)
        {
            _item = item;
            _count = -1;
            Current = DateOnly.MinValue;
        }

        public bool MoveNext()
        {
            if (_count == 0)
            {
                Current = _item.First;
                _count = 1;
                return true;
            }
            if (_count <= _item.Count)
            {
                var c = Current;
                c = c.AddDays(_item.DayInterval);
                Debug.Assert(c < _item.Last);
                Current = c;
                _count++;
                return true;
            }
            return false;
        }

        object IEnumerator.Current => Current;

        public void Dispose()
        {
        }
        public void Reset()
        {
            throw new NotSupportedException();
        }
    }
}

public interface IAllScheduledDateProvider
{
    IEnumerable<DateOnly> Dates(GetScheduledDatesParams p);
}

public interface IAllScheduledItemsProvider
{
    IEnumerable<ScheduledItem> Items(GetScheduledDatesParams p);
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
            if (ShouldInclude())
            {
                yield return ret;
            }
            if (!e.MoveNext())
            {
                yield break;
            }
            continue;

            bool ShouldInclude()
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
                        throw Unreachable();
                    }
                }
            }
        }
    }
}


public static class ScheduledDateToItemsTransformerHelper
{
    public static int GetScheduledInterval(this Parity parity)
    {
        int interval = 7;
        if (parity != Parity.EveryWeek)
        {
            interval *= 2;
        }
        return interval;
    }

    public static IEnumerable<ScheduledItem> TransformDatesToItems(
        this IEnumerable<DateOnly> dates,
        int interval)
    {
        DateOnly first = default;
        DateOnly prev = default;
        int count = 0;

        foreach (var date in dates)
        {
            if (count > 0
                && date.DayNumber - prev.DayNumber != interval)
            {
                yield return new ScheduledItem(
                    first: first,
                    last: prev,
                    count: count,
                    interval: interval);
                first = default;
                count = 0;
            }

            if (count == 0)
            {
                first = date;
                prev = first;
                count++;
            }
        }
    }
}

[AutoConstructor]
public sealed partial class ScheduledItemsProviderTransformer
    : IAllScheduledItemsProvider
{
    private readonly IAllScheduledDateProvider _impl;

    public IEnumerable<ScheduledItem> Items(GetScheduledDatesParams p)
    {
        var t = _impl.Dates(p);
        var interval = p.Parity.GetScheduledInterval();
        var ret = t.TransformDatesToItems(interval);
        return ret;
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
            Debug.Assert(current.Start <= d);
            if (current.EndExclusive > d)
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
    public readonly DateOnly Date => Week.GetDayOfThisWeek(_day);
}
