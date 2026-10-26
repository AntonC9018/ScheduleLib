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
        string path1 = Path.GetTempPath();
        string toString = Guid.NewGuid()
            .ToString("N");
        string text = $"scoped-topics-{toString}";
        string path2 = text;
        var directory = Path.Combine(path1, path2);
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path: Path.Combine(directory, "topics.csv"),
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

            byte[] CalculateBuffer()
            {
                byte[] buffer = Encoding.UTF8.GetBytes("""
                                                       {"documents":[{"path":"topics.csv","course":"Synthetic course","groups":"IA2601",
                                                       "semester":1,"wholeGroupOnly":true,"language":"Ro","lessonType":"Lab"}]}
                                                       """);
                return buffer;
            }

            await using var input = new MemoryStream(CalculateBuffer());
            var manifest = await ManifestSerializer.Deserialize(input, default);
            manifest.Documents[0].AllowMissingLessonTypes = allowMissingLessonTypes;
            if (unspecifiedLanguage)
            {
                manifest.Documents[0].Language = null;
            }

            var builder =
                new AllLessonTopicsDatabaseBuilder(schedule.Filter(new()),
                    NullLogger<AllLessonTopicsDatabaseBuilder>.Instance)
                {
                    Semester = semester,
                };
            await builder.AddFromManifest(new(manifest, directory),
                lookup,
                default);
            var topics = builder.Build();

            AttendanceLookupKey Key(
                CourseId courseId,
                GroupId groupId,
                GroupPartitionKey partition)
            {
                return new(new FoundGroups
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

            AttendanceLookupKey CalculateKey()
            {
                var key = Key(course,
                    group,
                    GroupPartitionKey.All);
                return key;
            }

            string? CalculateExpected()
            {
                string? expected2;
                if (enabled)
                {
                    expected2 = "Synthetic topic";
                }
                else
                {
                    expected2 = null;
                }

                return expected2;
            }

            string? expected3 = CalculateExpected();
            string? actual2 = topics.Get(CalculateKey());
            Assert.Equal(expected: expected3, actual: actual2);
            Assert.Null(topics.Get(Key(course,
                    group,
                    GroupPartitionKey.All) with
            {
                LessonType = LessonType.Curs,
            }));

            AttendanceLookupKey CalculateKey2()
            {
                var key2 = Key(course,
                    otherGroup,
                    GroupPartitionKey.All);
                return key2;
            }

            string? objectValue = topics.Get(CalculateKey2());
            object? objectValue2 = objectValue;
            Assert.Null(objectValue2);

            AttendanceLookupKey CalculateKey3()
            {
                var key3 = Key(otherCourse,
                    group,
                    GroupPartitionKey.All);
                return key3;
            }

            string? objectValue3 = topics.Get(CalculateKey3());
            object? objectValue4 = objectValue3;
            Assert.Null(objectValue4);

            AttendanceLookupKey CalculateKey4()
            {
                var courseId2 = course;
                GroupId groupId2 = group;
                var subGroup = new SubGroup("I");
                var specialization = Specialization.All;
                GroupPartitionKey partition2 = new(subGroup, specialization);
                var key4 = Key(courseId2,
                    groupId2,
                    partition2);
                return key4;
            }

            string? objectValue5 = topics.Get(CalculateKey4());
            object? objectValue6 = objectValue5;
            Assert.Null(objectValue6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
