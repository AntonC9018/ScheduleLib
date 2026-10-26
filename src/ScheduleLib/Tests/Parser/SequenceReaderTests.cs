using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class SequenceReaderTests
{
    [Fact]
    public void PeekSpanMaxSize_AfterMove_ReturnsWindowAtCurrentIndex()
    {
        var reader = new SequenceReader("hello world");
        reader.Move(6);

        Assert.Equal(expected: "worl",
            actual: reader.PeekSpanMaxSize(4)
                .ToString());
    }

    [Fact]
    public void PeekSpanMaxSize_ClampsToRemainingInput()
    {
        var reader = new SequenceReader("hello world");
        reader.Move(6);

        Assert.Equal(expected: "world",
            actual: reader.PeekSpanMaxSize(10)
                .ToString());
    }

    [Fact]
    public void PeekSpanMaxSize_AtEnd_ReturnsEmpty()
    {
        var reader = new SequenceReader("hi");
        reader.Move(2);

        Assert.True(reader.PeekSpanMaxSize(4).IsEmpty);
    }
}
