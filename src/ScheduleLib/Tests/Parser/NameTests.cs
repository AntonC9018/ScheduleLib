using ScheduleLib.Parsing;

namespace ScheduleLib.ParserTests;

public sealed class NameTests
{
    [Fact]
    public void EqualityComparer_IgnoresCase()
    {
        var a = new Name
        {
            FirstName = N("Anton"),
            LastName = N("Curmanschii"),
        };
        var b = new Name
        {
            FirstName = N("ANTON"),
            LastName = N("CURMANSCHII"),
        };
        Assert.Equal(expected: a, actual: b, Comparer);
        Assert.Equal(expected: Comparer.GetHashCode(a), actual: Comparer.GetHashCode(b));
    }

    [Fact]
    public void EqualityComparer_AllowsNoPatronymic()
    {
        var a = new Name
        {
            FirstName = N("Anton"),
            LastName = N("Curmanschii"),
        };
        var b = new Name
        {
            FirstName = N("ANTON"),
            LastName = N("Curmanschii"),
            Patronymic = N("Hello"),
        };
        Assert.Equal(expected: a, actual: b, Comparer);
        Assert.Equal(expected: b, actual: a, Comparer);
        Assert.Equal(expected: Comparer.GetHashCode(a), actual: Comparer.GetHashCode(b));
    }

    [Fact]
    public void EqualityComparer_Diacritics()
    {
        var a = new Name
        {
            FirstName = N("STEPVEȚ"),
            LastName = N("DIMA"),
            Patronymic = N("ALEX"),
        };
        var b = new Name
        {
            FirstName = N("Stepvet"),
            LastName = N("dima"),
        };
        Assert.Equal(expected: a, actual: b, Comparer);
        Assert.Equal(expected: b, actual: a, Comparer);
        Assert.Equal(expected: Comparer.GetHashCode(a), actual: Comparer.GetHashCode(b));
    }

    [Fact]
    public void EqualityComparer_WithPatronymic()
    {
        var a = new Name
        {
            FirstName = N("Anton"),
            LastName = N("Curmanschii"),
            Patronymic = N("HEllO"),
        };
        var b = new Name
        {
            FirstName = N("ANTON"),
            LastName = N("Curmanschii"),
            Patronymic = N("Hello"),
        };
        Assert.Equal(expected: a, actual: b, Comparer);
        Assert.Equal(expected: b, actual: a, Comparer);
        Assert.Equal(expected: Comparer.GetHashCode(a), actual: Comparer.GetHashCode(b));
    }

    private Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer Comparer => Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance;

    private static NameParts<string?> N(string s)
    {
        var ret = new NameParts<string?>();
        ret[0] = s;
        return ret;
    }
}


