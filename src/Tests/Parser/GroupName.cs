using ScheduleLib;
using ScheduleLib.Parsing.GroupParser;

namespace App.Tests;

public sealed class GroupNameParser
{
    private readonly GroupParseContext _context = GroupParseContext.Create(new()
    {
        CurrentStudyYear = 2024,
    });

    private Group Parse(string s) => _context.Parse(s);

    [Fact]
    public void RegularWithSpace()
    {
        var res = Parse("PMS 2401");
        Assert.Equal(1, res.Grade.Value);
        Assert.Equal(Language.Ro, res.Language);
        Assert.Equal("PMS2401", res.Name);

        // The fact that it's not actually licenta
        // must be specified from some other context.
        // It's impossible to guess from the string alone.
        Assert.True(res.QualificationType == QualificationType.Licenta);
    }

    [Fact]
    public void MasterWithLanguageWithSpace()
    {
        var res = Parse("MIA 2402 (ru)");
        Assert.Equal(1, res.Grade.Value);
        Assert.Equal(Language.Ru, res.Language);
        Assert.Equal("MIA2402", res.Name);
        Assert.True(res.QualificationType == QualificationType.Master);
    }

    [Fact]
    public void RegularNoSpace()
    {
        var res = Parse("DJ2401");
        Assert.Equal(1, res.Grade.Value);
        Assert.Equal(Language.Ro, res.Language);
        Assert.Equal("DJ2401", res.Name);
        Assert.True(res.QualificationType == QualificationType.Licenta);
    }
}
