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
        var directory = Path.Combine(Path.GetTempPath(), (string) $"topic-interleaved-{Guid.NewGuid().ToString("N")}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path: Path.Combine(directory, "course.csv"), contents: """
                name,lessonType,language
                Russian lecture,Curs,Ru
                Russian lab,Lab,Ru
                English lab,Lab,En
                """);
            var scheduleBuilder = new ScheduleBuilder();
            scheduleBuilder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            scheduleBuilder.EnableLookupModule();
            var course = scheduleBuilder.Course("Synthetic course");
            var russian = scheduleBuilder.Group("IA2601");
            russian.Ref.Language = Language.Ru;
            var english = scheduleBuilder.Group("IA2602");
            english.Ref.Language = Language.En;
            foreach (var group in new[] { russian, english })
            {
                foreach (var type in new[] { LessonType.Curs, LessonType.Lab })
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
            await using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
                {"documents":[
                  {"path":"course.csv","course":"Synthetic course","lessonType":"Lab","language":"Ru","allowMissingLessonTypes":true},
                  {"path":"course.csv","course":"Synthetic course","lessonType":"Lab","language":"En","allowMissingLessonTypes":true}
                ]}
                """));
            var manifest = await ManifestSerializer.Deserialize(input, default);
            var builder = new AllLessonTopicsDatabaseBuilder(schedule.Filter(new()), NullLogger<AllLessonTopicsDatabaseBuilder>.Instance);
            await builder.AddFromManifest(new(manifest, directory), lookup, default);
            var topics = builder.Build();

            AttendanceLookupKey Key(GroupId group, LessonType type)
            {
                return new(
                    new FoundGroups {
                        Value = [group],
                        IsWildcard = false
                    },
                    GroupPartitionKey.All,
                    course,
                    type,
                    0,
                    default);
            }

            Assert.Equal(expected: "Russian lab", actual: topics.Get(Key(russian, LessonType.Lab)));
            Assert.Equal(expected: "English lab", actual: topics.Get(Key(english, LessonType.Lab)));
            Assert.Null(topics.Get(Key(russian, LessonType.Curs)));
            Assert.Null(topics.Get(Key(english, LessonType.Curs)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InterleavedSeriesPreserveTypesLanguagesPartitionsAndSequenceOrder()
    {
        var directory = Path.Combine(Path.GetTempPath(), (string) $"topic-series-{Guid.NewGuid().ToString("N")}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(path: Path.Combine(directory, "course.csv"), contents: """
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
            scheduleBuilder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
            scheduleBuilder.EnableLookupModule();
            var course = scheduleBuilder.Course("Synthetic course");
            var russian = scheduleBuilder.Group("IA2601");
            russian.Ref.Language = Language.Ru;
            var english = scheduleBuilder.Group("IA2602");
            english.Ref.Language = Language.En;
            AddLesson(russian, LessonType.Curs, SubGroup.All);
            AddLesson(russian, LessonType.Lab, new SubGroup("I"));
            AddLesson(english, LessonType.Lab, SubGroup.All);
            var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
            unifier.Refresh(scheduleBuilder);
            scheduleBuilder.RefreshLookup();
            var lookup = scheduleBuilder.Lookup(unifier);
            var schedule = scheduleBuilder.Build();
            await using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
                {"documents":[
                  {"path":"course.csv","series":"curs-ru","course":"Synthetic course","groups":"IA2601",
                   "wholeGroupOnly":true,"lessonType":"Curs","language":"Ru","allowMissingLessonTypes":true},
                  {"path":"course.csv","series":"lab-ru","course":"Synthetic course","groups":"IA2601",
                   "subGroup":"I","lessonType":"Lab","language":"Ru","allowMissingLessonTypes":true},
                  {"path":"course.csv","series":"lab-en","course":"Synthetic course","groups":"IA2602",
                   "wholeGroupOnly":true,"lessonType":"Lab","language":"En","allowMissingLessonTypes":true}
                ]}
                """));
            var manifest = await ManifestSerializer.Deserialize(input, default);
            var builder = new AllLessonTopicsDatabaseBuilder(schedule.Filter(new()), NullLogger<AllLessonTopicsDatabaseBuilder>.Instance);
            await builder.AddFromManifest(new(manifest, directory), lookup, default);
            var topics = builder.Build();
            Check(russian, LessonType.Curs, GroupPartitionKey.All, first: "Lecture one", second: "Lecture two");
            Check(russian, LessonType.Lab, new(new SubGroup("I"), Specialization.All), first: "Russian lab one", second: "Russian lab two");
            Check(english, LessonType.Lab, GroupPartitionKey.All, first: "English lab one", second: "English lab two");
            Assert.Null(topics.Get(Key(russian, LessonType.Lab, GroupPartitionKey.All, 0)));
            Assert.Null(topics.Get(Key(english, LessonType.Curs, GroupPartitionKey.All, 0)));

            AttendanceLookupKey Key(GroupId group, LessonType type, GroupPartitionKey partition, int index) =>
                new(new FoundGroups {
                    Value = [group],
                    IsWildcard = false
                }, partition, course, type, index, default);

            void Check(GroupId group, LessonType type, GroupPartitionKey partition, string first, string second)
            {
                Assert.Equal(expected: first, actual: topics.Get(Key(group, type, partition, 0)));
                Assert.Equal(expected: second, actual: topics.Get(Key(group, type, partition, 1)));
                Assert.Null(topics.Get(Key(group, type, partition, 2)));
            }

            void AddLesson(GroupId group, LessonType type, SubGroup subgroup)
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
