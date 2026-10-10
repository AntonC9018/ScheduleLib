using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry.Impl;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryAssessmentTests
{
    [Theory]
    [InlineData("Atestare 1", true)]
    [InlineData("Atestarea a II-a", true)]
    [InlineData("Atestări", true)]
    [InlineData("Unit testing", false)]
    [InlineData("Implementarea unui test", false)]
    [InlineData("", false)]
    public void AssessmentLabelsAreRecognized(string topic, bool expected) =>
        Assert.Equal(expected, RegistryAssessmentTopics.IsAssessment(topic));

    [Fact]
    public void ExistingAssessmentDoesNotConsumeTopicOrAlterAttendance()
    {
        var lessons = Lessons();
        var assessment = Remote(lessons[1], "Atestare 1");
        var result = RegistryAssessments.Arrange(lessons, [assessment], [], Topic);
        Assert.Equal(new[] { "Topic 1", "Atestare 1", "Topic 2", "Topic 3" }, result.Local.Select(x => x.Topic));
        Assert.Equal(lessons.Select(x => x.Attendance), result.Local.Select(x => x.Attendance));
        var rerun = RegistryAssessments.Arrange(result.Local.ToArray(), result.Local.Select(x => Remote(x, x.Topic!)).ToArray(), [], Topic);
        Assert.Equal(result.Local, rerun.Local);
    }

    [Fact]
    public void MultipleConfiguredDatesMoveOnlyAssessmentTopics()
    {
        var lessons = Lessons();
        var result = RegistryAssessments.Arrange(lessons,
            [Remote(lessons[0], "Atestare 1"), Remote(lessons[1], "Atestare 2")],
            [Rule(lessons[2], lessons[3])], Topic);
        Assert.Equal(new[] { "Topic 1", "Topic 2", "Atestare 1", "Atestare 2" }, result.Local.Select(x => x.Topic));
        Assert.Equal(lessons.Select(x => x.DateTime), result.Local.Select(x => x.DateTime));
        var rerun = RegistryAssessments.Arrange(result.Local.ToArray(), result.Local.Select(x => Remote(x, x.Topic!)).ToArray(),
            [Rule(lessons[2], lessons[3])], Topic);
        Assert.Equal(result.Local, rerun.Local);
    }

    [Fact]
    public void ExistingAssessmentOutsideTimetableIsProtectedFromDeletion()
    {
        var lessons = Lessons();
        var remote = Remote(lessons[0] with { DateTime = lessons[0].DateTime.AddDays(-7) }, "Atestare");
        var result = RegistryAssessments.Arrange(lessons, [remote], [], Topic);
        Assert.Empty(result.Remote);
        Assert.Equal("Topic 1", result.Local[0].Topic);
    }

    [Fact]
    public void InvalidConfiguredDateStopsBeforeProducingCommands()
    {
        var lessons = Lessons();
        Assert.Throws<InvalidOperationException>(() => RegistryAssessments.Arrange(lessons, [],
            [Rule(lessons[0] with { DateTime = lessons[0].DateTime.AddDays(1) })], Topic));
        Assert.Throws<InvalidOperationException>(() => RegistryAssessments.Arrange(lessons, [],
            [Rule(lessons[0], lessons[0])], Topic));
    }

    [Fact]
    public void OtherLessonTypesKeepTheirOwnTopicSequence()
    {
        var lessons = Lessons();
        lessons[2] = lessons[2] with { RegistryLessonType = LessonType.Lab };
        var result = RegistryAssessments.Arrange(lessons, [Remote(lessons[1], "Atestare")], [], Topic);
        Assert.Equal(new[] { "Topic 1", "Atestare", "Topic 1", "Topic 2" }, result.Local.Select(x => x.Topic));
    }

    [Fact]
    public void LaterConfigurationReplacesDatesRatherThanRetainingOldDates()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<List<RegistryAssessmentRule>>>();
        var lessons = Lessons();
        var result = merger.Merge([Rule(lessons[2], lessons[3])], [Rule(lessons[0], lessons[1])]);
        Assert.Equal(Rule(lessons[2], lessons[3]).Dates, Assert.Single(result).Dates);
    }

    [Fact]
    public void ApplyingMovedTopicsProducesAnIdempotentEquationPlan()
    {
        var lessons = Lessons();
        var remote = lessons.Select((lesson, index) => Remote(lesson, index < 2 ? "Atestare " + (index + 1) : "Synthetic old topic")).ToArray();
        var result = RegistryAssessments.Arrange(lessons, remote, [Rule(lessons[2], lessons[3])], Topic);
        var schedule = new ScheduleBuilder().Build();
        var derivation = new SameDayDerivation();
        var commands = derivation.DeriveCommands(new(schedule, result.Remote, result.Local)).ToArray();
        Assert.Equal(4, commands.Length);
        Assert.All(commands, command => Assert.Equal(LessonEquationCommandType.Update, command.Type));
        var rerun = RegistryAssessments.Arrange(Lessons(), result.Local.Select(lesson => Remote(lesson, lesson.Topic!)).ToArray(),
            [Rule(lessons[2], lessons[3])], Topic);
        Assert.Empty(derivation.DeriveCommands(new(schedule, rerun.Remote, rerun.Local)));
    }

    [Fact]
    public void RulesDoNotLeakIntoOtherCoursesGroupsOrPartitions()
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = ScheduleLib.Parsing.GroupParser.GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var course = builder.Course("Synthetic course");
        var otherCourse = builder.Course("Other course");
        var group = builder.Group("IA2601");
        var otherGroup = builder.Group("IA2602");
        var schedule = builder.Build();
        var rules = new[] { Rule(Lessons()[0]) };
        Assert.Single(RegistryAssessments.Find(rules, schedule, course, [group], GroupPartitionKey.All));
        Assert.Empty(RegistryAssessments.Find(rules, schedule, otherCourse, [group], GroupPartitionKey.All));
        Assert.Empty(RegistryAssessments.Find(rules, schedule, course, [otherGroup], GroupPartitionKey.All));
        Assert.Empty(RegistryAssessments.Find(rules, schedule, course, [group], new(new SubGroup("I"), Specialization.All)));
        var alternativeRule = Rule(Lessons()[0]);
        alternativeRule.Alternative = "Synthetic elective";
        Assert.Single(RegistryAssessments.Find([alternativeRule], schedule, course, [group],
            new(SubGroup.All, Specialization.All, new Alternative("Synthetic elective"))));
        Assert.Empty(RegistryAssessments.Find([alternativeRule], schedule, course, [group], GroupPartitionKey.All));
    }

    [Fact]
    public void ConfiguredMoveWithoutImportedTopicsFlowsExistingRegistryTopics()
    {
        var lessons = Lessons();
        var remote = lessons.Select((lesson, index) => Remote(lesson, index == 0 ? "Atestare 1" : "Existing topic " + index)).ToArray();
        var result = RegistryAssessments.Arrange(lessons, remote, [Rule(lessons[3])], (_, _) => null);
        Assert.Equal(new[] { "Existing topic 1", "Existing topic 2", "Existing topic 3", "Atestare 1" }, result.Local.Select(x => x.Topic));
    }

    [Fact]
    public void AssessmentWithMissingTypeUsesItsExactScheduledTime()
    {
        var lessons = Lessons();
        lessons[2] = lessons[2] with { DateTime = lessons[1].DateTime.AddHours(2), RegistryLessonType = LessonType.Lab };
        var remote = Remote(lessons[1], "Atestare 1") with { LessonType = LessonType.Unspecified };
        var result = RegistryAssessments.Arrange(lessons, [remote], [], Topic);
        Assert.Equal("Atestare 1", result.Local[1].Topic);
        Assert.Equal("Topic 1", result.Local[2].Topic);
    }

    private static string Topic(LessonInstance lesson, int index) => "Topic " + (index + 1);
    private static RegistryAssessmentRule Rule(params LessonInstance[] lessons) => new()
    {
        Course = "Synthetic course", Group = "IA2601", LessonType = LessonType.Curs,
        Dates = lessons.Select(x => DateOnly.FromDateTime(x.DateTime)).ToList(),
    };
    private static LessonInstance[] Lessons() => Enumerable.Range(0, 4).Select(index => new LessonInstance
    {
        LessonId = default, DateTime = new DateTime(2026, 9, 7, 9, 45, 0).AddDays(index * 7),
        Topic = null, Attendance = [Attendance.Present], RegistryLessonType = LessonType.Curs,
    }).ToArray();
    private static RemoteLessonInstance Remote(LessonInstance lesson, string topic) => new()
    {
        DateTime = lesson.DateTime, Topic = topic, LessonType = lesson.RegistryLessonType,
        Attendance = lesson.Attendance!, EditUri = new("https://example.invalid/edit"), ViewUri = new("https://example.invalid/view"),
    };
}
