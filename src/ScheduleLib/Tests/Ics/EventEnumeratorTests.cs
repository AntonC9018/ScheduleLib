using ScheduleLib.Dates;

namespace Tests.Ics;

public sealed class EventEnumeratorTests
{
    private static DateOnly[] Dates(Event e) => [.. e];

    [Fact]
    public void SingleEvent_YieldsOnlyItsDate()
    {
        DateOnly date = new(year: 2026,
            month: 9,
            day: 7);
        var e = Event.CreateSingle(date);

        DateOnly[] expected =
        [
            new(year: 2026,
                month: 9,
                day: 7),
        ];
        var expected2 = expected;
        var actual2 = Dates(e);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void RecurringEvent_YieldsEveryOccurrenceUpToLast()
    {
        var first = new DateOnly(year: 2026,
            month: 9,
            day: 7);
        var e = Event.CreateRecurring(first,
            count: 3,
            interval: 7);

        var dates = Dates(e);
        DateOnly[] expected = [first, first.AddDays(7), first.AddDays(14)];
        Assert.Equal(expected: expected, actual: dates);
        Assert.Equal(expected: e.Last, actual: dates.Max());
    }
}
