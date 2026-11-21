using ScheduleLib.Application.Config;

namespace MainCli.Tests;

public sealed class DayHelperTests
{
    [Fact]
    public void GetDayOfThisWeekHandlesSundayAsEndOfCurrentWeek()
    {
        var sunday = new DateOnly(
            year: 2026,
            month: 11,
            day: 22);
        var monday = sunday.GetDayOfThisWeek(DayOfWeek.Monday);
        var expected = new DateOnly(
            year: 2026,
            month: 11,
            day: 16);
        Assert.Equal(expected: expected, actual: monday);
    }
}
