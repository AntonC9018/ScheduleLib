using Microsoft.Extensions.Logging.Abstractions;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;
using System.Text;

namespace ScheduleLib.Topics.Tests;

public sealed class ScopedManifestTests
{
    [Theory]
    [InlineData(1,
        true,
        false,
        false)]
    [InlineData(2,
        false,
        false,
        false)]
    [InlineData(null,
        false,
        false,
        false)]
    [InlineData(1,
        true,
        true,
        false)]
    [InlineData(1,
        true,
        false,
        true)]
    public async Task ImportedTopicsStayWithinCourseGroupPartitionAndSemester(
        int? semester,
        bool enabled,
        bool unspecifiedLanguage,
        bool allowMissingLessonTypes)
    {
        using var temp = new TestTempDirectory("scoped-topics");
        var directory = temp.Path;
        try
        {
            await File.WriteAllTextAsync(
                path: Path.Combine(directory, "topics.csv"),
                contents: "name\nSynthetic topic\n");
            var scheduleBuilder = new ScheduleBuilder();
            scheduleBuilder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            scheduleBuilder.EnableLookupModule();
            var course = scheduleBuilder.Course("Synthetic course");
            var otherCourse = scheduleBuilder.Course("Other course");
            var group = scheduleBuilder.Group("IA2601");
            var otherGroup = scheduleBuilder.Group("IA2602");
            foreach (var currentGroup in new[]
                     {
                         group,
                         otherGroup,
                     })
            {
                scheduleBuilder.RegularLesson(lesson =>
                {
                    lesson.Course(course);
                    lesson.Group(currentGroup);
                    lesson.Type(LessonType.Lab);
                    lesson.DayOfWeek(DayOfWeek.Monday);
                    lesson.TimeSlot(TimeSlot.First);
                });
            }
            if (allowMissingLessonTypes)
            {
                scheduleBuilder.RegularLesson(lesson =>
                {
                    lesson.Course(course);
                    lesson.Group(group);
                    lesson.Type(LessonType.Curs);
                    lesson.DayOfWeek(DayOfWeek.Tuesday);
                    lesson.TimeSlot(TimeSlot.First);
                });
            }
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(scheduleBuilder);
            scheduleBuilder.RefreshLookup();
            var lookup = scheduleBuilder.Lookup(unifier);
            var schedule = scheduleBuilder.Build();

            await using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
                                                       {"documents":[{"path":"topics.csv","course":"Synthetic course","groups":"IA2601",
                                                       "semester":1,"wholeGroupOnly":true,"language":"Ro","lessonType":"Lab"}]}
                                                       """));
            var manifest = await ManifestSerializer.Deserialize(input, default);
            manifest.Documents[0].AllowMissingLessonTypes = allowMissingLessonTypes;
            if (unspecifiedLanguage)
            {
                manifest.Documents[0].Language = null;
            }

            var builder =
                new AllLessonTopicsDatabaseBuilder(
                    schedule.Filter(new()),
                    NullLogger<AllLessonTopicsDatabaseBuilder>.Instance)
                {
                    Semester = semester,
                };
            await builder.AddFromManifest(
                new(manifest, directory),
                lookup,
                default);
            var topics = builder.Build();

            AttendanceLookupKey Key(
                CourseId courseId,
                GroupId groupId,
                GroupPartitionKey partition)
            {
                return new(
                    new FoundGroups
                    {
                        Value = [groupId],
                        IsWildcard = false,
                    },
                    partition,
                    courseId,
                    LessonType.Lab,
                    0,
                    default);
            }

            string? ExpectedTopic()
            {
                if (enabled)
                {
                    return "Synthetic topic";
                }
                return null;
            }

            string? expected3 = ExpectedTopic();
            string? actual2 = topics.Get(Key(
                    course,
                    group,
                    GroupPartitionKey.All));
            Assert.Equal(expected: expected3, actual: actual2);
            Assert.Null(topics.Get(Key(
                    course,
                    group,
                    GroupPartitionKey.All) with
            {
                LessonType = LessonType.Curs,
            }));

            string? objectValue = topics.Get(Key(
                    course,
                    otherGroup,
                    GroupPartitionKey.All));
            object? objectValue2 = objectValue;
            Assert.Null(objectValue2);

            string? objectValue3 = topics.Get(Key(
                    otherCourse,
                    group,
                    GroupPartitionKey.All));
            object? objectValue4 = objectValue3;
            Assert.Null(objectValue4);

            string? objectValue5 = topics.Get(Key(course, group, new GroupPartitionKey(new SubGroup("I"), Specialization.All)));
            object? objectValue6 = objectValue5;
            Assert.Null(objectValue6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
