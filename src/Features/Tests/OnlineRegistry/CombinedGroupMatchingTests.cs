using AngleSharp.Html.Parser;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class CombinedGroupMatchingTests
{
    [Fact]
    public void OrdinarySpecializationWildcardRejectsSeparateParentLabs()
    {
        var fixture = new Fixture();
        fixture.Add("IA2403(ru)");
        fixture.Add("IA2404(ru)");
        fixture.Add("IA2405(ru)", specialization: Specialization.GA2D);
        var schedule = fixture.Build();
        var parsed = Parse("IA24(SAWM)ru");
        var groups = new FoundGroups { IsWildcard = true, Value = [new GroupId(0), new GroupId(1)] };
        var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
            new LessonSearchFilter(fixture.Course, groups, Resolver.Resolve(parsed)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule)).ToArray();
        Assert.Empty(lessons);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void IntakeLabRejectionDoesNotDependOnResolvedParentCount(bool combined, bool sharedLab)
    {
        var fixture = new Fixture();
        fixture.Add("IA2403(ru)");
        fixture.Add("IA2404(ru)");
        if (sharedLab)
            fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.Groups.Add(new GroupId(1));
        var schedule = fixture.Build();
        var groups = new FoundGroups { IsWildcard = true, IsCombined = combined, Value = [new GroupId(0)] };
        var parameters = new LessonMatchParams(
            new LessonSearchFilter(fixture.Course, groups, new(SubGroup.All, Specialization.DezvoltareaAplicatiilor)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule);
        Assert.Empty(MatchLessonHelper.MatchLessonsInSchedule(parameters));
        Assert.Equal(combined && sharedLab ? 0 : 1, MatchLessonHelper.FindUnsafeIntakeLabs(parameters).Count);
    }

    [Fact]
    public void RejectingIntakeLabsDoesNotActivateAnUnrelatedElectiveFallback()
    {
        var fixture = new Fixture();
        fixture.Add("IA2403(ru)");
        fixture.Add("IA2403(ru)", specialization: Specialization.All);
        fixture.Builder.WeeklyLessons.Ref(1).Data.Base.Group.SubGroup = SpecialSubGroups.Optional;
        fixture.Builder.WeeklyLessons.Ref(1).Data.Base.General.Type = LessonType.Curs;
        var schedule = fixture.Build();
        var parameters = new LessonMatchParams(
            new LessonSearchFilter(fixture.Course, new FoundGroups { IsWildcard = true, Value = [new GroupId(0)] },
                new(SubGroup.All, Specialization.DezvoltareaAplicatiilor)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule);
        Assert.Empty(MatchLessonHelper.MatchLessonsInSchedule(parameters));
        Assert.Single(MatchLessonHelper.FindUnsafeIntakeLabs(parameters));
    }

    [Fact]
    public void ConfirmedCombinedLabelPreservesIntakeWildcardAndMissingLanguage()
    {
        var group = Parse("I_IA24(SAWM)");
        Assert.Equal("I_IA", group.FacultyName.ToString());
        Assert.Equal(3, group.Grade.Value);
        Assert.Null(group.GroupNumber);
        Assert.Null(group.Language);
        Assert.True(group.IsWildcard);
        Assert.Equal(new GroupPartitionKey(SubGroup.All, Specialization.DezvoltareaAplicatiilor), Resolver.Resolve(group));
    }

    [Fact]
    public void CombinedWildcardUsesBothFacultiesOnlyForItsCourseAndPartition()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402(ru)");
        fixture.Add("IA2403", otherCourse: true);
        fixture.Add("IA2404", specialization: Specialization.GA2D);
        fixture.Add("IA2501(ru)");
        fixture.Add("DJ2401(ru)");
        fixture.Add("IAFR2405");
        fixture.Add("I2406");
        fixture.Builder.Groups.Ref(7).QualificationType = QualificationType.Master;
        var result = Match(fixture, "I_IA24(SAWM)");
        Assert.Equal(new[] { new GroupId(0), new GroupId(1) }, result.ToArray());
    }

    [Fact]
    public void CombinedLabelCannotResolveWithoutCourseAndPartitionEvidence()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402(ru)");
        Assert.Empty(GroupsNavigator.FindGroupMatch(fixture.Build(), Parse("I_IA24(SAWM)")));
    }

    [Fact]
    public void CombinedMissingLanguageCanResolveRomanianParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401");
        fixture.Add("IA2402");
        Assert.Equal(new[] { new GroupId(0), new GroupId(1) }, Match(fixture, "I_IA24(SAWM)").ToArray());
    }

    [Fact]
    public void CombinedNumberedLabelKeepsItsGroupNumberConstraint()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)", specialization: Specialization.All);
        fixture.Add("IA2402(ru)", specialization: Specialization.All);
        Assert.Equal(new[] { new GroupId(1) }, Match(fixture, "I_IA2402ru").ToArray());
    }

    [Fact]
    public void CombinedMissingLanguageSkipsAmbiguousParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402");
        Assert.Empty(Match(fixture, "I_IA24(SAWM)"));
    }

    [Fact]
    public void CombinedExplicitLanguageSelectsItsParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402");
        Assert.Equal(new[] { new GroupId(0) }, Match(fixture, "I_IA24(SAWM)ru").ToArray());
    }

    [Fact]
    public void CombinedMissingLanguageSkipsWhenOnlyUnrelatedLessonsExist()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)", otherCourse: true);
        fixture.Add("IA2402", specialization: Specialization.GA2D);
        Assert.Empty(Match(fixture, "I_IA24(SAWM)"));
    }

    [Fact]
    public void OrdinaryWildcardRetainsLegacyParentMatching()
    {
        var fixture = new Fixture();
        fixture.Add("IA2401(ru)", specialization: Specialization.GA2D);
        fixture.Add("IA2402(ru)", specialization: Specialization.GA2D, otherCourse: true);
        fixture.Add("IA2403(ru)");
        Assert.Equal(new[] { new GroupId(0), new GroupId(1), new GroupId(2) }, Match(fixture, "IA24(GA2D)ru").ToArray());
    }

    [Fact]
    public void OrdinaryWildcardRetainsOptionalPartitionFallback()
    {
        var fixture = new Fixture();
        fixture.Add("IA2401(ru)", specialization: Specialization.All);
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.Optional;
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.General.Type = LessonType.Curs;
        var schedule = fixture.Build();
        var group = Parse("IA24(GA2D)ru");
        var parents = GroupsNavigator.FindGroupMatch(schedule, fixture.Course, Resolver.Resolve(group), group);
        Assert.Equal(new[] { new GroupId(0) }, parents.ToArray());
        var groups = new FoundGroups { IsWildcard = true, Value = parents };
        var result = MatchLessonHelper.MatchLessonsInSchedule(new(
            new LessonSearchFilter(fixture.Course, groups, Resolver.Resolve(group)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule)).ToArray();
        var lesson = Assert.Single(result);
        Assert.Equal(new GroupPartitionKey(SpecialSubGroups.Optional, Specialization.All),
            schedule.Get(lesson).Lesson.GroupPartitionKey);
    }

    [Fact]
    public void UnknownTrailingSuffixIsSkippedWithoutAbortingOtherGroups()
    {
        var document = new HtmlParser().ParseDocument("""
            <form name="lesson"><div class="row"></div><div class="row"><div class="col">
            <div class="row"><a href="https://registry.test/unknown">IA2401unknown</a><a href="https://registry.test/evaluation">Grades</a></div>
            <div class="row"><a href="https://registry.test/group">IA2401ru</a><a href="https://registry.test/evaluation">Grades</a></div>
            </div></div></form>
            """);
        int failures = 0;
        int searches = 0;
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = document,
            GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref GroupForSearch group) => { searches++; return [new GroupId(0)]; },
            ParseErrorHandler = _ => failures++,
        }).ToArray();
        Assert.Single(result);
        Assert.Equal(1, failures);
        Assert.Equal(1, searches);
    }

    [Theory]
    [InlineData("I_DJ24(SAWM)")]
    [InlineData("IA_I24(SAWM)")]
    [InlineData("I__IA24(SAWM)")]
    [InlineData("IA__2401")]
    [InlineData("IA_I2401")]
    [InlineData("I_IA_24(SAWM)")]
    public void UnconfirmedCombinedGrammarIsRejected(string value)
    {
        Assert.Throws<GroupParsingException>(() => Parse(value));
    }

    [Theory]
    [InlineData("DJ2204r(II)", "DJ", 4, "II", Language.Ru)]
    [InlineData("I2202rse", "I", 2, "", Language.Ru)]
    [InlineData("I2302se(II)", "I", 2, "II", null)]
    [InlineData("I2302SEr", "I", 2, "", Language.Ru)]
    public void HistoricalMarkersPreserveLanguageAndNumericSubgroup(string value, string faculty,
        int number, string subgroup, Language? language)
    {
        var group = Parse(value);
        Assert.Equal(faculty, group.FacultyName.ToString());
        Assert.Equal(number, group.GroupNumber);
        Assert.Equal(subgroup, group.SubGroupName.ToString());
        Assert.Equal(language, group.Language);
    }

    [Theory]
    [InlineData("I2302rr")]
    [InlineData("I2302rSEru")]
    public void HistoricalMarkersStillRejectDuplicateLanguage(string value)
    {
        Assert.Throws<GroupParsingException>(() => Parse(value));
    }

    [Theory]
    [InlineData("IA2202r(S)")]
    [InlineData("IA2101(D)")]
    [InlineData("IA2201(D)")]
    public void UnknownHistoricalPartitionIsSkippedBeforeSearchingParents(string label)
    {
        var document = new HtmlParser().ParseDocument($"""
            <form name="lesson"><div class="row"></div><div class="row"><div class="col">
            <div class="row"><a href="https://registry.test/group">{label}</a><a href="https://registry.test/evaluation">Grades</a></div>
            </div></div></form>
            """);
        int searches = 0;
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = document,
            GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref GroupForSearch group) => { searches++; return [new GroupId(0)]; },
            ParseErrorHandler = _ => Assert.Fail("Known legacy markers should parse."),
        }).ToArray();
        Assert.Empty(result);
        Assert.Equal(0, searches);
    }

    [Fact]
    public void CombinedResolvedParentsRejectSeparateLabs()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402(ru)");
        fixture.Add("DJ2401(ru)");
        var schedule = fixture.Build();
        var group = Parse("I_IA24(SAWM)");
        var parents = GroupsNavigator.FindGroupMatch(schedule, fixture.Course, Resolver.Resolve(group), group);
        var groups = new FoundGroups { IsWildcard = true, IsCombined = true, Value = parents };
        var result = MatchLessonHelper.MatchLessonsInSchedule(new(
            new LessonSearchFilter(fixture.Course, groups, Resolver.Resolve(group)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule)).ToArray();
        Assert.Empty(result);
    }

    [Fact]
    public void CombinedLessonContainingAnOutsideParentIsExcluded()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("DJ2401(ru)");
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.Groups.Add(new GroupId(1));
        var schedule = fixture.Build();
        var groups = new FoundGroups { IsWildcard = true, IsCombined = true, Value = [new GroupId(0)] };
        var result = MatchLessonHelper.MatchLessonsInSchedule(new(
            new LessonSearchFilter(fixture.Course, groups, new(SubGroup.All, Specialization.DezvoltareaAplicatiilor)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule)).ToArray();
        Assert.Empty(result);
    }

    [Fact]
    public void CombinedLinkCarriesItsMatchingKindAndRemappedPartition()
    {
        var document = new HtmlParser().ParseDocument("""
            <form name="lesson"><div class="row"></div><div class="row"><div class="col">
            <div class="row"><a href="https://registry.test/group">I_IA24(SAWM)</a><a href="https://registry.test/evaluation">Grades</a></div>
            </div></div></form>
            """);
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = document,
            GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref GroupForSearch group) =>
            {
                group = Parse(group.UnparsedName);
                return [new GroupId(0), new GroupId(1)];
            },
            ParseErrorHandler = _ => Assert.Fail("Confirmed combined label should parse."),
        }).ToArray();
        var link = Assert.Single(result);
        Assert.True(link.Groups.IsCombined);
        Assert.True(link.Groups.IsWildcard);
        Assert.Equal(new GroupPartitionKey(SubGroup.All, Specialization.DezvoltareaAplicatiilor), link.GroupPartition);
    }

    [Theory]
    [InlineData("IA2401", Language.Ro)]
    [InlineData("IA2401ru", Language.Ru)]
    public void WholeGroupRegistryIncludesParentLanguageLabsButExcludesEnglishSubgroup(string label, Language language)
    {
        var fixture = new Fixture();
        fixture.Add(language == Language.Ru ? "IA2401(ru)" : "IA2401", specialization: Specialization.All);
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.FromLanguage(language);
        fixture.Builder.RegularLesson(lesson =>
        {
            lesson.Group(new GroupId(0));
            lesson.Course(fixture.Course);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Type(LessonType.Lab);
            lesson.SubGroup(SpecialSubGroups.Eng);
        });
        var schedule = fixture.Build();
        var parsed = Parse(label);
        var parents = GroupsNavigator.FindGroupMatch(schedule, parsed);
        var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
            new LessonSearchFilter(fixture.Course, new FoundGroups { Value = parents, IsWildcard = false }, Resolver.Resolve(parsed)),
            fixture.Builder.LookupModule!.LessonsByCourse, schedule)).ToArray();
        var lesson = Assert.Single(lessons);
        Assert.Equal(SpecialSubGroups.FromLanguage(language), schedule.Get(lesson).Lesson.SubGroup);
    }

    [Theory]
    [InlineData("IA2401(en)", true)]
    [InlineData("IA2401(ru)", false)]
    [InlineData("IA2401", false)]
    public void EnglishRegistryMarkerMatchesWholeGroupOnlyForEnglishLanguageParents(string name, bool wholeGroup)
    {
        var fixture = new Fixture();
        fixture.Add(name, specialization: Specialization.All);
        var english = new GroupPartitionKey(SpecialSubGroups.Eng, Specialization.All);
        var partition = GroupsNavigator.NormalizePartition(fixture.Build(), new LessonGroups { new GroupId(0) }, english);
        Assert.Equal(wholeGroup ? GroupPartitionKey.All : english, partition);
    }

    private static readonly RegistryGroupMatchRule EnglishRule = new()
    {
        Faculty = "DJ", SubGroup = SpecialSubGroups.Eng.Value!, Language = Language.En,
        MatchByLanguage = true, AllowParentGroup = true,
    };

    [Theory]
    [InlineData("DJ2501(en)")]
    [InlineData("DJ2501(en)ru")]
    public void ConfiguredLanguageRuleSelectsUniqueEnglishParentAcrossNumbers(string registryName)
    {
        var fixture = new Fixture();
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Add("DJ2502(ru)", specialization: Specialization.All);
        fixture.Add("DJ2503(en)", specialization: Specialization.All);
        fixture.Add("DJ2404(en)", specialization: Specialization.All);
        fixture.Add("IA2504(en)", specialization: Specialization.All);
        var group = Parse(registryName);
        var schedule = fixture.Build();
        var strict = GroupsNavigator.FindGroupMatch(schedule, fixture.Course, Resolver.Resolve(group), group);
        if (registryName.EndsWith("ru", StringComparison.Ordinal))
            Assert.Empty(strict);
        else
            Assert.Equal(new[] { new GroupId(0) }, strict.ToArray());
        var matched = GroupsNavigator.FindGroupMatch(schedule, fixture.Course, Resolver.Resolve(group), group, [EnglishRule]);
        Assert.Equal(new[] { new GroupId(2) }, matched.ToArray());
        Assert.Equal(GroupPartitionKey.All, GroupsNavigator.NormalizePartition(schedule, matched, Resolver.Resolve(group)));
    }

    [Theory]
    [InlineData("DJ2504(en)")]
    [InlineData("DJ2504(ru)")]
    [InlineData("DJ2504")]
    public void AmbiguousIntakeRequiresStrictMatching(string extraParent)
    {
        var fixture = new Fixture();
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Add("DJ2502(ru)", specialization: Specialization.All);
        fixture.Add("DJ2503(en)", specialization: Specialization.All);
        fixture.Add(extraParent, specialization: Specialization.All);
        var group = Parse("DJ2501(en)");
        Assert.Equal(new[] { new GroupId(0) }, GroupsNavigator.FindGroupMatch(fixture.Build(), fixture.Course,
            Resolver.Resolve(group), group, [EnglishRule]).ToArray());
    }

    [Fact]
    public void LanguageFallbackRequiresCourseEvidenceAndParentPermission()
    {
        var fixture = new Fixture();
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Add("DJ2503(en)", specialization: Specialization.All, otherCourse: true);
        var group = Parse("DJ2502(en)");
        Assert.Empty(GroupsNavigator.FindGroupMatch(fixture.Build(), fixture.Course, Resolver.Resolve(group), group, [EnglishRule]));

        var other = new Fixture();
        other.Add("DJ2503(en)", specialization: Specialization.All);
        var rule = new RegistryGroupMatchRule
        {
            Faculty = "DJ", SubGroup = SpecialSubGroups.Eng.Value!, Language = Language.En, MatchByLanguage = true,
        };
        Assert.Empty(GroupsNavigator.FindGroupMatch(other.Build(), other.Course, Resolver.Resolve(group), group, [rule]));
        var explicitSubgroup = new Fixture();
        explicitSubgroup.Add("DJ2503(ru)", specialization: Specialization.All);
        explicitSubgroup.Add("DJ2503(ru)", specialization: Specialization.All);
        explicitSubgroup.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.Eng;
        explicitSubgroup.Builder.WeeklyLessons.Ref(1).Data.Base.Group.SubGroup = SpecialSubGroups.FromLanguage(Language.Ru);
        Assert.Equal(new[] { new GroupId(0) }, GroupsNavigator.FindGroupMatch(explicitSubgroup.Build(), explicitSubgroup.Course,
            Resolver.Resolve(group), group, [rule]).ToArray());
    }

    [Fact]
    public void MultipleEnglishAudiencesRequireStrictMatchingEvenWithDistinctParentLanguages()
    {
        var fixture = new Fixture();
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.Eng;
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Builder.WeeklyLessons.Ref(1).Data.Base.Group.SubGroup = SpecialSubGroups.FromLanguage(Language.Ro);
        fixture.Add("DJ2503(en)", specialization: Specialization.All);
        var group = Parse("DJ2502(en)");
        Assert.Empty(GroupsNavigator.FindGroupMatch(fixture.Build(), fixture.Course, Resolver.Resolve(group), group, [EnglishRule]));
    }

    [Fact]
    public void MultipleRegistryEnglishLinksDisableLanguageFallback()
    {
        var target = Parse("DJ2501(en)");
        Assert.True(GroupsNavigator.HasUniqueRegistryAudience([target, Parse("DJ2502ru")], target, EnglishRule, Resolver));
        Assert.False(GroupsNavigator.HasUniqueRegistryAudience([target, Parse("DJ2503(en)")], target, EnglishRule, Resolver));
        Assert.True(GroupsNavigator.HasUniqueRegistryAudience([target, Parse("DJ2403(en)")], target, EnglishRule, Resolver));
    }

    [Fact]
    public void ConfiguredRulesSupportOtherLanguagesWithoutHardcodedFacultyMatching()
    {
        var fixture = new Fixture();
        fixture.Add("IA2501(ru)", specialization: Specialization.All);
        fixture.Add("IA2502", specialization: Specialization.All);
        var rule = new RegistryGroupMatchRule
        {
            Faculty = "IA", SubGroup = SpecialSubGroups.FromLanguage(Language.Ro).Value!, Language = Language.Ro,
            MatchByLanguage = true, AllowParentGroup = true,
        };
        var group = Parse("IA2501");
        var partition = new GroupPartitionKey(SpecialSubGroups.FromLanguage(Language.Ro), Specialization.All);
        var schedule = fixture.Build();
        var parents = GroupsNavigator.FindGroupMatch(schedule, fixture.Course, partition, group, [rule]);
        Assert.Equal(new[] { new GroupId(1) }, parents.ToArray());
        Assert.Equal(GroupPartitionKey.All, GroupsNavigator.NormalizePartition(schedule, parents, partition, [rule]));
    }

    private static readonly GroupPartitionResolver Resolver = new(null);
    private static GroupForSearch Parse(string value)
    {
        var parsed = RegistryScraping.ParseGroupFromOnlineRegistry(
            GroupParseContext.Create(new() { CurrentStudyYear = new(2026) }), value);
        var remappings = new Remappings();
        Config.ConfigureRemappings(remappings);
        if (!parsed.SubGroupName.IsEmpty)
        {
            parsed.SubGroupName = remappings.SubGroupNameRemappings.Remap(parsed.SubGroupName).Value!.AsMemory();
        }
        return parsed;
    }

    private static LessonGroups Match(Fixture fixture, string value)
    {
        var group = Parse(value);
        return GroupsNavigator.FindGroupMatch(fixture.Build(), fixture.Course, Resolver.Resolve(group), group);
    }

    private sealed class Fixture
    {
        public readonly ScheduleBuilder Builder = new();
        public readonly CourseId Course;
        private readonly CourseId _otherCourse;
        private int _slot;

        public Fixture()
        {
            Builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            Builder.EnableLookupModule();
            Course = Builder.Course("Synthetic elective");
            _otherCourse = Builder.Course("Unrelated synthetic course");
        }

        public Schedule Build()
        {
            Builder.RefreshLookup();
            return Builder.Build();
        }

        public void Add(string name, Specialization? specialization = null, bool otherCourse = false)
        {
            var group = Builder.Group(name);
            Builder.RegularLesson(lesson =>
            {
                lesson.Group(group);
                lesson.Course(otherCourse ? _otherCourse : Course);
                lesson.DayOfWeek(DayOfWeek.Monday);
                lesson.TimeSlot(new TimeSlot(_slot++));
                lesson.Type(LessonType.Lab);
                lesson.Specialization(specialization ?? Specialization.DezvoltareaAplicatiilor);
            });
        }
    }
}
