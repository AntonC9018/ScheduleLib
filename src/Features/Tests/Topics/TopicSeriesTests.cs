using Microsoft.Extensions.Logging.Abstractions;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;
using System.Text;

namespace ScheduleLib.Topics.Tests;

public sealed class TopicSeriesTests
{
    [Fact]
    public async Task ManifestTypeAndLanguageFilterInterleavedCsvRows()
    {
        using var temp = new TestTempDirectory("topic-interleaved");
        var directory = temp.Path;
        try
        {
            await File.WriteAllTextAsync(
                path: Path.Combine(directory, "course.csv"),
                contents: """
                          name,lessonType,language
                          Russian lecture,Curs,Ru
                          Russian lab,Lab,Ru
                          English lab,Lab,En
                          """);
            var scheduleBuilder = new ScheduleBuilder();
            scheduleBuilder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            scheduleBuilder.EnableLookupModule();
            var course = scheduleBuilder.Course("Synthetic course");
            var russian = scheduleBuilder.Group("IA2601");
            russian.Ref.Language = Language.Ru;
            var english = scheduleBuilder.Group("IA2602");
            english.Ref.Language = Language.En;
            foreach (var group in new[]
                     {
                         russian,
                         english,
                     })
            {
                foreach (var type in new[]
                         {
                             LessonType.Curs,
                             LessonType.Lab,
                         })
                {
                    scheduleBuilder.RegularLesson(lesson =>
                    {
                        lesson.Course(course);
                        lesson.Group(group);
                        lesson.Type(type);
                        lesson.DayOfWeek(DayOfWeek.Monday);
                        lesson.TimeSlot(TimeSlot.First);
                    });
                }
            }
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(scheduleBuilder);
            scheduleBuilder.RefreshLookup();
            var lookup = scheduleBuilder.Lookup(unifier);
            var schedule = scheduleBuilder.Build();

            byte[] CalculateBuffer()
            {
                byte[] buffer = Encoding.UTF8.GetBytes("""
                                                       {"documents":[
                                                         {"path":"course.csv","course":"Synthetic course","lessonType":"Lab","language":"Ru","allowMissingLessonTypes":true},
                                                         {"path":"course.csv","course":"Synthetic course","lessonType":"Lab","language":"En","allowMissingLessonTypes":true}
                                                       ]}
                                                       """);
                return buffer;
            }

            await using var input = new MemoryStream(CalculateBuffer());
            var manifest = await ManifestSerializer.Deserialize(input, default);
            var schedule2 = schedule.Filter(new());
            Microsoft.Extensions.Logging.ILogger<AllLessonTopicsDatabaseBuilder> logger =
                NullLogger<AllLessonTopicsDatabaseBuilder>.Instance;
            var builder = new AllLessonTopicsDatabaseBuilder(schedule2, logger);
            await builder.AddFromManifest(
                new(manifest, directory),
                lookup,
                default);
            var topics = builder.Build();

            AttendanceLookupKey Key(GroupId group, LessonType type)
            {
                return new(
                    new FoundGroups
                    {
                        Value = [group],
                        IsWildcard = false,
                    },
                    GroupPartitionKey.All,
                    course,
                    type,
                    0,
                    default);
            }

            AttendanceLookupKey CalculateKey()
            {
                var key = Key(russian, LessonType.Lab);
                return key;
            }

            string expected2 = "Russian lab";
            string? actual2 = topics.Get(CalculateKey());
            Assert.Equal(expected: expected2, actual: actual2);

            AttendanceLookupKey CalculateKey2()
            {
                var key2 = Key(english, LessonType.Lab);
                return key2;
            }

            string expected3 = "English lab";
            string? actual3 = topics.Get(CalculateKey2());
            Assert.Equal(expected: expected3, actual: actual3);

            AttendanceLookupKey CalculateKey3()
            {
                var key3 = Key(russian, LessonType.Curs);
                return key3;
            }

            string? objectValue = topics.Get(CalculateKey3());
            object? objectValue2 = objectValue;
            Assert.Null(objectValue2);

            AttendanceLookupKey CalculateKey4()
            {
                var key4 = Key(english, LessonType.Curs);
                return key4;
            }

            string? objectValue3 = topics.Get(CalculateKey4());
            object? objectValue4 = objectValue3;
            Assert.Null(objectValue4);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InterleavedSeriesPreserveTypesLanguagesPartitionsAndSequenceOrder()
    {
        using var temp = new TestTempDirectory("topic-series");
        var directory = temp.Path;
        try
        {
            await File.WriteAllTextAsync(
                path: Path.Combine(directory, "course.csv"),
                contents: """
                          name,lessonType,language,series
                          Lecture one,Curs,Ru,curs-ru
                          Russian lab one,Lab,Ru,lab-ru
                          English lab one,Lab,En,lab-en
                          Lecture two,Curs,Ru,curs-ru
                          Russian lab two,Lab,Ru,lab-ru
                          English lab two,Lab,En,lab-en
                          Unselected topic,Lab,Ru,unused
                          """);
            var scheduleBuilder = new ScheduleBuilder();
            scheduleBuilder.GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            });
            scheduleBuilder.EnableLookupModule();
            var course = scheduleBuilder.Course("Synthetic course");
            var russian = scheduleBuilder.Group("IA2601");
            russian.Ref.Language = Language.Ru;
            var english = scheduleBuilder.Group("IA2602");
            english.Ref.Language = Language.En;
            AddLesson(
                russian,
                LessonType.Curs,
                SubGroup.All);
            GroupId group2 = russian;
            var type2 = LessonType.Lab;
            var subgroup2 = new SubGroup("I");
            AddLesson(
                group2,
                type2,
                subgroup2);
            AddLesson(
                english,
                LessonType.Lab,
                SubGroup.All);
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(scheduleBuilder);
            scheduleBuilder.RefreshLookup();
            var lookup = scheduleBuilder.Lookup(unifier);
            var schedule = scheduleBuilder.Build();

            byte[] CalculateBuffer()
            {
                byte[] buffer = Encoding.UTF8.GetBytes("""
                                                       {"documents":[
                                                         {"path":"course.csv","series":"curs-ru","course":"Synthetic course","groups":"IA2601",
                                                          "wholeGroupOnly":true,"lessonType":"Curs","language":"Ru","allowMissingLessonTypes":true},
                                                         {"path":"course.csv","series":"lab-ru","course":"Synthetic course","groups":"IA2601",
                                                          "subGroup":"I","lessonType":"Lab","language":"Ru","allowMissingLessonTypes":true},
                                                         {"path":"course.csv","series":"lab-en","course":"Synthetic course","groups":"IA2602",
                                                          "wholeGroupOnly":true,"lessonType":"Lab","language":"En","allowMissingLessonTypes":true}
                                                       ]}
                                                       """);
                return buffer;
            }

            await using var input = new MemoryStream(CalculateBuffer());
            var manifest = await ManifestSerializer.Deserialize(input, default);
            var schedule2 = schedule.Filter(new());
            Microsoft.Extensions.Logging.ILogger<AllLessonTopicsDatabaseBuilder> logger =
                NullLogger<AllLessonTopicsDatabaseBuilder>.Instance;
            var builder = new AllLessonTopicsDatabaseBuilder(schedule2, logger);
            await builder.AddFromManifest(
                new(manifest, directory),
                lookup,
                default);
            var topics = builder.Build();
            Check(
                russian,
                LessonType.Curs,
                GroupPartitionKey.All,
                first: "Lecture one",
                second: "Lecture two");
            GroupId group3 = russian;
            var type3 = LessonType.Lab;
            var subGroup = new SubGroup("I");
            var specialization = Specialization.All;
            GroupPartitionKey partition2 = new(subGroup, specialization);
            string first2 = "Russian lab one";
            string second2 = "Russian lab two";
            Check(
                group3,
                type3,
                partition2,
                first: first2,
                second: second2);
            Check(
                english,
                LessonType.Lab,
                GroupPartitionKey.All,
                first: "English lab one",
                second: "English lab two");

            AttendanceLookupKey CalculateKey()
            {
                var key = Key(
                    russian,
                    LessonType.Lab,
                    GroupPartitionKey.All,
                    0);
                return key;
            }

            string? objectValue = topics.Get(CalculateKey());
            object? objectValue2 = objectValue;
            Assert.Null(objectValue2);

            AttendanceLookupKey CalculateKey2()
            {
                var key2 = Key(
                    english,
                    LessonType.Curs,
                    GroupPartitionKey.All,
                    0);
                return key2;
            }

            string? objectValue3 = topics.Get(CalculateKey2());
            object? objectValue4 = objectValue3;
            Assert.Null(objectValue4);

            AttendanceLookupKey Key(
                GroupId group,
                LessonType type,
                GroupPartitionKey partition,
                int index)
            {
                return new(
                    new FoundGroups
                    {
                        Value = [group],
                        IsWildcard = false,
                    },
                    partition,
                    course,
                    type,
                    index,
                    default);
            }

            void Check(
                GroupId group,
                LessonType type,
                GroupPartitionKey partition,
                string first,
                string second)
            {
                AttendanceLookupKey CalculateKey()
                {
                    var key = Key(
                        group,
                        type,
                        partition,
                        0);
                    return key;
                }

                string? actual2 = topics.Get(CalculateKey());
                Assert.Equal(expected: first, actual: actual2);

                AttendanceLookupKey CalculateKey2()
                {
                    var key2 = Key(
                        group,
                        type,
                        partition,
                        1);
                    return key2;
                }

                string? actual3 = topics.Get(CalculateKey2());
                Assert.Equal(expected: second, actual: actual3);

                AttendanceLookupKey CalculateKey3()
                {
                    var key3 = Key(
                        group,
                        type,
                        partition,
                        2);
                    return key3;
                }

                string? objectValue = topics.Get(CalculateKey3());
                object? objectValue2 = objectValue;
                Assert.Null(objectValue2);
            }

            void AddLesson(
                GroupId group,
                LessonType type,
                SubGroup subgroup)
            {
                scheduleBuilder.RegularLesson(lesson =>
                {
                    lesson.Course(course);
                    lesson.Group(group);
                    lesson.Type(type);
                    lesson.SubGroup(subgroup);
                    lesson.DayOfWeek(DayOfWeek.Monday);
                    lesson.TimeSlot(TimeSlot.First);
                });
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
