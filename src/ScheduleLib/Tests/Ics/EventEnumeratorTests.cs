using ScheduleLib.Dates;

namespace Tests.Ics;

public class EventEnumeratorTests
{
    private static DateOnly[] Dates(Event e) => [.. e];

    [Fact]
    public void SingleEvent_YieldsOnlyItsDate()
    {
        var e = Event.CreateSingle(new(2026, 9, 7));

        DateOnly[] expected = [new(2026, 9, 7)];
        Assert.Equal(expected, Dates(e));
    }

    [Fact]
    public void RecurringEvent_YieldsEveryOccurrenceUpToLast()
    {
        var first = new DateOnly(2026, 9, 7);
        var e = Event.CreateRecurring(first, count: 3, interval: 7);

        var dates = Dates(e);
        DateOnly[] expected = [first, first.AddDays(7), first.AddDays(14)];
        Assert.Equal(expected, dates);
        Assert.Equal(e.Last, dates.Max());
    }
}
