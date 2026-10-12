using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryLessonRoutingTests
{
    [Fact]
    public void ConfirmedManualMergeUsesWholeAudienceEvenWhenOldSubgroupListsRemain()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        foreach (var number in new[] { 1, 2 })
            builder.RegularLesson(lesson =>
            {
                lesson.Course(course); lesson.Group(parent, SubGroup.CreateNumeric(number));
                lesson.Type(LessonType.Lab); lesson.DayOfWeek(number == 1 ? DayOfWeek.Monday : DayOfWeek.Tuesday);
                lesson.TimeSlot(TimeSlot.First);
            });
        builder.RefreshLookup();
        var schedule = builder.Build();
        var config = EmptyConfig();
        config.LabMerges = [new() { Course = "Synthetic course", Group = "IA2601", SourceSubGroup = "I" }];
        var fallback = MakeLink([parent], GroupPartitionKey.All, true, false, "https://registry.test/fallback");
        var oldSubgroup = ExplicitParent(parent, new(SubGroup.CreateNumeric(1), Specialization.All), "https://registry.test/old-subgroup");
        var merged = RegistryLessonRouting.Candidates(config, ParamsFor(builder, schedule, course, fallback.Groups, fallback.GroupPartition));
        var obsolete = RegistryLessonRouting.Candidates(config, ParamsFor(builder, schedule, course, oldSubgroup.Groups, oldSubgroup.GroupPartition));
        Assert.Empty(obsolete.Lessons);
        var survivor = Assert.Single(merged.Lessons);
        Assert.Equal(GroupPartitionKey.All, RegistryLessonRouting.ProjectPartition(config, schedule, schedule.Get(survivor).Lesson));
        Assert.Equal(DayOfWeek.Monday, schedule.Get(survivor).Weekly!.Value.Date.DayOfWeek);
    }

    private static BuiltRegistryConfig EmptyConfig() => new()
    {
        Credentials = new ValueCredentialsSource { Value = new Credentials { Login = "synthetic", Password = "synthetic" } },
        ExtraLessonInstanceAction = ExtraLessonInstanceAction.LeaveAlone,
        EquationCommandsDerivation = new AnyDayDerivation(),
        CommandProcessingConfig = CommandProcessingConfig.DryRun,
        LabMerges = [],
    };

    private static ScheduleBuilder NewBuilder()
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        builder.EnableLookupModule();
        return builder;
    }

    private static GroupLink MakeLink(LessonGroups groups, GroupPartitionKey partition, bool isWildcard, bool isCombined, string uri)
    {
        var found = new FoundGroups { Value = groups, IsWildcard = isWildcard, IsCombined = isCombined };
        return new GroupLink(found, partition, new Uri(uri), new Uri(uri + "/evaluation"));
    }

    private static GroupLink ExplicitParent(GroupId parent, GroupPartitionKey partition, string uri)
    {
        return MakeLink(new LessonGroups { parent }, partition, isWildcard: false, isCombined: false, uri);
    }

    private static LessonMatchParams ParamsFor(ScheduleBuilder builder, Schedule schedule, CourseId course, FoundGroups groups, GroupPartitionKey partition)
    {
        return new(new LessonSearchFilter(course, groups, partition), builder.LookupModule!.LessonsByCourse, schedule);
    }

    private static RegistryLessonSelection CandidatesFor(ScheduleBuilder builder, Schedule schedule, CourseId course, FoundGroups groups, GroupPartitionKey partition)
    {
        return RegistryLessonRouting.Candidates(EmptyConfig(), ParamsFor(builder, schedule, course, groups, partition));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CombinedOrIntakeFallbackReceivesSeparateParentLabsWhenAlone(bool isCombined)
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(first);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(second);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var fallbackGroups = new FoundGroups
        {
            Value = new LessonGroups { first, second },
            IsWildcard = true,
            IsCombined = isCombined,
        };
        var selection = CandidatesFor(builder, schedule, course, fallbackGroups, GroupPartitionKey.All);
        Assert.Equal(2, selection.Lessons.Count);
        Assert.Equal(GroupPartitionKey.All, selection.AttendancePartition);
        Assert.False(selection.IsLabMerge);

        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: MakeLink(new LessonGroups { first, second }, GroupPartitionKey.All, isWildcard: true, isCombined: isCombined, "https://registry.test/fallback"), Selection: selection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        var fallbackUri = new Uri("https://registry.test/fallback");
        Assert.Equal(2, routed[fallbackUri].Count);
        foreach (var entry in routed[fallbackUri])
        {
            var audience = entry.Value;
            Assert.Equal(1, audience.Count);
            var lesson = schedule.Get(entry.Key).Lesson;
            Assert.True(lesson.Groups.IsSingleGroup);
            Assert.True(audience.Contains(lesson.Groups[0]));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FallbackYieldsSeparateParentLabsToExplicitDestinations(bool isCombined)
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(first);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(second);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var fallbackSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first, second }, IsWildcard = true, IsCombined = isCombined },
            GroupPartitionKey.All);
        var firstSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first }, IsWildcard = false },
            GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { second }, IsWildcard = false },
            GroupPartitionKey.All);
        Assert.Equal(2, fallbackSelection.Lessons.Count);
        Assert.Single(firstSelection.Lessons);
        Assert.Single(secondSelection.Lessons);

        var fallbackLink = MakeLink(new LessonGroups { first, second }, GroupPartitionKey.All, isWildcard: true, isCombined: isCombined, "https://registry.test/fallback");
        var firstLink = ExplicitParent(first, GroupPartitionKey.All, "https://registry.test/first");
        var secondLink = ExplicitParent(second, GroupPartitionKey.All, "https://registry.test/second");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: fallbackLink, Selection: fallbackSelection),
            (Group: firstLink, Selection: firstSelection),
            (Group: secondLink, Selection: secondSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        Assert.Empty(routed[fallbackLink.Uri]);
        Assert.Single(routed[firstLink.Uri]);
        Assert.Single(routed[secondLink.Uri]);
        var firstRouted = Assert.Single(routed[firstLink.Uri]);
        Assert.True(firstRouted.Value.Contains(first));
        var secondRouted = Assert.Single(routed[secondLink.Uri]);
        Assert.True(secondRouted.Value.Contains(second));
    }

    [Fact]
    public void ExplicitParentOutranksBroadWildcardPerParent()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(first);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(second);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var broadSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first, second }, IsWildcard = true },
            GroupPartitionKey.All);
        var explicitSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first }, IsWildcard = false },
            GroupPartitionKey.All);
        Assert.Equal(2, broadSelection.Lessons.Count);
        Assert.Single(explicitSelection.Lessons);

        var broadLink = MakeLink(new LessonGroups { first, second }, GroupPartitionKey.All, isWildcard: true, isCombined: false, "https://registry.test/broad");
        var explicitLink = ExplicitParent(first, GroupPartitionKey.All, "https://registry.test/explicit");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: broadLink, Selection: broadSelection),
            (Group: explicitLink, Selection: explicitSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        Assert.Single(routed[explicitLink.Uri]);
        Assert.Single(routed[broadLink.Uri]);
        var explicitEntry = Assert.Single(routed[explicitLink.Uri]);
        Assert.True(explicitEntry.Value.Contains(first));
        Assert.Equal(first, schedule.Get(explicitEntry.Key).Lesson.Groups[0]);
        var broadEntry = Assert.Single(routed[broadLink.Uri]);
        Assert.True(broadEntry.Value.Contains(second));
        Assert.Equal(second, schedule.Get(broadEntry.Key).Lesson.Groups[0]);
    }

    [Fact]
    public void SubgroupListOutranksParentForMatchingNumericPartition()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        var subgroupPartition = new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All);
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            lesson.SubGroup(SubGroup.CreateNumeric(1));
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var subgroupSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, subgroupPartition);
        var parentSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, GroupPartitionKey.All);
        var lab = Assert.Single(subgroupSelection.Lessons);
        Assert.Contains(lab, parentSelection.Lessons);
        Assert.Equal(subgroupPartition, subgroupSelection.AttendancePartition);
        Assert.Equal(GroupPartitionKey.All, parentSelection.AttendancePartition);

        var subgroupLink = ExplicitParent(parent, subgroupPartition, "https://registry.test/subgroup");
        var parentLink = ExplicitParent(parent, GroupPartitionKey.All, "https://registry.test/parent");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: subgroupLink, Selection: subgroupSelection),
            (Group: parentLink, Selection: parentSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        Assert.Single(routed[subgroupLink.Uri]);
        Assert.Empty(routed[parentLink.Uri]);
        var routedEntry = Assert.Single(routed[subgroupLink.Uri]);
        Assert.Equal(lab, routedEntry.Key);
        Assert.True(routedEntry.Value.Contains(parent));
    }

    [Fact]
    public void PartialCoverageRoutesSubgroupISpecificallyAndSubgroupIIToParent()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        var firstPartition = new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All);
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            lesson.SubGroup(SubGroup.CreateNumeric(1));
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            lesson.SubGroup(SubGroup.CreateNumeric(2));
            lesson.DayOfWeek(DayOfWeek.Wednesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var subgroupSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, firstPartition);
        var parentSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, GroupPartitionKey.All);
        Assert.Single(subgroupSelection.Lessons);
        Assert.Equal(2, parentSelection.Lessons.Count);

        var subgroupLink = ExplicitParent(parent, firstPartition, "https://registry.test/subgroup-one");
        var parentLink = ExplicitParent(parent, GroupPartitionKey.All, "https://registry.test/parent");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: subgroupLink, Selection: subgroupSelection),
            (Group: parentLink, Selection: parentSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        var subgroupEntry = Assert.Single(routed[subgroupLink.Uri]);
        var parentEntry = Assert.Single(routed[parentLink.Uri]);
        Assert.Equal(SubGroup.CreateNumeric(1), schedule.Get(subgroupEntry.Key).Lesson.SubGroup);
        Assert.Equal(SubGroup.CreateNumeric(2), schedule.Get(parentEntry.Key).Lesson.SubGroup);
        Assert.True(subgroupEntry.Value.Contains(parent));
        Assert.True(parentEntry.Value.Contains(parent));
    }

    [Fact]
    public void MultipleParentFallbackScopesRetainSeparateGroupAudiences()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(first);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(second);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var firstSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first }, IsWildcard = false }, GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { second }, IsWildcard = false }, GroupPartitionKey.All);
        var firstLink = ExplicitParent(first, GroupPartitionKey.All, "https://registry.test/first");
        var secondLink = ExplicitParent(second, GroupPartitionKey.All, "https://registry.test/second");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: firstLink, Selection: firstSelection),
            (Group: secondLink, Selection: secondSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        var firstEntry = Assert.Single(routed[firstLink.Uri]);
        var secondEntry = Assert.Single(routed[secondLink.Uri]);
        Assert.Equal(1, firstEntry.Value.Count);
        Assert.Equal(1, secondEntry.Value.Count);
        Assert.True(firstEntry.Value.Contains(first));
        Assert.True(secondEntry.Value.Contains(second));
        Assert.NotEqual(firstEntry.Key, secondEntry.Key);
    }

    [Fact]
    public void BroadCombinedLessonMustNotIncludeOutsideParents()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        GroupId outside = builder.Group("DJ2401(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Groups([first, second]);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Groups([first, outside]);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var combinedGroups = new FoundGroups
        {
            Value = new LessonGroups { first, second },
            IsWildcard = true,
            IsCombined = true,
        };
        var selection = CandidatesFor(builder, schedule, course, combinedGroups, GroupPartitionKey.All);
        AnyLessonId valid = default;
        AnyLessonId tainted = default;
        bool foundValid = false;
        bool foundTainted = false;
        foreach (var id in builder.LookupModule!.LessonsByCourse[course])
        {
            var lesson = schedule.Get(id).Lesson;
            if (lesson.Groups.Count == 2 && lesson.Groups.Contains(first) && lesson.Groups.Contains(second))
            {
                valid = id;
                foundValid = true;
            }
            if (lesson.Groups.Count == 2 && lesson.Groups.Contains(first) && lesson.Groups.Contains(outside))
            {
                tainted = id;
                foundTainted = true;
            }
        }
        Assert.True(foundValid);
        Assert.True(foundTainted);
        Assert.Contains(valid, selection.Lessons);
        Assert.DoesNotContain(tainted, selection.Lessons);
    }

    [Fact]
    public void NonLabLecturesRemainUsableEvenIfLabRoutesElsewhere()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic elective");
        GroupId first = builder.Group("IA2403(ru)");
        GroupId second = builder.Group("IA2404(ru)");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(first);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(second);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(new TimeSlot(1));
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Groups([first, second]);
            lesson.Type(LessonType.Curs);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var fallbackSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first, second }, IsWildcard = true }, GroupPartitionKey.All);
        var explicitSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { first }, IsWildcard = false }, GroupPartitionKey.All);
        Assert.Equal(3, fallbackSelection.Lessons.Count);
        Assert.Equal(2, explicitSelection.Lessons.Count);

        var fallbackLink = MakeLink(new LessonGroups { first, second }, GroupPartitionKey.All, isWildcard: true, isCombined: false, "https://registry.test/broad");
        var explicitLink = ExplicitParent(first, GroupPartitionKey.All, "https://registry.test/explicit");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: fallbackLink, Selection: fallbackSelection),
            (Group: explicitLink, Selection: explicitSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics: null);
        AnyLessonId lecture = default;
        foreach (var id in fallbackSelection.Lessons)
        {
            if (schedule.Get(id).Lesson.Type == LessonType.Curs)
            {
                lecture = id;
            }
        }
        Assert.True(routed[explicitLink.Uri].ContainsKey(lecture));
        Assert.True(routed[fallbackLink.Uri].ContainsKey(lecture));
        foreach (var entry in routed[explicitLink.Uri])
        {
            if (schedule.Get(entry.Key).Lesson.Type == LessonType.Lab)
            {
                Assert.True(entry.Value.Contains(first));
            }
        }
        foreach (var entry in routed[fallbackLink.Uri])
        {
            if (schedule.Get(entry.Key).Lesson.Type == LessonType.Lab)
            {
                Assert.True(entry.Value.Contains(second));
            }
        }
        Assert.Equal(2, routed[explicitLink.Uri].Count);
        Assert.Equal(2, routed[fallbackLink.Uri].Count);
    }

    [Fact]
    public void EqualSpecificityAmbiguousDestinationsProduceDiagnosticsRatherThanArbitraryAssignment()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Curs);
            lesson.DayOfWeek(DayOfWeek.Tuesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var firstSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder, schedule, course,
            new FoundGroups { Value = new LessonGroups { parent }, IsWildcard = false }, GroupPartitionKey.All);
        Assert.Equal(2, firstSelection.Lessons.Count);
        Assert.Equal(2, secondSelection.Lessons.Count);

        var firstLink = ExplicitParent(parent, GroupPartitionKey.All, "https://registry.test/duplicate-one");
        var secondLink = ExplicitParent(parent, GroupPartitionKey.All, "https://registry.test/duplicate-two");
        List<(GroupLink Group, RegistryLessonSelection Selection)> destinations =
        [
            (Group: firstLink, Selection: firstSelection),
            (Group: secondLink, Selection: secondSelection),
        ];
        var diagnostics = new RegistrySyncDiagnostics();
        var routed = RegistryLessonRouting.Prioritize(schedule, destinations, diagnostics);
        AnyLessonId lab = default;
        AnyLessonId lecture = default;
        foreach (var id in firstSelection.Lessons)
        {
            var lesson = schedule.Get(id).Lesson;
            if (lesson.Type == LessonType.Lab)
            {
                lab = id;
            }
            else
            {
                lecture = id;
            }
        }
        Assert.False(routed[firstLink.Uri].ContainsKey(lab));
        Assert.False(routed[secondLink.Uri].ContainsKey(lab));
        Assert.True(routed[firstLink.Uri].ContainsKey(lecture));
        Assert.True(routed[secondLink.Uri].ContainsKey(lecture));
        var diagnostic = Assert.Single(diagnostics.Items.Where((RegistrySyncDiagnostic item) => item.Kind == "ambiguous-registry-destination"));
        Assert.Equal("Synthetic course", diagnostic.Course);
        Assert.Equal("IA2601", diagnostic.Group);
    }
}
