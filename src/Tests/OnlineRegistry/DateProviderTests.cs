namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class DateProviderTests
{
    [Fact]
    public void TestHoliday()
    {
        var monday = new DateOnly(year: 2025, month: 4, day: 25).GetDayOfThisWeek(DayOfWeek.Monday);
        var provider = new ManualAllScheduledDateProvider(
            studyWeeks: [
                new StudyWeek(monday, isOddWeek: true),
            ],
            holidays: [
                new HolidayPeriod(monday, monday.AddDays(5)),
            ]);
        var ret = provider.Dates(new()
        {
            Day = DayOfWeek.Monday,
            Parity = Parity.EveryWeek,
            From = monday,
            To = monday.AddDays(7),
        });
        Assert.Empty(ret);
    }

    [Fact]
    public void TestNoHoliday()
    {
        var monday = new DateOnly(year: 2025, month: 4, day: 25).GetDayOfThisWeek(DayOfWeek.Monday);
        var provider = new ManualAllScheduledDateProvider(
            studyWeeks: [
                new StudyWeek(monday, isOddWeek: true),
            ],
            holidays: []);
        var ret = provider.Dates(new()
        {
            Day = DayOfWeek.Monday,
            Parity = Parity.EveryWeek,
            From = monday,
            To = monday.AddDays(7),
        });
        Assert.Single(ret, monday);
    }
}

file static class Extensions
{
}

