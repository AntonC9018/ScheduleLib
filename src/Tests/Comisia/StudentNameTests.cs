using ScheduleLib.Parsing;

namespace Comisia.Tests;

public sealed class StudentNameTests
{
    private StudentName ParseName(string s)
    {
        var p = new Parser(s);
        var r = CommissionParser.ParseStudentName(ref p);
        Assert.True(p.IsEmpty);
        return r;
    }

    [Fact]
    public void RegularName_AllComponents()
    {
        var s = ParseName("Last First Patro");
        Assert.Equal("First", s.FirstName.A);
        Assert.Equal("Last", s.LastName.A);
        Assert.Equal("Patro", s.Patronymic.A);
    }

    [Fact]
    public void Name_AllComponentsAreDoubled()
    {
        var s = ParseName("LastA-LastB FirstA-FirstB PatroA-PatroB");
        Assert.Equal("FirstA", s.FirstName.A);
        Assert.Equal("FirstB", s.FirstName.B);
        Assert.Equal("LastA", s.LastName.A);
        Assert.Equal("LastB", s.LastName.B);
        Assert.Equal("PatroA", s.Patronymic.A);
        Assert.Equal("PatroB", s.Patronymic.B);
    }

    [Fact]
    public void RandomParensIgnored()
    {
        var s = ParseName("LastA (Also C) FirstA (Also B) Patro");
        Assert.Equal("FirstA", s.FirstName.A);
        Assert.Equal("LastA", s.LastName.A);
        Assert.Equal("Patro", s.Patronymic.A);
    }

    [Fact]
    public void PatroMightBeMissing()
    {
        var s = ParseName("Last First");
        Assert.Equal("First", s.FirstName.A);
        Assert.Equal("Last", s.LastName.A);
        Assert.Null(s.Patronymic.A);
    }

    [Fact]
    public void FirstMustNotBeMissing()
    {
        Assert.Throws<InvalidOperationException>(() => ParseName("Last"));
    }

    [Fact]
    public void StuffAfterIgnored()
    {
        var p = new Parser("Last First Patro (ABC) Extra Stuff");
        var s = CommissionParser.ParseStudentName(ref p);
        _ = s;
        Assert.True(p.ConsumeExactString(" (ABC) Extra Stuff"));
    }

    [Fact]
    public void ToStringIsCorrect()
    {
        var s = ParseName("A B C");
        Assert.Equal("A B C", s.ToString());
    }
}
