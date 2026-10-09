using System.Collections.ObjectModel;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.ScheduleDefaults;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests.OnlineRegistry;

public class RegistryScrapingTests
{
    private static GroupForSearch Parse(string input)
    {
        var context = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2025),
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

    [Theory]
    [InlineData("DJ2502(I)ru", "I")]
    [InlineData("DJ2502(II)ru", "II")]
    public void LanguageAfterNumericSubgroupUsesRequestedAcademicYear(string name, string subgroup)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        Assert.Equal("DJ", result.FacultyName.Span);
        Assert.Equal(2, result.GroupNumber);
        Assert.Equal(subgroup, result.SubGroupName.ToString());
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(2, result.Grade.Value);
    }

    [Fact]
    public void DuplicateLanguageAroundNumericSubgroupIsRejected()
    {
        Assert.Throws<GroupParsingException>(() => Parse("DJ2502ru(I)ru"));
    }

    [Fact]
    public void EducationSciencesMarkerIsConsumedWithoutAddingDomainMeaning()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "I2402SEru");
        Assert.Equal("I", result.FacultyName.Span);
        Assert.Equal(2, result.GroupNumber);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(3, result.Grade.Value);
        Assert.True(result.SubGroupName.IsEmpty);
    }

    [Theory]
    [InlineData("DJ2501(en)", null)]
    [InlineData("DJ2501ru(en)", Language.Ru)]
    public void EnglishLabPartitionPreservesParentGroupLanguage(string name, Language? language)
    {
        var result = Parse(name);
        Assert.Equal(1, result.GroupNumber);
        Assert.Equal(SpecialSubGroups.Eng.Value, result.SubGroupName.ToString());
        Assert.Equal(language, result.Language);
        Assert.False(result.IsWildcard);
        var partition = new GroupPartitionResolver(null).Resolve(result);
        Assert.Equal(SpecialSubGroups.Eng, partition.SubGroup);
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

    [Theory]
    [InlineData("IAFR2603(ru)", "IA", 3, Language.Ru, 1, false)]
    [InlineData("IAFR2403ru", "IA", 3, Language.Ru, 3, false)]
    [InlineData("MFR2501", "M", 1, null, 2, true)]
    public void FacultyAttendanceSuffixIsSeparatedBeforeResolvingQualification(
        string name, string faculty, int number, Language? language, int grade, bool master)
    {
        var context = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2026),
            GroupLabelsThatAreMaster = new ReadOnlySet<string>(new HashSet<string> { "M" }),
        });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        Assert.Equal(faculty, result.FacultyName.ToString());
        Assert.Equal(number, result.GroupNumber);
        Assert.Equal(language, result.Language);
        Assert.Equal(grade, result.Grade.Value);
        Assert.Equal(AttendanceMode.FrecventaRedusa, result.AttendanceMode);
        Assert.Equal(master ? QualificationType.Master : QualificationType.Licenta, result.QualificationType);
        Assert.False(result.IsDual);
        Assert.False(result.IsWildcard);
        Assert.True(result.SubGroupName.IsEmpty);
    }

    [Theory]
    [InlineData("IA2603(ru)", Language.Ru)]
    [InlineData("IA2603(ro)", Language.Ro)]
    public void ParenthesizedWholeGroupLanguageDoesNotCreatePartition(string name, Language language)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        Assert.Equal(language, result.Language);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(GroupPartitionKey.All, new GroupPartitionResolver(null).Resolve(result));
    }

    [Fact]
    public void DualSuffixSetsBothAttendanceAndDualMatchingFlag()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA2601DU");
        Assert.Equal("IA", result.FacultyName.ToString());
        Assert.Equal(1, result.GroupNumber);
        Assert.Equal(1, result.Grade.Value);
        Assert.Equal(AttendanceMode.Dual, result.AttendanceMode);
        Assert.True(result.IsDual);
        Assert.False(result.IsWildcard);
        Assert.True(result.SubGroupName.IsEmpty);
    }

    [Theory]
    [InlineData("IA2603ru(ru)")]
    [InlineData("IA2603ro(ru)")]
    [InlineData("IA2603(ru)ro")]
    [InlineData("IAFR2603fr")]
    [InlineData("IA2603frfr")]
    [InlineData("IA2603fr(I)fr")]
    [InlineData("IA2603DUDU")]
    [InlineData("IA2603DUfr")]
    [InlineData("IAFR2603DU")]
    [InlineData("IA2603frDU")]
    [InlineData("IAFR26(Dual)")]
    [InlineData("IA26(Dual)DU")]
    public void DuplicateOrConflictingMarkersAreRejected(string name)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        Assert.Throws<GroupParsingException>(() => RegistryScraping.ParseGroupFromOnlineRegistry(context, name));
    }

    [Theory]
    [InlineData("SAWM")]
    [InlineData("DAS-S")]
    [InlineData("UI/UX")]
    [InlineData("RVA")]
    public void ConfirmedSpecializationAliasesUseConfiguredRemappings(string alias)
    {
        var remappings = new Remappings();
        Config.ConfigureRemappings(remappings);
        var specialization = alias switch
        {
            "UI/UX" => Specialization.UI,
            "RVA" => Specialization.DJ,
            _ => Specialization.DezvoltareaAplicatiilor,
        };
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, $"IA25({alias})ru");
        result.SubGroupName = remappings.SubGroupNameRemappings.Remap(result.SubGroupName).Value!.AsMemory();
        Assert.True(result.IsWildcard);
        Assert.Null(result.GroupNumber);
        Assert.Equal(2, result.Grade.Value);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(new GroupPartitionKey(SubGroup.All, specialization), new GroupPartitionResolver(null).Resolve(result));
    }


    [Fact]
    public void HistoricalWildcardPreservesSpecializationAndAcademicYear()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA25(GA2D)ru");
        Assert.Equal("IA", result.FacultyName.ToString());
        Assert.Null(result.GroupNumber);
        Assert.True(result.IsWildcard);
        Assert.Equal(Language.Ru, result.Language);
        Assert.Equal(2, result.Grade.Value);
        Assert.Equal(new GroupPartitionKey(SubGroup.All, Specialization.GA2D), new GroupPartitionResolver(null).Resolve(result));
    }

    [Fact]
    public void ExistingDualWildcardSetsConsistentMatchingFields()
    {
        var result = Parse("IA25(Dual)ru");
        Assert.Equal(AttendanceMode.Dual, result.AttendanceMode);
        Assert.True(result.IsDual);
        Assert.Null(result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.False(result.IsWildcard);
        Assert.Equal(Language.Ru, result.Language);
    }


    [Fact]
    public void AppliedInformaticsMasterUsesConfiguredQualificationLabels()
    {
        var context = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2026),
            GroupLabelsThatAreMaster = Config.GroupLabelsThatAreMaster,
        });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "MIA2501");
        Assert.Equal("MIA", result.FacultyName.ToString());
        Assert.Equal(QualificationType.Master, result.QualificationType);
        Assert.Equal(1, result.GroupNumber);
        Assert.Equal(2, result.Grade.Value);
        Assert.Equal(AttendanceMode.Zi, result.AttendanceMode);
    }

}
