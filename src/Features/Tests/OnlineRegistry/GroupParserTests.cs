using System.Collections.ObjectModel;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.ScheduleDefaults;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryScrapingTests
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

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Equal(expected: 3, actual: result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: QualificationType.Licenta, actual: result.QualificationType);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Fact]
    public void IA253R_a_fr_ParsesCorrectly()
    {
        var result = Parse("IA253R(a)fr");

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Equal(expected: 3, actual: result.GroupNumber);
        Assert.Equal(expected: "a", actual: result.SubGroupName.Span);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Fact]
    public void IA253R_b_fr_ParsesCorrectly()
    {
        var result = Parse("IA253R(b)fr");

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Equal(expected: 3, actual: result.GroupNumber);
        Assert.Equal(expected: "b", actual: result.SubGroupName.Span);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Fact]
    public void RepetareIA253Rfr_ParsesCorrectly()
    {
        var result = Parse("Repetare - IA253Rfr Iatisina.Tamara HTML si CSS");

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Equal(expected: 3, actual: result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.True(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Fact]
    public void DJ2502ru_ParsesCorrectly()
    {
        var result = Parse("DJ2502ru");

        Assert.Equal(expected: "DJ", actual: result.FacultyName.Span);
        Assert.Equal(expected: 2, actual: result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: AttendanceMode.Zi, actual: result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Fact]
    public void DJ2502_I_ParsesCorrectly()
    {
        var result = Parse("DJ2502(I)");

        Assert.Equal(expected: "DJ", actual: result.FacultyName.Span);
        Assert.Equal(expected: 2, actual: result.GroupNumber);
        Assert.Equal(expected: "I", actual: result.SubGroupName.Span);
        Assert.Null(result.Language);
        Assert.Equal(expected: AttendanceMode.Zi, actual: result.AttendanceMode);
        Assert.False(result.IsRepeat);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
    }

    [Theory]
    [InlineData("DJ2502(I)ru", "I")]
    [InlineData("DJ2502(II)ru", "II")]
    public void LanguageAfterNumericSubgroupUsesRequestedAcademicYear(string name, string subgroup)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        Assert.Equal(expected: "DJ", actual: result.FacultyName.Span);
        Assert.Equal(expected: 2, actual: result.GroupNumber);
        Assert.Equal(expected: subgroup, actual: result.SubGroupName.ToString());
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: 2, actual: result.Grade.Value);
    }

    [Fact]
    public void DuplicateLanguageAroundNumericSubgroupIsRejected()
    {
        Assert.Throws<GroupParsingException>(() => Parse("DJ2502ru(I)ru"));
    }

    [Fact]
    public void EducationSciencesMarkerIsRetainedAsAdditionalCourses()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "I2402SEru");
        Assert.Equal(expected: "I", actual: result.FacultyName.Span);
        Assert.Equal(expected: 2, actual: result.GroupNumber);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: 3, actual: result.Grade.Value);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.True(result.IsAdditionalCourses);
    }

    [Theory]
    [InlineData("DJ2501(en)", null)]
    [InlineData("DJ2501ru(en)", Language.Ru)]
    public void EnglishLabPartitionPreservesParentGroupLanguage(string name, Language? language)
    {
        var result = Parse(name);
        Assert.Equal(expected: 1, actual: result.GroupNumber);
        Assert.Equal(expected: SpecialSubGroups.Eng.Value, actual: result.SubGroupName.ToString());
        Assert.Equal(expected: language, actual: result.Language);
        Assert.False(result.IsWildcard);
        Assert.False(result.IsAdditionalCourses);
        var partition = new GroupPartitionResolver(null).Resolve(result);
        Assert.Equal(expected: SpecialSubGroups.Eng, actual: partition.SubGroup);
    }

    [Theory]
    [InlineData("IA2501", false)]
    [InlineData("IA2501SE", true)]
    [InlineData("I2402SEru", true)]
    [InlineData("I2302SEr", true)]
    [InlineData("I2302se(II)", true)]
    [InlineData("DJ2502(I)SE", true)]
    [InlineData("DJ2502SE(I)", true)]
    public void AdditionalCoursesMarkerIsRetainedAcrossPositions(string name, bool expected)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        Assert.Equal(expected: expected, actual: result.IsAdditionalCourses);
    }

    [Fact]
    public void AdditionalCoursesMarkerDoesNotChangeScheduledPartition()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var ordinary = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA2601(I)");
        var additional = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA2601SE(I)");
        Assert.False(ordinary.IsAdditionalCourses);
        Assert.True(additional.IsAdditionalCourses);
        var resolver = new GroupPartitionResolver(null);
        Assert.Equal(expected: resolver.Resolve(ordinary), actual: resolver.Resolve(additional));
        Assert.Equal(expected: new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All), actual: resolver.Resolve(additional));
    }

    [Fact]
    public void WildcardFormat_ParsesCorrectly()
    {
        var result = Parse("IA24(GA2D)ru");

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Null(result.GroupNumber);
        Assert.Equal(expected: "GA2D", actual: result.SubGroupName.Span);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.True(result.IsWildcard);
        Assert.False(result.IsRepeat);
        Assert.Equal(expected: 2, actual: result.Grade.Value);
    }

    [Fact]
    public void WithSE_RetainsAdditionalCoursesMarker()
    {
        var result = Parse("IA2501SE");

        Assert.Equal(expected: "IA", actual: result.FacultyName.Span);
        Assert.Equal(expected: 1, actual: result.GroupNumber);
        Assert.Null(result.Language);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
        Assert.True(result.IsAdditionalCourses);
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
        Assert.Equal(expected: faculty, actual: result.FacultyName.ToString());
        Assert.Equal(expected: number, actual: result.GroupNumber);
        Assert.Equal(expected: language, actual: result.Language);
        Assert.Equal(expected: grade, actual: result.Grade.Value);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.Equal(expected: master ? QualificationType.Master : QualificationType.Licenta, actual: result.QualificationType);
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
        Assert.Equal(expected: language, actual: result.Language);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(expected: GroupPartitionKey.All, actual: new GroupPartitionResolver(null).Resolve(result));
    }

    [Fact]
    public void DualSuffixSetsBothAttendanceAndDualMatchingFlag()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA2601DU");
        Assert.Equal(expected: "IA", actual: result.FacultyName.ToString());
        Assert.Equal(expected: 1, actual: result.GroupNumber);
        Assert.Equal(expected: 1, actual: result.Grade.Value);
        Assert.Equal(expected: AttendanceMode.DualZi, actual: result.AttendanceMode);
        Assert.True(result.IsDual);
        Assert.False(result.IsWildcard);
        Assert.True(result.SubGroupName.IsEmpty);
    }

    [Theory]
    [InlineData("IA_2001fr", 1, null)]
    [InlineData("IA_2002Rfr", 2, Language.Ru)]
    public void HistoricalFacultyYearSeparatorPreservesAttendanceAndLanguage(string name, int number, Language? language)
    {
        var result = Parse(name);
        Assert.Equal(expected: "IA", actual: result.FacultyName.ToString());
        Assert.Equal(expected: number, actual: result.GroupNumber);
        Assert.Equal(expected: AttendanceMode.FrecventaRedusa, actual: result.AttendanceMode);
        Assert.Equal(expected: language, actual: result.Language);
    }

    [Fact]
    public void ParenthesizedDualAfterNumberSelectsAttendanceInsteadOfPartition()
    {
        var result = Parse("IA2501(Dual)");
        Assert.Equal(expected: 1, actual: result.GroupNumber);
        Assert.Equal(expected: AttendanceMode.DualZi, actual: result.AttendanceMode);
        Assert.True(result.IsDual);
        Assert.False(result.IsWildcard);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.Equal(expected: GroupPartitionKey.All, actual: new GroupPartitionResolver(null).Resolve(result));
    }

    [Theory]
    [InlineData("IA2603ru(ru)")]
    [InlineData("IA2603ro(ru)")]
    [InlineData("IA2603(ru)ro")]
    [InlineData("IAFR2603fr")]
    [InlineData("IA2603frfr")]
    [InlineData("IA2603fr(I)fr")]
    [InlineData("IA2603DUDU")]
    [InlineData("IA26(Dual)DU")]
    [InlineData("IA2501(Dual)DU")]
    [InlineData("IA2501DU(Dual)")]
    public void DuplicateMarkersAreRejected(string name)
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
        Assert.Equal(expected: 2, actual: result.Grade.Value);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: new GroupPartitionKey(SubGroup.All, specialization), actual: new GroupPartitionResolver(null).Resolve(result));
    }


    [Fact]
    public void HistoricalWildcardPreservesSpecializationAndAcademicYear()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var result = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA25(GA2D)ru");
        Assert.Equal(expected: "IA", actual: result.FacultyName.ToString());
        Assert.Null(result.GroupNumber);
        Assert.True(result.IsWildcard);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
        Assert.Equal(expected: 2, actual: result.Grade.Value);
        Assert.Equal(expected: new GroupPartitionKey(SubGroup.All, Specialization.GA2D), actual: new GroupPartitionResolver(null).Resolve(result));
    }

    [Fact]
    public void ExistingDualWildcardSetsConsistentMatchingFields()
    {
        var result = Parse("IA25(Dual)ru");
        Assert.Equal(expected: AttendanceMode.DualZi, actual: result.AttendanceMode);
        Assert.True(result.IsDual);
        Assert.Null(result.GroupNumber);
        Assert.True(result.SubGroupName.IsEmpty);
        Assert.False(result.IsWildcard);
        Assert.Equal(expected: Language.Ru, actual: result.Language);
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
        Assert.Equal(expected: "MIA", actual: result.FacultyName.ToString());
        Assert.Equal(expected: QualificationType.Master, actual: result.QualificationType);
        Assert.Equal(expected: 1, actual: result.GroupNumber);
        Assert.Equal(expected: 2, actual: result.Grade.Value);
        Assert.Equal(expected: AttendanceMode.Zi, actual: result.AttendanceMode);
    }

}
