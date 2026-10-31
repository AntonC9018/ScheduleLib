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
        var groups = new FoundGroups
        {
            IsWildcard = true,
            Value = [new GroupId(0), new GroupId(1)],
        };
        var courseId = fixture.Course;
        var groups2 = groups;
        var groupPartition = Resolver.Resolve(parsed);
        var filter = new LessonSearchFilter(
            courseId,
            groups2,
            groupPartition);
        var lookup = fixture.Builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        LessonMatchParams p = new(
            filter,
            lookup,
            schedule2);
        var matchLessonsInSchedule = MatchLessonHelper.MatchLessonsInSchedule(p);
        var lessons = matchLessonsInSchedule.ToArray();
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
        {
            GroupId CalculateId()
            {
                var id = new GroupId(1);
                return id;
            }

            fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.Groups
                .Add(CalculateId());
        }

        var schedule = fixture.Build();
        var groups = new FoundGroups
        {
            IsWildcard = true,
            IsCombined = combined,
            Value = [new GroupId(0)],
        };
        var courseId = fixture.Course;
        var groups2 = groups;
        GroupPartitionKey groupPartition = new(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
        var filter = new LessonSearchFilter(
            courseId,
            groups2,
            groupPartition);
        var lookup = fixture.Builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        var parameters = new LessonMatchParams(
            filter,
            lookup,
            schedule2);
        var collection = MatchLessonHelper.MatchLessonsInSchedule(parameters);
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);

        bool CalculateCondition()
        {
            if (!combined)
            {
                return false;
            }

            return sharedLab;
        }

        bool condition = CalculateCondition();


        int expected2;
        if (condition)
        {
            expected2 = 0;
        }
        else
        {
            expected2 = 1;
        }

        var findUnsafeIntakeLabs = MatchLessonHelper.FindUnsafeIntakeLabs(parameters);
        int actual2 = findUnsafeIntakeLabs.Count;
        Assert.Equal(expected: expected2, actual: actual2);
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

        GroupPartitionKey CalculateGroupPartition()
        {
            GroupPartitionKey groupPartition = new(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
            return groupPartition;
        }

        var parameters = new LessonMatchParams(
            new LessonSearchFilter(
                fixture.Course,
                new FoundGroups
                {
                    IsWildcard = true,
                    Value = [new GroupId(0)],
                },
                CalculateGroupPartition()),
            fixture.Builder.LookupModule!.LessonsByCourse,
            schedule);
        var collection = MatchLessonHelper.MatchLessonsInSchedule(parameters);
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
        var collection3 = MatchLessonHelper.FindUnsafeIntakeLabs(parameters);
        IEnumerable<AnyLessonId> collection4 = collection3;
        Assert.Single(collection4);
    }

    [Fact]
    public void ConfirmedCombinedLabelPreservesIntakeWildcardAndMissingLanguage()
    {
        var group = Parse("I_IA24(SAWM)");
        Assert.Equal(expected: "I_IA", actual: group.FacultyName.ToString());
        Assert.Equal(expected: 3, actual: group.Grade.Value);
        Assert.Null(group.GroupNumber);
        Assert.Null(group.Language);
        Assert.True(group.IsWildcard);
        var expected2 = new GroupPartitionKey(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
        var actual2 = Resolver.Resolve(group);
        Assert.Equal(expected: expected2, actual: actual2);
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
        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
                new GroupId(1),
            },
            actual: result.ToArray());
    }

    [Fact]
    public void CombinedLabelCannotResolveWithoutCourseAndPartitionEvidence()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402(ru)");

        GroupForSearch CalculateG()
        {
            var g = Parse("I_IA24(SAWM)");
            return g;
        }

        LessonGroups CalculateCollection()
        {
            var collection = GroupsNavigator.FindGroupMatch(fixture.Build(), CalculateG());
            return collection;
        }

        Assert.Empty(CalculateCollection());
    }

    [Fact]
    public void CombinedMissingLanguageCanResolveRomanianParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401");
        fixture.Add("IA2402");
        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
                new GroupId(1),
            },
            actual: Match(fixture, "I_IA24(SAWM)")
                .ToArray());
    }

    [Fact]
    public void CombinedNumberedLabelKeepsItsGroupNumberConstraint()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)", specialization: Specialization.All);
        fixture.Add("IA2402(ru)", specialization: Specialization.All);
        Assert.Equal(
            expected: new[]
            {
                new GroupId(1),
            },
            actual: Match(fixture, "I_IA2402ru")
                .ToArray());
    }

    [Fact]
    public void CombinedMissingLanguageSkipsAmbiguousParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402");
        var collection = Match(fixture, "I_IA24(SAWM)");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Fact]
    public void CombinedExplicitLanguageSelectsItsParents()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("IA2402");
        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
            },
            actual: Match(fixture, "I_IA24(SAWM)ru")
                .ToArray());
    }

    [Fact]
    public void CombinedMissingLanguageSkipsWhenOnlyUnrelatedLessonsExist()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)", otherCourse: true);
        fixture.Add("IA2402", specialization: Specialization.GA2D);
        var collection = Match(fixture, "I_IA24(SAWM)");
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Fact]
    public void OrdinaryWildcardRetainsLegacyParentMatching()
    {
        var fixture = new Fixture();
        fixture.Add("IA2401(ru)", specialization: Specialization.GA2D);
        fixture.Add(
            "IA2402(ru)",
            specialization: Specialization.GA2D,
            otherCourse: true);
        fixture.Add("IA2403(ru)");
        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
                new GroupId(1),
                new GroupId(2),
            },
            actual: Match(fixture, "IA24(GA2D)ru")
                .ToArray());
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

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        var parents = GroupsNavigator.FindGroupMatch(
            schedule,
            fixture.Course,
            CalculatePartition(),
            group);
        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
            },
            actual: parents.ToArray());
        var groups = new FoundGroups
        {
            IsWildcard = true,
            Value = parents,
        };
        var courseId = fixture.Course;
        var groups2 = groups;
        var groupPartition = Resolver.Resolve(group);
        var filter = new LessonSearchFilter(
            courseId,
            groups2,
            groupPartition);
        var lookup = fixture.Builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        LessonMatchParams p = new(
            filter,
            lookup,
            schedule2);
        var matchLessonsInSchedule = MatchLessonHelper.MatchLessonsInSchedule(p);
        var result = matchLessonsInSchedule.ToArray();
        var lesson = Assert.Single(result);

        GroupPartitionKey CalculateExpected()
        {
            var expected2 = new GroupPartitionKey(SpecialSubGroups.Optional, Specialization.All);
            return expected2;
        }

        Assert.Equal(
            expected: CalculateExpected(),
            actual: schedule.Get(lesson).Lesson.GroupPartitionKey);
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
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref group) =>
            {
                searches++;
                return [new GroupId(0)];
            },
            ParseErrorHandler = _ =>
            {
                failures++;
            },
        })
            .ToArray();
        Assert.Single(result);
        Assert.Equal(expected: 1, actual: failures);
        Assert.Equal(expected: 1, actual: searches);
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
    [InlineData("DJ2204r(II)",
        "DJ",
        4,
        "II",
        Language.Ru,
        false)]
    [InlineData("I2202rse",
        "I",
        2,
        "",
        Language.Ru,
        true)]
    [InlineData("I2302se(II)",
        "I",
        2,
        "II",
        null,
        true)]
    [InlineData("I2302SEr",
        "I",
        2,
        "",
        Language.Ru,
        true)]
    public void HistoricalMarkersPreserveLanguageAndNumericSubgroup(
        string value,
        string faculty,
        int number,
        string subgroup,
        Language? language,
        bool isAdditionalCourses)
    {
        var group = Parse(value);
        Assert.Equal(expected: faculty, actual: group.FacultyName.ToString());
        Assert.Equal(expected: number, actual: group.GroupNumber);
        Assert.Equal(expected: subgroup, actual: group.SubGroupName.ToString());
        Assert.Equal(expected: language, actual: group.Language);
        Assert.Equal(expected: isAdditionalCourses, actual: group.IsAdditionalCourses);
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
        var htmlParser = new HtmlParser();
        string source = $"""
                         <form name="lesson"><div class="row"></div><div class="row"><div class="col">
                         <div class="row"><a href="https://registry.test/group">{label}</a><a href="https://registry.test/evaluation">Grades</a></div>
                         </div></div></form>
                         """;
        var document = htmlParser.ParseDocument(source);
        int searches = 0;
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = document,
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref group) =>
            {
                searches++;
                return [new GroupId(0)];
            },
            ParseErrorHandler = _ => Assert.Fail("Known legacy markers should parse."),
        })
            .ToArray();
        Assert.Empty(result);
        Assert.Equal(expected: 0, actual: searches);
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

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        var parents = GroupsNavigator.FindGroupMatch(
            schedule,
            fixture.Course,
            CalculatePartition(),
            group);
        var groups = new FoundGroups
        {
            IsWildcard = true,
            IsCombined = true,
            Value = parents,
        };
        var courseId = fixture.Course;
        var groups2 = groups;
        var groupPartition = Resolver.Resolve(group);
        var filter = new LessonSearchFilter(
            courseId,
            groups2,
            groupPartition);
        var lookup = fixture.Builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        LessonMatchParams p = new(
            filter,
            lookup,
            schedule2);
        var matchLessonsInSchedule = MatchLessonHelper.MatchLessonsInSchedule(p);
        var result = matchLessonsInSchedule.ToArray();
        Assert.Empty(result);
    }

    [Fact]
    public void CombinedLessonContainingAnOutsideParentIsExcluded()
    {
        var fixture = new Fixture();
        fixture.Add("I2401(ru)");
        fixture.Add("DJ2401(ru)");

        GroupId CalculateId()
        {
            var id = new GroupId(1);
            return id;
        }

        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.Groups
            .Add(CalculateId());
        var schedule = fixture.Build();
        var groups = new FoundGroups
        {
            IsWildcard = true,
            IsCombined = true,
            Value = [new GroupId(0)],
        };
        var courseId = fixture.Course;
        var groups2 = groups;
        GroupPartitionKey groupPartition = new(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
        var filter = new LessonSearchFilter(
            courseId,
            groups2,
            groupPartition);
        var lookup = fixture.Builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        LessonMatchParams p = new(
            filter,
            lookup,
            schedule2);
        var matchLessonsInSchedule = MatchLessonHelper.MatchLessonsInSchedule(p);
        var result = matchLessonsInSchedule.ToArray();
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
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref group) =>
            {
                group = Parse(group.UnparsedName);
                return [new GroupId(0), new GroupId(1)];
            },
            ParseErrorHandler = _ => Assert.Fail("Confirmed combined label should parse."),
        })
            .ToArray();
        var link = Assert.Single(result);
        Assert.True(link.Groups.IsCombined);
        Assert.True(link.Groups.IsWildcard);
        var expected2 = new GroupPartitionKey(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
        var actual2 = link.GroupPartition;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Theory]
    [InlineData("IA2401", Language.Ro)]
    [InlineData("IA2401ru", Language.Ru)]
    public void WholeGroupRegistryIncludesParentLanguageLabsButExcludesEnglishSubgroup(string label, Language language)
    {
        var fixture = new Fixture();
        var fixture2 = fixture;
        string name;
        if (language == Language.Ru)
        {
            name = "IA2401(ru)";
        }
        else
        {
            name = "IA2401";
        }

        Specialization? specialization2 = Specialization.All;
        fixture2.Add(name, specialization: specialization2);
        fixture.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.FromLanguage(language);
        fixture.Builder.RegularLesson(lesson =>
        {
            var lessonBuilder = lesson;
            var group = new GroupId(0);
            lessonBuilder.Group(group);
            lesson.Course(fixture.Course);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
            lesson.Type(LessonType.Lab);
            lesson.SubGroup(SpecialSubGroups.Eng);
        });
        var schedule = fixture.Build();
        var parsed = Parse(label);
        var parents = GroupsNavigator.FindGroupMatch(schedule, parsed);

        GroupPartitionKey CalculateGroupPartition()
        {
            var groupPartition = Resolver.Resolve(parsed);
            return groupPartition;
        }

        var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
                new LessonSearchFilter(
                    fixture.Course,
                    new FoundGroups
                    {
                        Value = parents,
                        IsWildcard = false,
                    },
                    CalculateGroupPartition()),
                fixture.Builder.LookupModule!.LessonsByCourse,
                schedule))
            .ToArray();
        var lesson = Assert.Single(lessons);

        SubGroup CalculateExpected()
        {
            var expected2 = SpecialSubGroups.FromLanguage(language);
            return expected2;
        }

        Assert.Equal(expected: CalculateExpected(), actual: schedule.Get(lesson).Lesson.SubGroup);
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
        var partition = GroupsNavigator.NormalizePartition(
            fixture.Build(),
            new LessonGroups { new GroupId(0) },
            english);
        GroupPartitionKey expected2;
        if (wholeGroup)
        {
            expected2 = GroupPartitionKey.All;
        }
        else
        {
            expected2 = english;
        }

        var actual2 = partition;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    private static readonly RegistryGroupMatchRule EnglishRule = new()
    {
        Faculty = "DJ",
        SubGroup = SpecialSubGroups.Eng.Value!,
        Language = Language.En,
        MatchByLanguage = true,
        AllowParentGroup = true,
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

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        var strict = GroupsNavigator.FindGroupMatch(
            schedule,
            fixture.Course,
            CalculatePartition(),
            group);
        if (registryName.EndsWith("ru", StringComparison.Ordinal))
        {
            Assert.Empty(strict);
        }
        else
        {
            Assert.Equal(
                expected: new[]
                {
                    new GroupId(0),
                },
                actual: strict.ToArray());
        }

        GroupPartitionKey CalculatePartition2()
        {
            var partition2 = Resolver.Resolve(group);
            return partition2;
        }

        var matched = GroupsNavigator.FindGroupMatch(
            schedule,
            fixture.Course,
            CalculatePartition2(),
            group,
            [EnglishRule]);
        Assert.Equal(
            expected: new[]
            {
                new GroupId(2),
            },
            actual: matched.ToArray());

        GroupPartitionKey CalculatePartition3()
        {
            var partition3 = Resolver.Resolve(group);
            return partition3;
        }

        var expected2 = GroupPartitionKey.All;
        var actual2 = GroupsNavigator.NormalizePartition(
            schedule,
            matched,
            CalculatePartition3());
        Assert.Equal(expected: expected2, actual: actual2);
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

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
            },
            actual: GroupsNavigator.FindGroupMatch(
                    fixture.Build(),
                    fixture.Course,
                    CalculatePartition(),
                    group,
                    [EnglishRule])
                .ToArray());
    }

    [Fact]
    public void LanguageFallbackRequiresCourseEvidenceAndParentPermission()
    {
        var fixture = new Fixture();
        fixture.Add("DJ2501", specialization: Specialization.All);
        fixture.Add(
            "DJ2503(en)",
            specialization: Specialization.All,
            otherCourse: true);
        var group = Parse("DJ2502(en)");

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        LessonGroups CalculateCollection()
        {
            var collection = GroupsNavigator.FindGroupMatch(
                fixture.Build(),
                fixture.Course,
                CalculatePartition(),
                group,
                [EnglishRule]);
            return collection;
        }

        Assert.Empty(CalculateCollection());

        var other = new Fixture();
        other.Add("DJ2503(en)", specialization: Specialization.All);
        var rule = new RegistryGroupMatchRule
        {
            Faculty = "DJ",
            SubGroup = SpecialSubGroups.Eng.Value!,
            Language = Language.En,
            MatchByLanguage = true,
        };

        GroupPartitionKey CalculatePartition2()
        {
            var partition2 = Resolver.Resolve(group);
            return partition2;
        }

        LessonGroups CalculateCollection2()
        {
            var collection2 = GroupsNavigator.FindGroupMatch(
                other.Build(),
                other.Course,
                CalculatePartition2(),
                group,
                [rule]);
            return collection2;
        }

        Assert.Empty(CalculateCollection2());
        var explicitSubgroup = new Fixture();
        explicitSubgroup.Add("DJ2503(ru)", specialization: Specialization.All);
        explicitSubgroup.Add("DJ2503(ru)", specialization: Specialization.All);
        explicitSubgroup.Builder.WeeklyLessons.Ref(0).Data.Base.Group.SubGroup = SpecialSubGroups.Eng;
        explicitSubgroup.Builder.WeeklyLessons.Ref(1).Data.Base.Group.SubGroup =
            SpecialSubGroups.FromLanguage(Language.Ru);

        GroupPartitionKey CalculatePartition3()
        {
            var partition3 = Resolver.Resolve(group);
            return partition3;
        }

        Assert.Equal(
            expected: new[]
            {
                new GroupId(0),
            },
            actual: GroupsNavigator.FindGroupMatch(
                    explicitSubgroup.Build(),
                    explicitSubgroup.Course,
                    CalculatePartition3(),
                    group,
                    [rule])
                .ToArray());
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

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        LessonGroups CalculateCollection()
        {
            var collection = GroupsNavigator.FindGroupMatch(
                fixture.Build(),
                fixture.Course,
                CalculatePartition(),
                group,
                [EnglishRule]);
            return collection;
        }

        Assert.Empty(CalculateCollection());
    }

    [Fact]
    public void MultipleRegistryEnglishLinksDisableLanguageFallback()
    {
        var target = Parse("DJ2501(en)");
        Assert.True(GroupsNavigator.HasUniqueRegistryAudience(
            [target, Parse("DJ2502ru")],
            target,
            EnglishRule,
            Resolver));
        Assert.False(GroupsNavigator.HasUniqueRegistryAudience(
            [target, Parse("DJ2503(en)")],
            target,
            EnglishRule,
            Resolver));
        Assert.True(GroupsNavigator.HasUniqueRegistryAudience(
            [target, Parse("DJ2403(en)")],
            target,
            EnglishRule,
            Resolver));
    }

    [Fact]
    public void AdditionalCoursesVariantDoesNotDisableLanguageRuleMatching()
    {
        var ordinary = Parse("DJ2501(en)");
        var additionalSamePartition = Parse("DJ2503SE(en)");
        Assert.True(ordinary.IsAdditionalCourses == false);
        Assert.True(additionalSamePartition.IsAdditionalCourses);

        // Same Eng partition but different variant still counts as unique within each variant.
        bool CalculateCondition()
        {
            bool condition3 = GroupsNavigator.HasUniqueRegistryAudience(
                [ordinary, additionalSamePartition],
                ordinary,
                EnglishRule,
                Resolver);
            return condition3;
        }

        Assert.True(CalculateCondition());
        bool condition = GroupsNavigator.HasUniqueRegistryAudience(
            [ordinary, additionalSamePartition],
            additionalSamePartition,
            EnglishRule,
            Resolver);
        Assert.True(condition);
        // Same-variant duplicates still disable the language fallback.
        var additionalDuplicate = Parse("DJ2504SE(en)");
        bool condition2 = GroupsNavigator.HasUniqueRegistryAudience(
            [additionalSamePartition, additionalDuplicate],
            additionalSamePartition,
            EnglishRule,
            Resolver);
        Assert.False(condition2);
    }

    [Fact]
    public void GroupLinkCarriesAdditionalCoursesMarkerThroughHtmlNavigation()
    {
        var document = new HtmlParser().ParseDocument("""
                                                      <form name="lesson"><div class="row"></div><div class="row"><div class="col">
                                                      <div class="row"><a href="https://registry.test/ordinary">DJ2501(en)</a><a href="https://registry.test/evaluation">Grades</a></div>
                                                      <div class="row"><a href="https://registry.test/additional">DJ2501SE(en)</a><a href="https://registry.test/evaluation">Grades</a></div>
                                                      </div></div></form>
                                                      """);
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = document,
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
            GroupPartitionResolver = Resolver,
            SearchGroupId = (ref group) => [new GroupId(0)],
            ParseErrorHandler = _ => Assert.Fail("SE markers should parse."),
        })
            .ToArray();
        Assert.Equal(expected: 2, actual: result.Length);
        var ordinary = Assert.Single(result, link => link.Uri.ToString() == "https://registry.test/ordinary");
        var additional = Assert.Single(result, link => link.Uri.ToString() == "https://registry.test/additional");
        Assert.False(ordinary.IsAdditionalCourses);
        Assert.True(additional.IsAdditionalCourses);
        // SE never changes the scheduled partition.
        Assert.Equal(expected: ordinary.GroupPartition, actual: additional.GroupPartition);
    }

    [Fact]
    public void ConfiguredRulesSupportOtherLanguagesWithoutHardcodedFacultyMatching()
    {
        var fixture = new Fixture();
        fixture.Add("IA2501(ru)", specialization: Specialization.All);
        fixture.Add("IA2502", specialization: Specialization.All);
        var rule = new RegistryGroupMatchRule
        {
            Faculty = "IA",
            SubGroup = SpecialSubGroups.FromLanguage(Language.Ro).Value!,
            Language = Language.Ro,
            MatchByLanguage = true,
            AllowParentGroup = true,
        };
        var group = Parse("IA2501");
        var subGroup = SpecialSubGroups.FromLanguage(Language.Ro);
        var specialization2 = Specialization.All;
        var partition = new GroupPartitionKey(subGroup, specialization2);
        var schedule = fixture.Build();
        var parents = GroupsNavigator.FindGroupMatch(
            schedule,
            fixture.Course,
            partition,
            group,
            [rule]);
        Assert.Equal(
            expected: new[]
            {
                new GroupId(1),
            },
            actual: parents.ToArray());
        var expected2 = GroupPartitionKey.All;
        var actual2 = GroupsNavigator.NormalizePartition(
            schedule,
            parents,
            partition,
            [rule]);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    private static readonly GroupPartitionResolver Resolver = new(null);

    private static GroupForSearch Parse(string value)
    {
        var parsed = RegistryScraping.ParseGroupFromOnlineRegistry(
            GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
            value);
        var remappings = new Remappings();
        Config.ConfigureRemappings(remappings);
        if (!parsed.SubGroupName.IsEmpty)
        {
            parsed.SubGroupName = remappings.SubGroupNameRemappings.Remap(parsed.SubGroupName).Value!
                .AsMemory();
        }
        return parsed;
    }

    private static LessonGroups Match(Fixture fixture, string value)
    {
        var group = Parse(value);

        GroupPartitionKey CalculatePartition()
        {
            var partition = Resolver.Resolve(group);
            return partition;
        }

        return GroupsNavigator.FindGroupMatch(
            fixture.Build(),
            fixture.Course,
            CalculatePartition(),
            group);
    }

    private sealed class Fixture
    {
        public readonly ScheduleBuilder Builder = new();
        public readonly CourseId Course;
        private readonly CourseId _otherCourse;
        private int _slot;

        public Fixture()
        {
            Builder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            Builder.EnableLookupModule();
            Course = Builder.Course("Synthetic elective");
            _otherCourse = Builder.Course("Unrelated synthetic course");
        }

        public Schedule Build()
        {
            Builder.RefreshLookup();
            return Builder.Build();
        }

        public void Add(
            string name,
            Specialization? specialization = null,
            bool otherCourse = false)
        {
            var group = Builder.Group(name);
            Builder.RegularLesson(lesson =>
            {
                lesson.Group(group);
                var lessonBuilder2 = lesson;
                CourseId course;
                if (otherCourse)
                {
                    course = _otherCourse;
                }
                else
                {
                    course = Course;
                }

                lessonBuilder2.Course(course);
                lesson.DayOfWeek(DayOfWeek.Monday);
                var lessonBuilder = lesson;
                int index = _slot++;
                var timeSlot = new TimeSlot(index);
                lessonBuilder.TimeSlot(timeSlot);
                lesson.Type(LessonType.Lab);
                lesson.Specialization(specialization ?? Specialization.DezvoltareaAplicatiilor);
            });
        }
    }
}
