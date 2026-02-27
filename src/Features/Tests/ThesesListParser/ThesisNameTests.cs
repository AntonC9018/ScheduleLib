using ScheduleLib.Theses.Parsing;

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
    [InlineData("\\")]
    [InlineData("/")]
    [InlineData(". ")]
    public void TestingAllSeparators(string sep)
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
    [InlineData(". ")]
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

    [Fact]
    public void Dot_OnlyCountsAsSeparator_WhenFollowedByWhitespace()
    {
        var r = ThesisListParser.ParseThesisNames("ASP.NET aplicatie. ASP.NET приложение");
        Assert.Equal("ASP.NET aplicatie", r.Ro.Span);
        Assert.Equal("ASP.NET приложение", r.Ru.Span);
    }

    [Fact]
    public void QuotesTrimmed()
    {
        var t = @"""Звуковая стилизация: как звуковой дизайн формирует узнаваемость игрового мира""\„Stilizarea sunetului: modul în care designul sunetului modelează recunoașterea lumii jocului";
        var r = ThesisListParser.ParseThesisNames(t);
        Assert.Equal("Звуковая стилизация: как звуковой дизайн формирует узнаваемость игрового мира", r.Ru.Span);
        Assert.Equal("Stilizarea sunetului: modul în care designul sunetului modelează recunoașterea lumii jocului", r.Ro.Span);
    }

    [Fact]
    public void NoRussianSymbolsHere()
    {
        var t = "Dezvoltarea aplicației WEB cu baza de date “Cartela medicală a pacientului” in mediul de program ASP.NET – DOT.NET";
        var r = ThesisListParser.ParseThesisNames(t);
        Assert.Equal("Dezvoltarea aplicației WEB cu baza de date “Cartela medicală a pacientului” in mediul de program ASP.NET – DOT.NET", r.Ro.Span);
    }

    [Fact(Skip = "English support is hard")]
    public void Bug()
    {
        var t = "Rețelele de calculatoare într-o companie de elaborare a jocurilor (game company). / Computer networks in a game company. / Компьютерные сети в игровой компании. ";
        var r = ThesisListParser.ParseThesisNames(t);
        Assert.Equal("Rețelele de calculatoare într-o companie de elaborare a jocurilor (game company)", r.Ro.Span);
        Assert.Equal("Computer networks in a game company", r.Ru.Span);
    }
}
