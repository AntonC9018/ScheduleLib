using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Builders;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class GroupNameParserTests
{
    private readonly GroupParseContext _context = GroupParseContext.Create(new()
    {
        CurrentStudyYear = new(2024),
    });

    private Group Parse(string s) => _context.Parse(s.AsMemory());

    [Theory]
    [InlineData("DU-IA2401(ro)", "IA", AttendanceMode.DualZi, Language.Ro)]
    [InlineData("DU-DJ2401(ru)", "DJ", AttendanceMode.DualZi, Language.Ru)]
    [InlineData("DU-IAFR2401(ru)", "IA", AttendanceMode.DualFr, Language.Ru)]
    public void DualPrefixPreservesProgramAndAttendanceForm(string name, string faculty, AttendanceMode mode, Language language)
    {
        var group = Parse(name);
        Assert.Equal(faculty, group.Faculty.Name);
        Assert.Equal(mode, group.AttendanceMode);
        Assert.Equal(language, group.Language);
        Assert.Equal(1, group.Grade.Value);
        Assert.Equal(1, group.GroupNumber);
        Assert.StartsWith("DU-", group.Name);
    }

    [Fact]
    public void LegacyDualCodeNeedsAnExplicitProgramAlias()
    {
        var builder = new ScheduleBuilder { GroupParseContext = _context };
        var unmapped = builder.ParseGroup("DU2501(ru)");
        Assert.Equal("DU", unmapped.Faculty.Name);
        Assert.Equal(AttendanceMode.Zi, unmapped.AttendanceMode);

        Config.ConfigureRemappings(builder.Remappings);
        var mapped = builder.ParseGroup("DU2501(ru)");
        Assert.Equal("DU-IA2501", mapped.Name);
        Assert.Equal("IA", mapped.Faculty.Name);
        Assert.Equal(AttendanceMode.DualZi, mapped.AttendanceMode);
        Assert.Equal(Language.Ru, mapped.Language);
        Assert.Equal("DU", builder.ParseGroup("DU2601").Faculty.Name);
    }

    [Fact]
    public void AliasAndCanonicalCodeResolveToOneGroup()
    {
        var builder = new ScheduleBuilder { GroupParseContext = _context };
        builder.EnableLookupModule();
        Config.ConfigureRemappings(builder.Remappings);
        var alias = builder.Group("DU2501(ro)");
        var canonical = builder.Group("DU-IA2501(ro)");
        Assert.Equal(alias.Id, canonical.Id);
        Assert.Equal(1, builder.Groups.Count);
    }

    [Fact]
    public void RegularWithSpace()
    {
        var res = Parse("PMS 2401");
        Assert.Equal(1, res.Grade.Value);
        Assert.Equal(Language.Ro, res.Language);
        Assert.Equal("PMS2401", res.Name);
        Assert.Equal("PMS", res.Faculty.Name);

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
        Assert.Equal("IA", res.Faculty.Name);
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

    [Fact]
    // ReSharper disable once InconsistentNaming
    public void AttendanceModeFR()
    {
        var res = Parse("DJFR2401");
        Assert.Equal(1, res.Grade.Value);
        Assert.Equal(Language.Ro, res.Language);
        Assert.Equal("DJFR2401", res.Name);
        Assert.Equal("DJ", res.Faculty.Name);
        Assert.Equal(QualificationType.Licenta, res.QualificationType);
        Assert.Equal(AttendanceMode.FrecventaRedusa, res.AttendanceMode);
    }
}
