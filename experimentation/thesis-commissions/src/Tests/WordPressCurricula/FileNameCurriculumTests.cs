using ScheduleLib.Parsing;

namespace WordPressCurricula.Tests;

public sealed class FileNameCurriculumTests
{
    [Fact]
    public void Normal()
    {
        const string s = "0123 Hello";
        var c = Helper.ParseCurriculum(s);
        Assert.Equal("0123", c.Code.Value);
        Assert.Equal("Hello", c.Name);
    }

    [Fact]
    public void NormalWithUnderscores()
    {
        const string s = "0123_22 Hello";
        var c = Helper.ParseCurriculum(s);
        Assert.Equal("0123.22", c.Code.Value);
        Assert.Equal("Hello", c.Name);
    }

    [Fact]
    public void UnderscoresInsteadOfSpaces()
    {
        const string s = "0123_22_Hello";
        var c = Helper.ParseCurriculum(s);
        Assert.Equal("0123.22", c.Code.Value);
        Assert.Equal("Hello", c.Name);
    }

    [Fact]
    public void UnderscoresRemovedFromName()
    {
        const string s = "0123_22_Hello_World";
        var c = Helper.ParseCurriculum(s);
        Assert.Equal("0123.22", c.Code.Value);
        Assert.Equal("Hello World", c.Name);
    }

    [Fact]
    public void MultipleSegmentsSupported()
    {
        const string s = "0123.22.123 Hello World";
        var c = Helper.ParseCurriculum(s);
        Assert.Equal("0123.22.123", c.Code.Value);
        Assert.Equal("Hello World", c.Name);
    }

    [Fact]
    public void NoCode()
    {
        const string s = "Hello_World";
        var c = Helper.ParseCurriculum(s);
        Assert.Null(c.Code.Value);
        Assert.Equal("Hello World", c.Name);
    }

    [Fact]
    public void EmptySegmentsNotAllowed()
    {
        const string s = "0123..123 Hello World";
        Assert.Throws<InvalidOperationException>(() => Helper.ParseCurriculum(s));
    }

    [Fact]
    public void SeparatorInFrontNotAllowed()
    {
        const string s = ".0123.123 Hello World";
        Assert.Throws<InvalidOperationException>(() => Helper.ParseCurriculum(s));
    }
}
