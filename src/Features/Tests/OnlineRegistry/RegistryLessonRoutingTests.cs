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
        foreach (var number in new[]
                 {
                     1,
                     2,
                 })
        {
            builder.RegularLesson(lesson =>
            {
                lesson.Course(course);
                var lessonBuilder = lesson;
                var group = parent;
                var subGroup = SubGroup.CreateNumeric(number);
                SubGroup? subGroup2 = subGroup;
                lessonBuilder.Group(group, subGroup2);
                lesson.Type(LessonType.Lab);
                var lessonBuilder2 = lesson;
                DayOfWeek dayOfWeek;
                if (number == 1)
                {
                    dayOfWeek = DayOfWeek.Monday;
                }
                else
                {
                    dayOfWeek = DayOfWeek.Tuesday;
                }

                lessonBuilder2.DayOfWeek(dayOfWeek);
                lesson.TimeSlot(TimeSlot.First);
            });
        }

        builder.RefreshLookup();
        var schedule = builder.Build();
        var config = EmptyConfig();
        config.LabMerges =
        [
            new()
            {
                Course = "Synthetic course",
                Group = "IA2601",
                SourceSubGroup = "I",
            },
        ];
        var fallback = MakeLink([parent],
            GroupPartitionKey.All,
            isWildcard: true,
            isCombined: false,
            "https://registry.test/fallback");
        var parent2 = parent;
        var subGroup3 = SubGroup.CreateNumeric(1);
        var specialization = Specialization.All;
        GroupPartitionKey partition = new(subGroup3, specialization);
        string uri = "https://registry.test/old-subgroup";
        var oldSubgroup = ExplicitParent(parent2,
            partition,
            uri);
        var config2 = config;
        var parameters = ParamsFor(builder,
            schedule,
            course,
            fallback.Groups,
            fallback.GroupPartition);
        var merged = RegistryLessonRouting.Candidates(config2, parameters);
        var config3 = config;
        var parameters2 = ParamsFor(builder,
            schedule,
            course,
            oldSubgroup.Groups,
            oldSubgroup.GroupPartition);
        var obsolete = RegistryLessonRouting.Candidates(config3, parameters2);
        Assert.Empty(obsolete.Lessons);
        var survivor = Assert.Single(merged.Lessons);
        Assert.Equal(expected: GroupPartitionKey.All,
            actual: RegistryLessonRouting.ProjectPartition(config,
                schedule,
                schedule.Get(survivor).Lesson));
        Assert.Equal(expected: DayOfWeek.Monday, actual: schedule.Get(survivor).Weekly!.Value.Date.DayOfWeek);
    }

    private static BuiltRegistryConfig EmptyConfig()
    {
        return new()
        {
            Credentials = new ValueCredentialsSource
            {
                Value = new Credentials
                {
                    Login = "synthetic",
                    Password = "synthetic",
                },
            },
            ExtraLessonInstanceAction = ExtraLessonInstanceAction.LeaveAlone,
            EquationCommandsDerivation = new AnyDayDerivation(),
            CommandProcessingConfig = CommandProcessingConfig.DryRun,
            LabMerges = [],
        };
    }

    private static ScheduleBuilder NewBuilder()
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2026),
        });
        builder.EnableLookupModule();
        return builder;
    }

    private static GroupLink MakeLink(
        LessonGroups groups,
        GroupPartitionKey partition,
        bool isWildcard,
        bool isCombined,
        string uri,
        bool isAdditionalCourses = false)
    {
        var found = new FoundGroups
        {
            Value = groups,
            IsWildcard = isWildcard,
            IsCombined = isCombined,
        };

        Uri CalculateUri()
        {
            var uri2 = new Uri(uri);
            return uri2;
        }

        Uri CalculateEvaluationUri()
        {
            string text = $"{uri}/evaluation";
            string uriString = text;
            var evaluationUri2 = new Uri(uriString);
            return evaluationUri2;
        }

        return new GroupLink(found,
            partition,
            uri: CalculateUri(),
            evaluationUri: CalculateEvaluationUri(),
            isAdditionalCourses);
    }

    private static GroupLink ExplicitParent(
        GroupId parent,
        GroupPartitionKey partition,
        string uri,
        bool isAdditionalCourses = false)
    {
        return MakeLink(new LessonGroups { parent },
            partition,
            isWildcard: false,
            isCombined: false,
            uri,
            isAdditionalCourses: isAdditionalCourses);
    }

    private static LessonMatchParams ParamsFor(
        ScheduleBuilder builder,
        Schedule schedule,
        CourseId course,
        FoundGroups groups,
        GroupPartitionKey partition)
    {
        var filter = new LessonSearchFilter(course,
            groups,
            partition);
        var lookup = builder.LookupModule!.LessonsByCourse;
        var schedule2 = schedule;
        LessonMatchParams lessonMatchParams = new(filter,
            lookup,
            schedule2);
        return lessonMatchParams;
    }

    private static RegistryLessonSelection CandidatesFor(
        ScheduleBuilder builder,
        Schedule schedule,
        CourseId course,
        FoundGroups groups,
        GroupPartitionKey partition)
    {
        var config = EmptyConfig();
        var parameters = ParamsFor(builder,
            schedule,
            course,
            groups,
            partition);
        var candidates = RegistryLessonRouting.Candidates(config, parameters);
        return candidates;
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
            Value = new LessonGroups
            {
                first,
                second,
            },
            IsWildcard = true,
            IsCombined = isCombined,
        };
        var selection = CandidatesFor(builder,
            schedule,
            course,
            fallbackGroups,
            GroupPartitionKey.All);
        Assert.Equal(expected: 2, actual: selection.Lessons.Count);
        Assert.Equal(expected: GroupPartitionKey.All, actual: selection.AttendancePartition);
        Assert.False(selection.IsLabMerge);

        List<RegistryLessonDestination> destinations =
        [
            new(Group: MakeLink(new LessonGroups
                    {
                        first,
                        second,
                    },
                    GroupPartitionKey.All,
                    isWildcard: true,
                    isCombined: isCombined,
                    "https://registry.test/fallback"),
                Selection: selection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        var fallbackUri = new Uri("https://registry.test/fallback");
        Assert.Equal(expected: 2, actual: routed[fallbackUri].Count);
        foreach (var entry in routed[fallbackUri])
        {
            var audience = entry.Value;
            Assert.Equal(expected: 1, actual: audience.Count);
            var lesson = schedule.Get(entry.Key).Lesson;
            Assert.True(lesson.Groups.IsSingleGroup);
            bool condition = audience.Contains(lesson.Groups[0]);
            Assert.True(condition);
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

        var fallbackSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups
                {
                    first,
                    second,
                },
                IsWildcard = true,
                IsCombined = isCombined,
            },
            GroupPartitionKey.All);
        var firstSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { first },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { second },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: 2, actual: fallbackSelection.Lessons.Count);
        Assert.Single(firstSelection.Lessons);
        Assert.Single(secondSelection.Lessons);

        var fallbackLink = MakeLink(new LessonGroups
            {
                first,
                second,
            },
            GroupPartitionKey.All,
            isWildcard: true,
            isCombined: isCombined,
            "https://registry.test/fallback");
        var firstLink = ExplicitParent(first,
            GroupPartitionKey.All,
            "https://registry.test/first");
        var secondLink = ExplicitParent(second,
            GroupPartitionKey.All,
            "https://registry.test/second");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: fallbackLink, Selection: fallbackSelection),
            new(Group: firstLink, Selection: firstSelection),
            new(Group: secondLink, Selection: secondSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        Assert.Empty(routed[fallbackLink.Uri]);
        Assert.Single(routed[firstLink.Uri]);
        Assert.Single(routed[secondLink.Uri]);
        var firstRouted = Assert.Single(routed[firstLink.Uri]);
        bool condition = firstRouted.Value.Contains(first);
        Assert.True(condition);
        var secondRouted = Assert.Single(routed[secondLink.Uri]);
        bool condition2 = secondRouted.Value.Contains(second);
        Assert.True(condition2);
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

        var broadSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups
                {
                    first,
                    second,
                },
                IsWildcard = true,
            },
            GroupPartitionKey.All);
        var explicitSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { first },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: 2, actual: broadSelection.Lessons.Count);
        Assert.Single(explicitSelection.Lessons);

        var broadLink = MakeLink(new LessonGroups
            {
                first,
                second,
            },
            GroupPartitionKey.All,
            isWildcard: true,
            isCombined: false,
            "https://registry.test/broad");
        var explicitLink = ExplicitParent(first,
            GroupPartitionKey.All,
            "https://registry.test/explicit");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: broadLink, Selection: broadSelection),
            new(Group: explicitLink, Selection: explicitSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        Assert.Single(routed[explicitLink.Uri]);
        Assert.Single(routed[broadLink.Uri]);
        var explicitEntry = Assert.Single(routed[explicitLink.Uri]);
        bool condition = explicitEntry.Value.Contains(first);
        Assert.True(condition);
        Assert.Equal(expected: first, actual: schedule.Get(explicitEntry.Key).Lesson.Groups[0]);
        var broadEntry = Assert.Single(routed[broadLink.Uri]);
        bool condition2 = broadEntry.Value.Contains(second);
        Assert.True(condition2);
        Assert.Equal(expected: second, actual: schedule.Get(broadEntry.Key).Lesson.Groups[0]);
    }

    [Fact]
    public void SubgroupListOutranksParentForMatchingNumericPartition()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        var subGroup = SubGroup.CreateNumeric(1);
        var specialization = Specialization.All;
        var subgroupPartition = new GroupPartitionKey(subGroup, specialization);
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(1);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var subgroupSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            subgroupPartition);
        var parentSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var lab = Assert.Single(subgroupSelection.Lessons);
        Assert.Contains(lab, parentSelection.Lessons);
        Assert.Equal(expected: subgroupPartition, actual: subgroupSelection.AttendancePartition);
        Assert.Equal(expected: GroupPartitionKey.All, actual: parentSelection.AttendancePartition);

        var subgroupLink = ExplicitParent(parent,
            subgroupPartition,
            "https://registry.test/subgroup");
        var parentLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/parent");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: subgroupLink, Selection: subgroupSelection),
            new(Group: parentLink, Selection: parentSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        Assert.Single(routed[subgroupLink.Uri]);
        Assert.Empty(routed[parentLink.Uri]);
        var routedEntry = Assert.Single(routed[subgroupLink.Uri]);
        Assert.Equal(expected: lab, actual: routedEntry.Key);
        bool condition = routedEntry.Value.Contains(parent);
        Assert.True(condition);
    }

    [Fact]
    public void PartialCoverageRoutesSubgroupISpecificallyAndSubgroupIIToParent()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        var subGroup = SubGroup.CreateNumeric(1);
        var specialization = Specialization.All;
        var firstPartition = new GroupPartitionKey(subGroup, specialization);
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(1);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(2);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Wednesday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var subgroupSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            firstPartition);
        var parentSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Single(subgroupSelection.Lessons);
        Assert.Equal(expected: 2, actual: parentSelection.Lessons.Count);

        var subgroupLink = ExplicitParent(parent,
            firstPartition,
            "https://registry.test/subgroup-one");
        var parentLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/parent");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: subgroupLink, Selection: subgroupSelection),
            new(Group: parentLink, Selection: parentSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        var subgroupEntry = Assert.Single(routed[subgroupLink.Uri]);
        var parentEntry = Assert.Single(routed[parentLink.Uri]);

        SubGroup CalculateExpected()
        {
            var expected2 = SubGroup.CreateNumeric(1);
            return expected2;
        }

        Assert.Equal(expected: CalculateExpected(), actual: schedule.Get(subgroupEntry.Key).Lesson.SubGroup);

        SubGroup CalculateExpected2()
        {
            var expected3 = SubGroup.CreateNumeric(2);
            return expected3;
        }

        Assert.Equal(expected: CalculateExpected2(), actual: schedule.Get(parentEntry.Key).Lesson.SubGroup);
        bool condition = subgroupEntry.Value.Contains(parent);
        Assert.True(condition);
        bool condition2 = parentEntry.Value.Contains(parent);
        Assert.True(condition2);
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

        var firstSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { first },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { second },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var firstLink = ExplicitParent(first,
            GroupPartitionKey.All,
            "https://registry.test/first");
        var secondLink = ExplicitParent(second,
            GroupPartitionKey.All,
            "https://registry.test/second");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: firstLink, Selection: firstSelection),
            new(Group: secondLink, Selection: secondSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        var firstEntry = Assert.Single(routed[firstLink.Uri]);
        var secondEntry = Assert.Single(routed[secondLink.Uri]);
        Assert.Equal(expected: 1, actual: firstEntry.Value.Count);
        Assert.Equal(expected: 1, actual: secondEntry.Value.Count);
        bool condition = firstEntry.Value.Contains(first);
        Assert.True(condition);
        bool condition2 = secondEntry.Value.Contains(second);
        Assert.True(condition2);
        Assert.NotEqual(expected: firstEntry.Key, actual: secondEntry.Key);
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
            Value = new LessonGroups
            {
                first,
                second,
            },
            IsWildcard = true,
            IsCombined = true,
        };
        var selection = CandidatesFor(builder,
            schedule,
            course,
            combinedGroups,
            GroupPartitionKey.All);
        AnyLessonId valid = default;
        AnyLessonId tainted = default;
        bool foundValid = false;
        bool foundTainted = false;
        foreach (var id in builder.LookupModule!.LessonsByCourse[course])
        {
            var lesson = schedule.Get(id).Lesson;

            bool CheckCondition()
            {
                if (lesson.Groups.Count != 2)
                {
                    return false;
                }

                if (!lesson.Groups.Contains(first))
                {
                    return false;
                }

                if (lesson.Groups.Contains(second))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                valid = id;
                foundValid = true;
            }

            bool CheckCondition2()
            {
                if (lesson.Groups.Count != 2)
                {
                    return false;
                }

                if (!lesson.Groups.Contains(first))
                {
                    return false;
                }

                if (lesson.Groups.Contains(outside))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition2())
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
            var lessonBuilder = lesson;
            var timeSlot = new TimeSlot(1);
            lessonBuilder.TimeSlot(timeSlot);
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

        var fallbackSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups
                {
                    first,
                    second,
                },
                IsWildcard = true,
            },
            GroupPartitionKey.All);
        var explicitSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { first },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: 3, actual: fallbackSelection.Lessons.Count);
        Assert.Equal(expected: 2, actual: explicitSelection.Lessons.Count);

        var fallbackLink = MakeLink(new LessonGroups
            {
                first,
                second,
            },
            GroupPartitionKey.All,
            isWildcard: true,
            isCombined: false,
            "https://registry.test/broad");
        var explicitLink = ExplicitParent(first,
            GroupPartitionKey.All,
            "https://registry.test/explicit");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: fallbackLink, Selection: fallbackSelection),
            new(Group: explicitLink, Selection: explicitSelection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        AnyLessonId lecture = default;
        foreach (var id in fallbackSelection.Lessons)
        {
            if (schedule.Get(id).Lesson.Type == LessonType.Curs)
            {
                lecture = id;
            }
        }
        bool condition = routed[explicitLink.Uri].ContainsKey(lecture);
        Assert.True(condition);
        bool condition2 = routed[fallbackLink.Uri].ContainsKey(lecture);
        Assert.True(condition2);
        foreach (var entry in routed[explicitLink.Uri])
        {
            if (schedule.Get(entry.Key).Lesson.Type == LessonType.Lab)
            {
                bool condition3 = entry.Value.Contains(first);
                Assert.True(condition3);
            }
        }
        foreach (var entry in routed[fallbackLink.Uri])
        {
            if (schedule.Get(entry.Key).Lesson.Type == LessonType.Lab)
            {
                bool condition4 = entry.Value.Contains(second);
                Assert.True(condition4);
            }
        }
        Assert.Equal(expected: 2, actual: routed[explicitLink.Uri].Count);
        Assert.Equal(expected: 2, actual: routed[fallbackLink.Uri].Count);
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

        var firstSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: 2, actual: firstSelection.Lessons.Count);
        Assert.Equal(expected: 2, actual: secondSelection.Lessons.Count);

        var firstLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/duplicate-one");
        var secondLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/duplicate-two");
        List<RegistryLessonDestination> destinations =
        [
            new(Group: firstLink, Selection: firstSelection),
            new(Group: secondLink, Selection: secondSelection),
        ];
        var diagnostics = new RegistrySyncDiagnostics();
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics);
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
        bool condition = routed[firstLink.Uri].ContainsKey(lab);
        Assert.False(condition);
        bool condition2 = routed[secondLink.Uri].ContainsKey(lab);
        Assert.False(condition2);
        bool condition3 = routed[firstLink.Uri].ContainsKey(lecture);
        Assert.True(condition3);
        bool condition4 = routed[secondLink.Uri].ContainsKey(lecture);
        Assert.True(condition4);
        var diagnostic =
            Assert.Single(diagnostics.Items.Where((item) => item.Kind == "ambiguous-registry-destination"));
        Assert.Equal(expected: "Synthetic course", actual: diagnostic.Course);
        Assert.Equal(expected: "IA2601", actual: diagnostic.Group);
    }

    [Fact]
    public void OrdinarySubgroupAndAdditionalCoursesParentReceiveSameLabIndependently()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(1);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();
        var subGroup2 = SubGroup.CreateNumeric(1);
        var specialization = Specialization.All;
        var subgroupPartition = new GroupPartitionKey(subGroup2, specialization);

        var ordinarySelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            subgroupPartition);
        var additionalSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var lab = Assert.Single(ordinarySelection.Lessons);
        Assert.Contains(lab, additionalSelection.Lessons);

        var ordinaryLink = ExplicitParent(parent,
            subgroupPartition,
            "https://registry.test/ordinary-subgroup");
        var additionalLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/additional-parent",
            isAdditionalCourses: true);
        Assert.False(ordinaryLink.IsAdditionalCourses);
        Assert.True(additionalLink.IsAdditionalCourses);
        List<RegistryLessonDestination> destinations =
        [
            new(Group: ordinaryLink, Selection: ordinarySelection),
            new(Group: additionalLink, Selection: additionalSelection),
        ];
        var diagnostics = new RegistrySyncDiagnostics();
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics);
        bool condition = routed[ordinaryLink.Uri].ContainsKey(lab);
        Assert.True(condition);
        bool condition2 = routed[additionalLink.Uri].ContainsKey(lab);
        Assert.True(condition2);
        bool condition3 = routed[ordinaryLink.Uri][lab].Contains(parent);
        Assert.True(condition3);
        bool condition4 = routed[additionalLink.Uri][lab].Contains(parent);
        Assert.True(condition4);
        Assert.Equal(expected: 0,
            actual: diagnostics.Items.Count((item) => item.Kind == "ambiguous-registry-destination"));
    }

    [Fact]
    public void OrdinaryAndAdditionalCoursesParentsWithSameSpecificityBothReceiveLab()
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
        builder.RefreshLookup();
        var schedule = builder.Build();

        var ordinarySelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var additionalSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var lab = Assert.Single(ordinarySelection.Lessons);
        Assert.Contains(lab, additionalSelection.Lessons);

        var ordinaryLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/ordinary-parent");
        var additionalLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/additional-parent",
            isAdditionalCourses: true);
        List<RegistryLessonDestination> destinations =
        [
            new(Group: ordinaryLink, Selection: ordinarySelection),
            new(Group: additionalLink, Selection: additionalSelection),
        ];
        var diagnostics = new RegistrySyncDiagnostics();
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics);
        bool condition = routed[ordinaryLink.Uri].ContainsKey(lab);
        Assert.True(condition);
        bool condition2 = routed[additionalLink.Uri].ContainsKey(lab);
        Assert.True(condition2);
        Assert.Equal(expected: 0,
            actual: diagnostics.Items.Count((item) => item.Kind == "ambiguous-registry-destination"));
    }

    [Fact]
    public void DuplicateAdditionalCoursesDestinationsRemainAmbiguousWithinVariant()
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

        var firstSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var secondSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: 2, actual: firstSelection.Lessons.Count);

        var firstLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/additional-one",
            isAdditionalCourses: true);
        var secondLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/additional-two",
            isAdditionalCourses: true);
        List<RegistryLessonDestination> destinations =
        [
            new(Group: firstLink, Selection: firstSelection),
            new(Group: secondLink, Selection: secondSelection),
        ];
        var diagnostics = new RegistrySyncDiagnostics();
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics);
        AnyLessonId lab = default;
        AnyLessonId lecture = default;
        foreach (var id in firstSelection.Lessons)
        {
            if (schedule.Get(id).Lesson.Type == LessonType.Lab)
            {
                lab = id;
            }
            else
            {
                lecture = id;
            }
        }
        bool condition = routed[firstLink.Uri].ContainsKey(lab);
        Assert.False(condition);
        bool condition2 = routed[secondLink.Uri].ContainsKey(lab);
        Assert.False(condition2);
        bool condition3 = routed[firstLink.Uri].ContainsKey(lecture);
        Assert.True(condition3);
        bool condition4 = routed[secondLink.Uri].ContainsKey(lecture);
        Assert.True(condition4);
        Assert.Single(diagnostics.Items.Where((item) => item.Kind == "ambiguous-registry-destination"));
    }

    [Fact]
    public void OnlyAdditionalCoursesListReceivesLab()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(1);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var selection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var lab = Assert.Single(selection.Lessons);
        var additionalLink = ExplicitParent(parent,
            GroupPartitionKey.All,
            "https://registry.test/only-additional",
            isAdditionalCourses: true);
        List<RegistryLessonDestination> destinations =
        [
            new(Group: additionalLink, Selection: selection),
        ];
        var routed = RegistryLessonRouting.Prioritize(schedule,
            destinations,
            diagnostics: null);
        var entry = Assert.Single(routed[additionalLink.Uri]);
        Assert.Equal(expected: lab, actual: entry.Key);
        bool condition = entry.Value.Contains(parent);
        Assert.True(condition);
    }

    [Fact]
    public void AdditionalCoursesMarkerDoesNotChangeScheduledPartitionScope()
    {
        var builder = NewBuilder();
        var course = builder.Course("Synthetic course");
        GroupId parent = builder.Group("IA2601");
        builder.RegularLesson(lesson =>
        {
            lesson.Course(course);
            lesson.Group(parent);
            lesson.Type(LessonType.Lab);
            var lessonBuilder = lesson;
            var subGroup = SubGroup.CreateNumeric(1);
            lessonBuilder.SubGroup(subGroup);
            lesson.DayOfWeek(DayOfWeek.Monday);
            lesson.TimeSlot(TimeSlot.First);
        });
        builder.RefreshLookup();
        var schedule = builder.Build();

        var ordinarySelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        var additionalSelection = CandidatesFor(builder,
            schedule,
            course,
            new FoundGroups
            {
                Value = new LessonGroups { parent },
                IsWildcard = false,
            },
            GroupPartitionKey.All);
        Assert.Equal(expected: ordinarySelection.Lessons.Count, actual: additionalSelection.Lessons.Count);
        Assert.Equal(expected: ordinarySelection.Lessons.ToHashSet(), actual: additionalSelection.Lessons.ToHashSet());
        Assert.Equal(expected: GroupPartitionKey.All, actual: ordinarySelection.AttendancePartition);
        Assert.Equal(expected: GroupPartitionKey.All, actual: additionalSelection.AttendancePartition);
        var lab = Assert.Single(ordinarySelection.Lessons);
        var lesson = schedule.Get(lab).Lesson;
        var subGroup2 = SubGroup.CreateNumeric(1);
        var specialization = Specialization.All;
        var expected2 = new GroupPartitionKey(subGroup2, specialization);
        var actual2 = RegistryLessonRouting.ProjectPartition(EmptyConfig(),
            schedule,
            lesson);
        Assert.Equal(expected: expected2, actual: actual2);
    }
}
