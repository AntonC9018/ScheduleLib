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
        Assert.Null(Parse("15"));
    }

    [Fact]
    public void ValidTimeParses()
    {
        Assert.Equal(new TimeOnly(15, 30), Parse("15:30"));
    }
}
