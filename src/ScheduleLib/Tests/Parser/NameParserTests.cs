using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class NameParserTests
{
    private Name ParseName(string s)
    {
        var p = new SequenceReader(s);
        var r = NameHelper.Parse(ref p);
        Assert.True(p.IsEmpty);
        return r;
    }

    [Fact]
    public void RegularName_AllComponents()
    {
        var s = ParseName("Last First Patro");
        Assert.Equal(expected: "First", actual: s.FirstName[0]);
        Assert.Equal(expected: "Last", actual: s.LastName[0]);
        Assert.Equal(expected: "Patro", actual: s.Patronymic[0]);
    }

    [Fact]
    public void Name_AllComponentsAreDoubled()
    {
        var s = ParseName("LastA-LastB FirstA-FirstB PatroA-PatroB");
        Assert.Equal(expected: "FirstA", actual: s.FirstName[0]);
        Assert.Equal(expected: "FirstB", actual: s.FirstName[1]);
        Assert.Equal(expected: "LastA", actual: s.LastName[0]);
        Assert.Equal(expected: "LastB", actual: s.LastName[1]);
        Assert.Equal(expected: "PatroA", actual: s.Patronymic[0]);
        Assert.Equal(expected: "PatroB", actual: s.Patronymic[1]);
    }

    [Fact]
    public void RandomParensIgnored()
    {
        var s = ParseName("LastA (Also C) FirstA (Also B) Patro");
        Assert.Equal(expected: "FirstA", actual: s.FirstName[0]);
        Assert.Equal(expected: "LastA", actual: s.LastName[0]);
        Assert.Equal(expected: "Patro", actual: s.Patronymic[0]);
    }

    [Fact]
    public void PatroMightBeMissing()
    {
        var s = ParseName("Last First");
        Assert.Equal(expected: "First", actual: s.FirstName[0]);
        Assert.Equal(expected: "Last", actual: s.LastName[0]);
        Assert.Null(s.Patronymic[0]);
    }

    [Fact]
    public void FirstMustNotBeMissing()
    {
        Assert.Throws<NameParsingException>(() => ParseName("Last"));
    }

    [Fact]
    public void StuffAfterIgnored()
    {
        var p = new SequenceReader("Last First Patro (ABC) Extra Stuff");
        var s = NameHelper.Parse(ref p);
        _ = s;
        bool condition = p.ConsumeExactString(" (ABC) Extra Stuff");
        Assert.True(condition);
    }

    [Fact]
    public void ToStringIsCorrect()
    {
        var s = ParseName("A B C");
        Assert.Equal(expected: "A B", actual: s.ToString());
    }
}
