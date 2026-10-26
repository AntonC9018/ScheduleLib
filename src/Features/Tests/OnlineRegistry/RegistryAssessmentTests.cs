using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry.Impl;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryAssessmentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LineEndingNormalizationDoesNotCauseRepeatedTopicUpdates(bool changedText)
    {
        var lesson = Lessons()[0] with
        {
            Topic = "First line\nSecond line",
        };
        var lesson2 = lesson;
        string topic;
        if (changedText)
        {
            topic = "First line\r\nChanged line";
        }
        else
        {
            topic = "First line\r\nSecond line";
        }

        var saved = Remote(lesson2, topic);
        var schedule = new ScheduleBuilder().Build();
        var anyDayDerivation = new AnyDayDerivation();
        GetLessonEquationCommandsParams p = new(schedule,
            [saved],
            [lesson]);
        var deriveCommands = anyDayDerivation.DeriveCommands(p);
        var commands = deriveCommands.ToArray();
        if (changedText)
        {
            Assert.Equal(expected: LessonEquationCommandType.Update, actual: Assert.Single(commands).Type);
        }
        else
        {
            Assert.Empty(commands);
        }
    }

    [Theory]
    [InlineData("Atestare 1", true)]
    [InlineData("Atestarea a II-a", true)]
    [InlineData("Atestări", true)]
    [InlineData("Unit testing", false)]
    [InlineData("Implementarea unui test", false)]
    [InlineData("", false)]
    public void AssessmentLabelsAreRecognized(string topic, bool expected)
    {
        bool expected2 = expected;
        bool actual2 = RegistryAssessmentTopics.IsAssessment(topic);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void SharedDestinationUsesIndependentParentTopicSequences()
    {
        var lessons = Lessons()
            .Select((lesson, index) => lesson with
            {
                LessonId = new WeeklyLessonId(index).AsAny(),
            })
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            [],
            [],
            Topic,
            lesson => [new GroupId(lesson.LessonId.Id % 2)]);
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Topic 1",
                "Topic 2",
                "Topic 2",
            },
            actual: result.Local.Select(lesson => lesson.Topic));
    }

    [Fact]
    public void AssessmentCompactsOnlyItsParentSequence()
    {
        var lessons = Lessons()
            .Select((lesson, index) => lesson with
            {
                LessonId = new WeeklyLessonId(index).AsAny(),
            })
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            [Remote(lessons[1], "Atestare 1")],
            [],
            Topic,
            lesson => [new GroupId(lesson.LessonId.Id % 2)]);
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Atestare 1",
                "Topic 2",
                "Topic 1",
            },
            actual: result.Local.Select(lesson => lesson.Topic));
        Assert.Equal(expected: lessons.Select(lesson => lesson.Attendance),
            actual: result.Local.Select(lesson => lesson.Attendance));
    }

    [Fact]
    public void SharedAudienceOrderDoesNotSplitItsTopicSequence()
    {
        var lessons = Lessons()
            .Select((lesson, index) => lesson with
            {
                LessonId = new WeeklyLessonId(index).AsAny(),
            })
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            [],
            [],
            Topic,
            lesson => lesson.LessonId.Id switch
            {
                0 => [new GroupId(1), new GroupId(0)],
                1 => [new GroupId(0), new GroupId(1)],
                2 => [new GroupId(0)],
                _ => [new GroupId(1)],
            });
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Topic 2",
                "Topic 1",
                "Topic 1",
            },
            actual: result.Local.Select(lesson => lesson.Topic));
    }

    [Fact]
    public void ExistingRegistryFallbackRetainsDestinationOrderAcrossParents()
    {
        var lessons = Lessons()
            .Select((lesson, index) => lesson with
            {
                LessonId = new WeeklyLessonId(index).AsAny(),
            })
            .ToArray();

        RemoteLessonInstance ProjectValue(LessonInstance lesson, int index)
        {
            var operationValue = lesson;
            string operationValue2;
            if (index == 0)
            {
                operationValue2 = "Atestare 1";
            }
            else
            {
                operationValue2 = "Existing topic " + index;
            }

            string operationValue3 = operationValue2;
            var operationValue4 = Remote(operationValue, operationValue3);
            return operationValue4;
        }

        var remote = lessons.Select(ProjectValue)
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            remote,
            [Rule(lessons[3])],
            (_, _) => null,
            lesson => [new GroupId(lesson.LessonId.Id % 2)]);
        Assert.Equal(expected: new[]
            {
                "Existing topic 1",
                "Existing topic 2",
                "Existing topic 3",
                "Atestare 1",
            },
            actual: result.Local.Select(lesson => lesson.Topic));
    }

    [Fact]
    public void ExistingAssessmentDoesNotConsumeTopicOrAlterAttendance()
    {
        var lessons = Lessons();
        var assessment = Remote(lessons[1], "Atestare 1");
        var result = RegistryAssessments.Arrange(lessons,
            [assessment],
            [],
            Topic);
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Atestare 1",
                "Topic 2",
                "Topic 3",
            },
            actual: result.Local.Select(x => x.Topic));
        Assert.Equal(expected: lessons.Select(x => x.Attendance), actual: result.Local.Select(x => x.Attendance));
        var rerun = RegistryAssessments.Arrange(result.Local.ToArray(),
            result.Local.Select(x => Remote(x, x.Topic!))
                .ToArray(),
            [],
            Topic);
        Assert.Equal(expected: result.Local, actual: rerun.Local);
    }

    [Fact]
    public void MultipleConfiguredDatesMoveOnlyAssessmentTopics()
    {
        var lessons = Lessons();
        var result = RegistryAssessments.Arrange(lessons,
            [Remote(lessons[0], "Atestare 1"), Remote(lessons[1], "Atestare 2")],
            [Rule(lessons[2], lessons[3])],
            Topic);
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Topic 2",
                "Atestare 1",
                "Atestare 2",
            },
            actual: result.Local.Select(x => x.Topic));
        Assert.Equal(expected: lessons.Select(x => x.DateTime), actual: result.Local.Select(x => x.DateTime));
        var rerun = RegistryAssessments.Arrange(result.Local.ToArray(),
            result.Local.Select(x => Remote(x, x.Topic!))
                .ToArray(),
            [Rule(lessons[2], lessons[3])],
            Topic);
        Assert.Equal(expected: result.Local, actual: rerun.Local);
    }

    [Fact]
    public void ExistingAssessmentOutsideTimetableIsProtectedFromDeletion()
    {
        var lessons = Lessons();
        var remote = Remote(lessons[0] with
        {
            DateTime = lessons[0].DateTime.AddDays(-7),
        },
            "Atestare");
        var result = RegistryAssessments.Arrange(lessons,
            [remote],
            [],
            Topic);
        Assert.Empty(result.Remote);
        Assert.Equal(expected: "Topic 1", actual: result.Local[0].Topic);
    }

    [Fact]
    public void InvalidConfiguredDateStopsBeforeProducingCommands()
    {
        var lessons = Lessons();
        Assert.Throws<InvalidOperationException>(() => RegistryAssessments.Arrange(lessons,
            [],
            [
                Rule(lessons[0] with
                {
                    DateTime = lessons[0].DateTime.AddDays(1),
                }),
            ],
            Topic));
        Assert.Throws<InvalidOperationException>(() => RegistryAssessments.Arrange(lessons,
            [],
            [Rule(lessons[0], lessons[0])],
            Topic));
    }

    [Fact]
    public void OtherLessonTypesKeepTheirOwnTopicSequence()
    {
        var lessons = Lessons();
        lessons[2] = lessons[2] with
        {
            RegistryLessonType = LessonType.Lab,
        };
        var result = RegistryAssessments.Arrange(lessons,
            [Remote(lessons[1], "Atestare")],
            [],
            Topic);
        Assert.Equal(expected: new[]
            {
                "Topic 1",
                "Atestare",
                "Topic 1",
                "Topic 2",
            },
            actual: result.Local.Select(x => x.Topic));
    }

    [Fact]
    public void LaterConfigurationReplacesDatesRatherThanRetainingOldDates()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<List<RegistryAssessmentRule>>>();
        var lessons = Lessons();
        var result = merger.Merge(@from: [Rule(lessons[2], lessons[3])], @into: [Rule(lessons[0], lessons[1])]);
        var rule = Rule(lessons[2], lessons[3]);
        var expected2 = rule.Dates;
        var single = Assert.Single(result);
        var actual2 = single.Dates;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void ApplyingMovedTopicsProducesAnIdempotentEquationPlan()
    {
        var lessons = Lessons();

        RemoteLessonInstance ProjectValue(LessonInstance lesson, int index)
        {
            var operationValue = lesson;
            string operationValue2;
            if (index < 2)
            {
                string operationValue3 = "Atestare ";
                int operationValue4 = index + 1;
                operationValue2 = operationValue3 + operationValue4;
            }
            else
            {
                operationValue2 = "Synthetic old topic";
            }

            string operationValue5 = operationValue2;
            var operationValue6 = Remote(operationValue, operationValue5);
            return operationValue6;
        }

        var remote = lessons.Select(ProjectValue)
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            remote,
            [Rule(lessons[2], lessons[3])],
            Topic);
        var schedule = new ScheduleBuilder().Build();
        var derivation = new SameDayDerivation();
        var sameDayDerivation = derivation;
        GetLessonEquationCommandsParams p = new(schedule,
            result.Remote,
            result.Local);
        var deriveCommands = sameDayDerivation.DeriveCommands(p);
        var commands = deriveCommands.ToArray();
        Assert.Equal(expected: 4, actual: commands.Length);
        Assert.All(commands, command => Assert.Equal(expected: LessonEquationCommandType.Update, actual: command.Type));
        var rerun = RegistryAssessments.Arrange(Lessons(),
            result.Local.Select(lesson => Remote(lesson, lesson.Topic!))
                .ToArray(),
            [Rule(lessons[2], lessons[3])],
            Topic);
        var sameDayDerivation2 = derivation;
        GetLessonEquationCommandsParams p2 = new(schedule,
            rerun.Remote,
            rerun.Local);
        var collection = sameDayDerivation2.DeriveCommands(p2);
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Fact]
    public void RulesDoNotLeakIntoOtherCoursesGroupsOrPartitions()
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext =
            Parsing.GroupParser.GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
        var course = builder.Course("Synthetic course");
        var otherCourse = builder.Course("Other course");
        var group = builder.Group("IA2601");
        var otherGroup = builder.Group("IA2602");
        var schedule = builder.Build();
        var rules = new[]
        {
            Rule(Lessons()[0]),
        };
        var collection = RegistryAssessments.Find(rules,
            schedule,
            course,
            [group],
            GroupPartitionKey.All);
        IEnumerable<RegistryAssessmentRule> collection2 = collection;
        Assert.Single(collection2);
        var collection3 = RegistryAssessments.Find(rules,
            schedule,
            otherCourse,
            [group],
            GroupPartitionKey.All);
        System.Collections.IEnumerable collection4 = collection3;
        Assert.Empty(collection4);
        var collection5 = RegistryAssessments.Find(rules,
            schedule,
            course,
            [otherGroup],
            GroupPartitionKey.All);
        System.Collections.IEnumerable collection6 = collection5;
        Assert.Empty(collection6);

        GroupPartitionKey CalculatePartition()
        {
            var subGroup = new SubGroup("I");
            var specialization = Specialization.All;
            GroupPartitionKey partition = new(subGroup, specialization);
            return partition;
        }

        var collection9 = RegistryAssessments.Find(rules,
            schedule,
            course,
            [group],
            CalculatePartition());
        System.Collections.IEnumerable collection10 = collection9;
        Assert.Empty(collection10);
        var alternativeRule = Rule(Lessons()[0]);
        alternativeRule.Alternative = "Synthetic elective";

        GroupPartitionKey CalculatePartition2()
        {
            var subGroup2 = SubGroup.All;
            var specialization2 = Specialization.All;
            var alternative = new Alternative("Synthetic elective");
            GroupPartitionKey partition2 = new(subGroup2,
                specialization2,
                alternative);
            return partition2;
        }

        var collection11 = RegistryAssessments.Find([alternativeRule],
            schedule,
            course,
            [group],
            CalculatePartition2());
        IEnumerable<RegistryAssessmentRule> collection12 = collection11;
        Assert.Single(collection12);
        var collection7 = RegistryAssessments.Find([alternativeRule],
            schedule,
            course,
            [group],
            GroupPartitionKey.All);
        System.Collections.IEnumerable collection8 = collection7;
        Assert.Empty(collection8);
    }

    [Fact]
    public void ConfiguredMoveWithoutImportedTopicsFlowsExistingRegistryTopics()
    {
        var lessons = Lessons();

        RemoteLessonInstance ProjectValue(LessonInstance lesson, int index)
        {
            var operationValue = lesson;
            string operationValue2;
            if (index == 0)
            {
                operationValue2 = "Atestare 1";
            }
            else
            {
                operationValue2 = "Existing topic " + index;
            }

            string operationValue3 = operationValue2;
            var operationValue4 = Remote(operationValue, operationValue3);
            return operationValue4;
        }

        var remote = lessons.Select(ProjectValue)
            .ToArray();
        var result = RegistryAssessments.Arrange(lessons,
            remote,
            [Rule(lessons[3])],
            (_, _) => null);
        Assert.Equal(expected: new[]
            {
                "Existing topic 1",
                "Existing topic 2",
                "Existing topic 3",
                "Atestare 1",
            },
            actual: result.Local.Select(x => x.Topic));
    }

    [Fact]
    public void AssessmentWithMissingTypeUsesItsExactScheduledTime()
    {
        var lessons = Lessons();
        lessons[2] = lessons[2] with
        {
            DateTime = lessons[1].DateTime.AddHours(2),
            RegistryLessonType = LessonType.Lab,
        };
        var remote = Remote(lessons[1], "Atestare 1") with
        {
            LessonType = LessonType.Unspecified,
        };
        var result = RegistryAssessments.Arrange(lessons,
            [remote],
            [],
            Topic);
        Assert.Equal(expected: "Atestare 1", actual: result.Local[1].Topic);
        Assert.Equal(expected: "Topic 1", actual: result.Local[2].Topic);
    }

    private static string Topic(LessonInstance lesson, int index) => "Topic " + (index + 1);

    private static RegistryAssessmentRule Rule(params LessonInstance[] lessons)
    {
        return new()
        {
            Course = "Synthetic course",
            Group = "IA2601",
            LessonType = LessonType.Curs,
            Dates = lessons.Select(x => DateOnly.FromDateTime(x.DateTime))
                .ToList(),
        };
    }

    private static LessonInstance[] Lessons()
    {
        return Enumerable.Range(start: 0, count: 4)
            .Select(index => new LessonInstance
            {
                LessonId = default,
                DateTime = new DateTime(year: 2026,
                    month: 9,
                    day: 7,
                    hour: 9,
                    minute: 45,
                    second: 0).AddDays(index * 7),
                Topic = null,
                Attendance = [Attendance.Present],
                RegistryLessonType = LessonType.Curs,
            })
            .ToArray();
    }

    private static RemoteLessonInstance Remote(LessonInstance lesson, string topic)
    {
        return new()
        {
            DateTime = lesson.DateTime,
            Topic = topic,
            LessonType = lesson.RegistryLessonType,
            Attendance = lesson.Attendance!,
            EditUri = new("https://example.invalid/edit"),
            ViewUri = new("https://example.invalid/view"),
        };
    }
}
