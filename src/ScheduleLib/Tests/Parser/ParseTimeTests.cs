using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class ParseTimeTests
{
    private static TimeOnly? Parse(string s)
    {
        var reader = new SequenceReader(s);
        return ParserHelper.ParseTime(ref reader);
    }

    [Fact]
    public void HourDigitsWithoutColonReturnsNull()
    {
        var value = Parse("15");
        Assert.Null(value);
    }

    [Fact]
    public void ValidTimeParses()
    {
        var expected2 = new TimeOnly(hour: 15, minute: 30);
        TimeOnly? expected3 = expected2;
        var actual2 = Parse("15:30");
        Assert.Equal(expected: expected3, actual: actual2);
    }
}
