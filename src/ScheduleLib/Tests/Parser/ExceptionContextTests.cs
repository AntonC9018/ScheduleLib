using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

public sealed class ExceptionContextTests
{
    [Fact]
    public void OverlapContextKeepsTheFailureTimeSnapshot()
    {
        var errors = new List<string>
        {
            "first overlap",
            "second overlap",
        };
        var error = OverlappingLessonsException.ForOverlaps(errors);

        errors[0] = "changed later";
        errors.Add("another overlap");

        Assert.Equal(expected: 2, actual: error.ErrorCount);
        Assert.Equal(expected: ["first overlap", "second overlap"], actual: error.Errors);
        Assert.Equal(expected: "The schedule has 2 overlapping lesson pairs:\nfirst overlap\nsecond overlap",
            actual: error.Message);
    }

    [Fact]
    public void MissingMergeCommandRetainsItsOriginalFailure()
    {
        var error = new UnsupportedMergeCellCommandException(null);

        Assert.Null(error.Command);
        Assert.Equal(expected: "Unsupported merge cell command: ", actual: error.Message);
    }

    [Fact]
    public void ParserContextDoesNotRetainMutableSpanStorage()
    {
        var characters = "unknown-day".ToCharArray();
        ScheduleBuildException caught;
        try
        {
            throw InvalidScheduleDocumentException.ForUnknownDayName(characters);
        }
        catch (ScheduleBuildException error)
        {
            caught = error;
        }

        characters[0] = '?';
        var context = Assert.IsType<InvalidScheduleDocumentException.UnknownDayNameException>(caught);
        Assert.Equal(expected: "unknown-day", actual: context.DayName);
        Assert.Equal(expected: "Unknown day name: `unknown-day`", actual: context.Message);
    }
}
