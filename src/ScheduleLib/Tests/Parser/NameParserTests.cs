using ScheduleLib.Parsing;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class NameParserTests
{
    private Name ParseName(string s)
    {
        var p = new Parser(s);
        var r = NameHelper.Parse(ref p);
        Assert.True(p.IsEmpty);
        return r;
    }

    [Fact]
    public void RegularName_AllComponents()
    {
        var s = ParseName("Last First Patro");
        Assert.Equal("First", s.FirstName[0]);
        Assert.Equal("Last", s.LastName[0]);
        Assert.Equal("Patro", s.Patronymic[0]);
    }

    [Fact]
    public void Name_AllComponentsAreDoubled()
    {
        var s = ParseName("LastA-LastB FirstA-FirstB PatroA-PatroB");
        Assert.Equal("FirstA", s.FirstName[0]);
        Assert.Equal("FirstB", s.FirstName[1]);
        Assert.Equal("LastA", s.LastName[0]);
        Assert.Equal("LastB", s.LastName[1]);
        Assert.Equal("PatroA", s.Patronymic[0]);
        Assert.Equal("PatroB", s.Patronymic[1]);
    }

    [Fact]
    public void RandomParensIgnored()
    {
        var s = ParseName("LastA (Also C) FirstA (Also B) Patro");
        Assert.Equal("FirstA", s.FirstName[0]);
        Assert.Equal("LastA", s.LastName[0]);
        Assert.Equal("Patro", s.Patronymic[0]);
    }

    [Fact]
    public void PatroMightBeMissing()
    {
        var s = ParseName("Last First");
        Assert.Equal("First", s.FirstName[0]);
        Assert.Equal("Last", s.LastName[0]);
        Assert.Null(s.Patronymic[0]);
    }

    [Fact]
    public void FirstMustNotBeMissing()
    {
        Assert.Throws<InvalidOperationException>(() => ParseName("Last"));
    }

    [Fact]
    public void StuffAfterIgnored()
    {
        var p = new Helper.Parsing.Parser("Last First Patro (ABC) Extra Stuff");
        var s = NameHelper.Parse(ref p);
        _ = s;
        Assert.True(p.ConsumeExactString(" (ABC) Extra Stuff"));
    }

    [Fact]
    public void ToStringIsCorrect()
    {
        var s = ParseName("A B C");
        Assert.Equal("A B", s.ToString());
    }
}
