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
    public void RejectsYearsBelow1900()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StudyYear(1899));
    }

    [Fact]
    public void KeepsTheFullYearAndTheModuloForm()
    {
        var year = new StudyYear(2026);

        Assert.Equal(expected: 2026, actual: year.Value);
        Assert.Equal(expected: 26, actual: year.Mod100);
        Assert.Equal(expected: "2026", actual: year.ToString());
        Assert.True(year == new StudyYear(2026));

        StudyYear CalculateOther()
        {
            var other = new StudyYear(2027);
            return other;
        }

        bool CalculateCondition()
        {
            int compareTo = year.CompareTo(CalculateOther());
            bool condition = compareTo < 0;
            return condition;
        }

        Assert.True(CalculateCondition());
    }
}
