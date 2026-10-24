using ScheduleLib.Dates;

namespace Tests.Ics;

public sealed class EventEnumeratorTests
{
    private static DateOnly[] Dates(Event e) => [.. e];

    [Fact]
    public void SingleEvent_YieldsOnlyItsDate()
    {
        var e = Event.CreateSingle(new(year: 2026, month: 9, day: 7));

        DateOnly[] expected = [new(year: 2026, month: 9, day: 7)];
        Assert.Equal(expected: expected, actual: Dates(e));
    }

    [Fact]
    public void RecurringEvent_YieldsEveryOccurrenceUpToLast()
    {
        var first = new DateOnly(year: 2026, month: 9, day: 7);
        var e = Event.CreateRecurring(first, count: 3, interval: 7);

        var dates = Dates(e);
        DateOnly[] expected = [first, first.AddDays(7), first.AddDays(14)];
        Assert.Equal(expected: expected, actual: dates);
        Assert.Equal(expected: e.Last, actual: dates.Max());
    }
}
