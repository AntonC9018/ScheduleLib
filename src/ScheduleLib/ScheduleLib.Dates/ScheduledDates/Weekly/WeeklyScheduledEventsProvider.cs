using System.Diagnostics;
using System.Collections;
using AutoConstructor.Attributes;

namespace ScheduleLib.Dates;

public readonly record struct Event
{
    public readonly DateOnly First;
    public readonly DateOnly Last;
    public readonly int Count;
    public readonly int DayInterval;

    public bool IsRepeated => Count > 1;

    public static Event CreateSingle(DateOnly date)
    {
        return new(
            first: date,
            last: date,
            count: 1,
            interval: 0);
    }

    public static Event Create(
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

    private static DateOnly FirstToLast(
        DateOnly first,
        int count,
        int interval)
    {
        int int32 = interval;
        int int322 = count - 1;
        var days = int32 * (int322);
        return first.AddDays(days);
    }

    public static Event CreateRecurring(
        DateOnly first,
        int count,
        int interval)
    {
        global::System.DateOnly first2 = first;
        global::System.DateOnly last2 = FirstToLast(first, count: count, interval: interval);
        int count2 = count;
        int interval2 = interval;
        global::ScheduleLib.Dates.Event eventValue = new(
                    first: first2, last: last2, count: count2, interval: interval2);
        return eventValue;
    }

    public Event(
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
            var expectedLast = FirstToLast(first, count: count, interval: interval);
            Debug.Assert(expectedLast == last);
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
        private readonly Event _item;
        public DateOnly Current { get; private set; }
        private int _index;

        public Enumerator(Event item)
        {
            _item = item;
            _index = 0;
            Current = default;
        }

        public bool MoveNext()
        {
            if (_index >= _item.Count)
            {
                return false;
            }
            Current = _item.First.AddDays(_item.DayInterval * _index);
            _index++;
            return true;
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

public interface IWeeklyScheduledEventsProvider
{
    IEnumerable<Event> Events(GetScheduledDatesParams p);
}

public static class ScheduledDatesToEventsTransformerHelper
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

    public static IEnumerable<Event> TransformDatesToEvents(
        this IEnumerable<DateOnly> dates,
        int interval)
    {
        DateOnly first = default;
        DateOnly prev = default;
        int count = 0;

        using var dateE = dates.GetEnumerator();
        while (true)
        {
            bool hasValue = dateE.MoveNext();
            var date = hasValue ? dateE.Current : default;
            bool ShouldOutput()
            {
                if (count == 0)
                {
                    return false;
                }
                if (!hasValue)
                {
                    return true;
                }
                if (date.DayNumber - prev.DayNumber != interval)
                {
                    return true;
                }
                return false;
            }
            if (ShouldOutput())
            {
                int CalculateInterval()
                {
                    int interval2;
                    if (count > 1)
                    {
                        interval2 = interval;
                    }
                    else
                    {
                        interval2 = 0;
                    }

                    return interval2;
                }

                yield return new Event(
                                    first: first,
                                    last: prev,
                                    count: count,
                                    interval: CalculateInterval());
                first = default;
                count = 0;
            }

            if (!hasValue)
            {
                break;
            }

            if (count == 0)
            {
                first = date;
            }

            prev = date;
            count++;
        }
    }
}

[AutoConstructor]
public sealed partial class ScheduledEventsProviderTransformer
    : IWeeklyScheduledEventsProvider
{
    private readonly IWeeklyScheduledDateProvider _impl;

    public IEnumerable<Event> Events(GetScheduledDatesParams p)
    {
        var t = _impl.Dates(p);
        var interval = p.Parity.GetScheduledInterval();
        var ret = t.TransformDatesToEvents(interval);
        return ret;
    }
}

