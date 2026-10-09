using AngleSharp.Html.Parser;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class CombinedGroupMatchingTests
{
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

    [Fact]
    public void UnknownHistoricalSPartitionIsSkippedBeforeSearchingParents()
    {
        var document = new HtmlParser().ParseDocument("""
            <form name="lesson"><div class="row"></div><div class="row"><div class="col">
            <div class="row"><a href="https://registry.test/group">IA2202r(S)</a><a href="https://registry.test/evaluation">Grades</a></div>
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
    public void CombinedResolvedParentsMatchSeparateLessonsAndExcludeOtherCohorts()
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
        Assert.Equal(2, result.Length);
        Assert.All(result, lesson => Assert.True(schedule.Get(lesson).Lesson.Groups.IsSubSetOf(parents)));
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
