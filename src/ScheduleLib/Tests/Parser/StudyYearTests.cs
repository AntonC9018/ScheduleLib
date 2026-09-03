namespace ScheduleLib.ParserTests;

public sealed class StudyYearTests
{
    [Fact]
    public void RejectsTwoDigitYears()
    {
        // Group labels carry the year modulo 100; passing such a form would silently
        // corrupt grade determination.
        Assert.Throws<ArgumentOutOfRangeException>(() => new StudyYear(26));
    }

    [Fact]
    public void KeepsTheFullYearAndTheModuloForm()
    {
        var year = new StudyYear(2026);

        Assert.Equal(2026, year.Value);
        Assert.Equal(26, year.Mod100);
        Assert.Equal("2026", year.ToString());
        Assert.True(year == new StudyYear(2026));
        Assert.True(year.CompareTo(new StudyYear(2027)) < 0);
    }
}
