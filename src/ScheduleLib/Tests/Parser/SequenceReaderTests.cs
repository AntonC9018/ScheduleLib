using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class SequenceReaderTests
{
    [Fact]
    public void PeekSpanMaxSize_AfterMove_ReturnsWindowAtCurrentIndex()
    {
        var reader = new SequenceReader("hello world");
        reader.Move(6);

        Assert.Equal("worl", reader.PeekSpanMaxSize(4).ToString());
    }

    [Fact]
    public void PeekSpanMaxSize_ClampsToRemainingInput()
    {
        var reader = new SequenceReader("hello world");
        reader.Move(6);

        Assert.Equal("world", reader.PeekSpanMaxSize(10).ToString());
    }

    [Fact]
    public void PeekSpanMaxSize_AtEnd_ReturnsEmpty()
    {
        var reader = new SequenceReader("hi");
        reader.Move(2);

        Assert.True(reader.PeekSpanMaxSize(4).IsEmpty);
    }
}
