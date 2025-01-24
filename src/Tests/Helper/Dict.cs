using ScheduleLib;

namespace App.Tests;

public sealed class ComparerTests
{
    [Fact]
    public void StringComparer()
    {
        var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        set.Add("Abc");
        Assert.False(set.Add("abc"));
    }

    [Fact]
    public void DiacriticsComparer()
    {
        var set = new HashSet<string>(IgnoreDiacriticsComparer.Instance);
        set.Add("A.Șchiopu");
        Assert.False(set.Add("A.Schiopu"));
    }
}
