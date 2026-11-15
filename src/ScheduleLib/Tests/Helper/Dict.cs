namespace ScheduleLib.Tests;

public sealed class ComparerTests
{
    [Fact]
    public void StringComparer()
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        set.Add("Abc");
        bool condition = set.Add("abc");
        Assert.False(condition);
    }

    [Fact]
    public void DiacriticsComparer()
    {
        var set = new HashSet<string>(IgnoreDiacriticsAndCaseComparer.Instance);
        set.Add("A.Șchiopu");
        bool condition = set.Add("A.Schiopu");
        Assert.False(condition);
    }
}
