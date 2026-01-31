using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests.OnlineRegistry;

public class RegistryScrapingTests
{
    private static GroupForSearch Parse(string input)
    {
        var context = GroupParseContext.Create(new()
        {
            CurrentStudyYear = 2025,
        });
        return RegistryScraping.ParseGroupFromOnlineRegistry(context, input);
    }

    [Fact]
    public void IA253Rfr_ParsesCorrectly()
    {
        var result = Parse("IA253Rfr");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Equal(3, result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(AttendanceMode.FrecventaRedusa, result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(QualificationType.Licenta, result.QualificationType);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void IA253R_a_fr_ParsesCorrectly()
    {
        var result = Parse("IA253R(a)fr");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Equal(3, result.GroupNumber);
        Assert.Equal("a", result.SubGroupName.Span);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(AttendanceMode.FrecventaRedusa, result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void IA253R_b_fr_ParsesCorrectly()
    {
        var result = Parse("IA253R(b)fr");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Equal(3, result.GroupNumber);
        Assert.Equal("b", result.SubGroupName.Span);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(AttendanceMode.FrecventaRedusa, result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void RepetareIA253Rfr_ParsesCorrectly()
    {
        var result = Parse("Repetare - IA253Rfr Iatisina.Tamara HTML si CSS");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Equal(3, result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(AttendanceMode.FrecventaRedusa, result.AttendanceMode);
        Assert.True(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void DJ2502ru_ParsesCorrectly()
    {
        var result = Parse("DJ2502ru");

        Assert.Equal("DJ", result.FacultyName.Span);
        Assert.Equal(2, result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(AttendanceMode.Zi, result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void DJ2502_I_ParsesCorrectly()
    {
        var result = Parse("DJ2502(I)");

        Assert.Equal("DJ", result.FacultyName.Span);
        Assert.Equal(2, result.GroupNumber);
        Assert.Equal("I", result.SubGroupName.Span);
        Assert.Null(result.Language);
        Assert.Equal(AttendanceMode.Zi, result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(1, result.Grade.Value);
    }

    [Fact]
    public void WildcardFormat_ParsesCorrectly()
    {
        var result = Parse("IA24(GA2D)ru");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Null(result.GroupNumber);
        Assert.Equal("GA2D", result.SubGroupName.Span);
        Assert.Equal(Language.Ru, result.Language);
        Assert.True(result.IsWildcard);
        Assert.False(result.IsRepeat);
        Assert.Equal(2, result.Grade.Value);
    }

    [Fact]
    public void WithSE_IgnoresSE()
    {
        var result = Parse("IA2501SE");

        Assert.Equal("IA", result.FacultyName.Span);
        Assert.Equal(1, result.GroupNumber);
        Assert.Null(result.Language);
        Assert.Equal(1, result.Grade.Value);
    }
}
