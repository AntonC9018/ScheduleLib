namespace Comisia.Tests;

public sealed class ThesisNameTests
{
    [Fact]
    public void NewLineParsedOk()
    {
        var r = ThesisListParser.ParseThesisNames("Hello\nПривет");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Привет", r.Ru.Span);
    }

    [Fact]
    public void CarriageReturnNewLineParsedOk()
    {
        var r = ThesisListParser.ParseThesisNames("Hello\r\nПривет");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Привет", r.Ru.Span);
    }

    [Fact]
    public void RepeatedNewLines_DontAffectTheResult()
    {
        var r = ThesisListParser.ParseThesisNames("Hello\n\r\n\nПривет");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Привет", r.Ru.Span);
    }

    [Fact]
    public void WhitespaceNotCounted()
    {
        var r = ThesisListParser.ParseThesisNames("  Hello  \n  Привет ");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Привет", r.Ru.Span);
    }

    [Fact]
    public void NoRussian_AllConsideredRo()
    {
        var r = ThesisListParser.ParseThesisNames("Hello\nRussian?");
        Assert.Equal("Hello\nRussian?", r.Ro.Span);
        Assert.Equal("", r.Ru.Span);
    }

    [Theory]
    [InlineData('\\')]
    [InlineData('/')]
    [InlineData('.')]
    public void TestingAllSeparators(char sep)
    {
        var r = ThesisListParser.ParseThesisNames($"Hello{sep}Рус");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Рус", r.Ru.Span);
    }

    [Fact]
    public void ParenthesisFormat_NoRussian_InterpretedAsRo()
    {
        var r = ThesisListParser.ParseThesisNames("Hello (Russian?)");
        Assert.Equal("Hello (Russian?)", r.Ro.Span);
    }

    [Fact]
    public void NestedParentheses_Russian_InterpretedAsRu()
    {
        var r = ThesisListParser.ParseThesisNames("Hello (Russian? (рус))");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("Russian? (рус)", r.Ru.Span);
    }

    [Fact]
    public void RussianTextInParentheses_InterpretedAsRu()
    {
        var r = ThesisListParser.ParseThesisNames("Hello (русский текст)");
        Assert.Equal("Hello", r.Ro.Span);
        Assert.Equal("русский текст", r.Ru.Span);
    }

    [Fact]
    public void RussianTextInParentheses_AndNotRussianTextInParentheses_InterpretedOk()
    {
        var r = ThesisListParser.ParseThesisNames("Hello (explicatie) (Привет (explicatie))");
        Assert.Equal("Hello (explicatie)", r.Ro.Span);
        Assert.Equal("Привет (explicatie)", r.Ru.Span);
    }

    [Fact]
    public void ExplicitLanguage()
    {
        var r = ThesisListParser.ParseThesisNames("ro: Hello (explicatie) ru: Привет (explicatie)");
        Assert.Equal("Hello (explicatie)", r.Ro.Span);
        Assert.Equal("Привет (explicatie)", r.Ru.Span);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData(@"\")]
    [InlineData("/")]
    [InlineData(".")]
    public void ExplicitLanguage_Separated(string sep)
    {
        var r = ThesisListParser.ParseThesisNames($"ro: Hello (explicatie){sep}ru: Привет (explicatie)");
        Assert.Equal("Hello (explicatie)", r.Ro.Span);
        Assert.Equal("Привет (explicatie)", r.Ru.Span);
    }

    [Fact]
    public void ExplicitLanguage_Separated_DifferentOrderOfLanguage()
    {
        var r = ThesisListParser.ParseThesisNames("ru: Привет (explicatie)/ro: Hello (explicatie)");
        Assert.Equal("Hello (explicatie)", r.Ro.Span);
        Assert.Equal("Привет (explicatie)", r.Ru.Span);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData(".")]
    [InlineData("\r\n")]
    [InlineData(@"\")]
    [InlineData("/")]
    public void InvertedLanguageOrder_Separators(string sep)
    {
        var r = ThesisListParser.ParseThesisNames($"Привет{sep}Hello");
        Assert.Equal("Привет", r.Ru.Span);
        Assert.Equal("Hello", r.Ro.Span);
    }

    // Even though russian translation in parens is ok, romanian is not.
    [Fact]
    public void RoInParens_NotAllowed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ThesisListParser.ParseThesisNames($"Привет (Hello)"));
    }
}
