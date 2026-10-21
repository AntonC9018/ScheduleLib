namespace WordPressCurricula.Tests;

public sealed class HtmlCurriculumNameTests
{
    [Fact]
    public void NoCode_NoModifiers()
    {
        const string s = "Nume disciplinei";
        var c = ParseSingleSource(s);
        Assert.Empty(c.Codes);
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Empty(c.AttendanceLinks);
    }

    [Fact]
    public void Code_NoModifiers()
    {
        const string s = "123.456 Nume disciplinei";
        var c = ParseSingleSource(s);
        Assert.Collection(c.Codes, x => Assert.Equal("123.456", x.Value));
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Empty(c.AttendanceLinks);
    }

    [Fact]
    public void MultipleSpacesInName_Ignored()
    {
        const string s = "Nume   disciplinei  ";
        var c = ParseSingleSource(s);
        Assert.Empty(c.Codes);
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Empty(c.AttendanceLinks);
    }

    [Theory]
    [InlineData("123..456 Nume disciplinei")]
    [InlineData("123. Nume disciplinei")]
    [InlineData(".123 Nume disciplinei")]
    [InlineData(". Nume disciplinei")]
    [InlineData("123.123.123 Nume disciplinei")]
    public void InvalidCodeDoesntParse(string str)
    {
        Assert.Throws<InvalidOperationException>(() => _ = ParseSingleSource(str));
    }

    [Fact]
    public void NoCode_SingleAttendanceModifier_Useless()
    {
        const string s = "Nume disciplinei (120 credite)";
        var c = ParseSingleSource(s);
        Assert.Empty(c.Codes);
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Empty(c.AttendanceLinks);
    }

    [Fact]
    public void NoCode_SingleAttendanceModifier_Useful()
    {
        const string s = "Nume disciplinei (studii cu frecvență)";
        var c = ParseSingleSource(s);
        Assert.Empty(c.Codes);
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Collection(c.AttendanceLinks, x =>
        {
            Assert.Equal(AttendanceMode.Zi, x.AttendanceMode);
        });
    }

    [Fact]
    public void NoCode_MultipleAttendanceModifiers()
    {
        const string s = "Nume disciplinei (studii cu frecvență, studii la distanță, studii duale, studii cu frecvență redusă)";
        var c = ParseSingleSource(s);
        Assert.Empty(c.Codes);
        Assert.Equal("Nume disciplinei", c.Name);
        Assert.Collection(c.AttendanceLinks,
            x1 => Assert.Equal(AttendanceMode.Zi, x1.AttendanceMode),
            x2 => Assert.Equal(AttendanceMode.Distance, x2.AttendanceMode),
            x3 => Assert.Equal(AttendanceMode.Dual, x3.AttendanceMode),
            x4 => Assert.Equal(AttendanceMode.FR, x4.AttendanceMode));
    }

    [Fact]
    public void EmptyAttendaceModifierList_NotAllowed()
    {
        const string s = "Nume disciplinei ()";
        Assert.Throws<InvalidOperationException>(() => _ = ParseSingleSource(s));
    }

    [Fact]
    public void InvalidAttendanceModifier_NotAllowed()
    {
        const string s = "Nume disciplinei (едрена вожь)";
        Assert.Throws<InvalidOperationException>(() => _ = ParseSingleSource(s));
    }

    [Fact]
    public void AttendanceModifier_SplicedOverMultipleSegments_Works()
    {
        var s1 = new IdentifiedElement();
        var s2 = new IdentifiedElement();
        var s3 = new IdentifiedElement();
        var s4 = new IdentifiedElement();

        var c = Helper.ParseCurriculumInHtml(new()
        {
            Source = [
                new("Nume disciplinei", s1),
                new(" (studii ", s2),
                new("la", s3),
                new(" distanta)", s4),
            ],
        });
        var x = Assert.Single(c.AttendanceLinks);
        Assert.Collection(x.LinkSequence.HtmlElements,
            x1 => Assert.Same(s2, x1),
            x2 => Assert.Same(s3, x2),
            x3 => Assert.Same(s4, x3));
        Assert.Equal(" (studii".IndexOf('s'), x.LinkSequence.StartIndex);
        Assert.Equal(" distanta)".IndexOf(')'), x.LinkSequence.EndIndexExclusive);
    }


    [Fact]
    public void AttendanceModifier_WhenLastSegmentEmpty_ItsNotIncludedInTheSequence()
    {
        var s1 = new IdentifiedElement();
        var s2 = new IdentifiedElement();
        var s3 = new IdentifiedElement();

        var c = Helper.ParseCurriculumInHtml(new()
        {
            Source = [
                new("Nume disciplinei", s1),
                new(" (studii la distanta", s2),
                new(")", s3),
            ],
        });
        var x = Assert.Single(c.AttendanceLinks);
        Assert.Collection(x.LinkSequence.HtmlElements, x1 => Assert.Same(s2, x1));
        Assert.Equal(x.LinkSequence.EndIndexExclusive, " (studii la distanta".Length);
    }

    private HtmlCurriculum ParseSingleSource(string source)
    {
        var c = Helper.ParseCurriculumInHtml(new()
        {
            Source = [new(source, null!)],
        });
        return c;
    }
}
