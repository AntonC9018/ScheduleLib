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
        global::System.TimeOnly? value = Parse("15");
        Assert.Null(value);
    }

    [Fact]
    public void ValidTimeParses()
    {
        global::System.TimeOnly expected2 = new TimeOnly(hour: 15, minute: 30);
        global::System.TimeOnly? expected3 = expected2;
        global::System.TimeOnly? actual2 = Parse("15:30");
        Assert.Equal(expected: expected3, actual: actual2);
    }
}
