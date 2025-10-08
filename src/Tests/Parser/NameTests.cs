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
        Assert.Equal(a, b, Comparer);
        Assert.Equal(Comparer.GetHashCode(a), Comparer.GetHashCode(b));
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
        Assert.Equal(a, b, Comparer);
        Assert.Equal(b, a, Comparer);
        Assert.Equal(Comparer.GetHashCode(a), Comparer.GetHashCode(b));
    }

    private Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer Comparer => Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance;

    private static NameParts<string?> N(string s)
    {
        var ret = new NameParts<string?>();
        ret[0] = s;
        return ret;
    }
}


