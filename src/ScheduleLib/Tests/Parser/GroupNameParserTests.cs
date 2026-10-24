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
        Assert.Equal(expected: faculty, actual: group.Faculty.Name);
        Assert.Equal(expected: mode, actual: group.AttendanceMode);
        Assert.Equal(expected: language, actual: group.Language);
        Assert.Equal(expected: 1, actual: group.Grade.Value);
        Assert.Equal(expected: 1, actual: group.GroupNumber);
        Assert.StartsWith(expectedStartString: "DU-", actualString: group.Name);
    }

    [Fact]
    public void LegacyDualCodeNeedsAnExplicitProgramAlias()
    {
        var builder = new ScheduleBuilder { GroupParseContext = _context };
        var unmapped = builder.ParseGroup("DU2501(ru)");
        Assert.Equal(expected: "DU", actual: unmapped.Faculty.Name);
        Assert.Equal(expected: AttendanceMode.Zi, actual: unmapped.AttendanceMode);

        Config.ConfigureRemappings(builder.Remappings);
        var mapped = builder.ParseGroup("DU2501(ru)");
        Assert.Equal(expected: "DU-IA2501", actual: mapped.Name);
        Assert.Equal(expected: "IA", actual: mapped.Faculty.Name);
        Assert.Equal(expected: AttendanceMode.DualZi, actual: mapped.AttendanceMode);
        Assert.Equal(expected: Language.Ru, actual: mapped.Language);
        Assert.Equal(expected: "DU", actual: builder.ParseGroup("DU2601").Faculty.Name);
    }

    [Fact]
    public void AliasAndCanonicalCodeResolveToOneGroup()
    {
        var builder = new ScheduleBuilder { GroupParseContext = _context };
        builder.EnableLookupModule();
        Config.ConfigureRemappings(builder.Remappings);
        var alias = builder.Group("DU2501(ro)");
        var canonical = builder.Group("DU-IA2501(ro)");
        Assert.Equal(expected: alias.Id, actual: canonical.Id);
        Assert.Equal(expected: 1, actual: builder.Groups.Count);
    }

    [Fact]
    public void RegularWithSpace()
    {
        var res = Parse("PMS 2401");
        Assert.Equal(expected: 1, actual: res.Grade.Value);
        Assert.Equal(expected: Language.Ro, actual: res.Language);
        Assert.Equal(expected: "PMS2401", actual: res.Name);
        Assert.Equal(expected: "PMS", actual: res.Faculty.Name);

        // The fact that it's not actually licenta
        // must be specified from some other context.
        // It's impossible to guess from the string alone.
        Assert.True(res.QualificationType == QualificationType.Licenta);
    }

    [Fact]
    public void MasterWithLanguageWithSpace()
    {
        var res = Parse("MIA 2402 (ru)");
        Assert.Equal(expected: 1, actual: res.Grade.Value);
        Assert.Equal(expected: Language.Ru, actual: res.Language);
        Assert.Equal(expected: "MIA2402", actual: res.Name);
        Assert.Equal(expected: "IA", actual: res.Faculty.Name);
        Assert.True(res.QualificationType == QualificationType.Master);
    }

    [Fact]
    public void RegularNoSpace()
    {
        var res = Parse("DJ2401");
        Assert.Equal(expected: 1, actual: res.Grade.Value);
        Assert.Equal(expected: Language.Ro, actual: res.Language);
        Assert.Equal(expected: "DJ2401", actual: res.Name);
        Assert.True(res.QualificationType == QualificationType.Licenta);
    }

    [Fact]
    // ReSharper disable once InconsistentNaming
    public void AttendanceModeFR()
    {
        var res = Parse("DJFR2401");
        Assert.Equal(expected: 1, actual: res.Grade.Value);
        Assert.Equal(expected: Language.Ro, actual: res.Language);
        Assert.Equal(expected: "DJFR2401", actual: res.Name);
        Assert.Equal(expected: "DJ", actual: res.Faculty.Name);
        Assert.Equal(expected: QualificationType.Licenta, actual: res.QualificationType);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: res.AttendanceMode);
    }
}
