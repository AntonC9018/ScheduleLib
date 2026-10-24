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
        string path1 = Path.GetTempPath();
        string toString = Guid.NewGuid().ToString("N");
        string text = $"topic-interleaved-{toString}";
        string path2 = (string) text;
        var directory = Path.Combine(path1, path2);
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
            global::ScheduleLib.FilteredSchedule schedule2 = schedule.Filter(new());
            global::Microsoft.Extensions.Logging.ILogger<global::ScheduleLib.Application.Core.Topics.AllLessonTopicsDatabaseBuilder> logger = NullLogger<AllLessonTopicsDatabaseBuilder>.Instance;
            var builder = new AllLessonTopicsDatabaseBuilder(schedule2, logger);
            await builder.AddFromManifest(new(manifest, directory), lookup, default);
            var topics = builder.Build();

            AttendanceLookupKey Key(GroupId group, LessonType type)
            {
                return new(
                    new FoundGroups
                    {
                        Value = [group],
                        IsWildcard = false
                    },
                    GroupPartitionKey.All,
                    course,
                    type,
                    0,
                    default);
            }

            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = Key(russian, LessonType.Lab);
                return key;
            }

            Assert.Equal(expected: "Russian lab", actual: topics.Get(CalculateKey()));
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = Key(english, LessonType.Lab);
                return key2;
            }

            Assert.Equal(expected: "English lab", actual: topics.Get(CalculateKey2()));
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey3()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key3 = Key(russian, LessonType.Curs);
                return key3;
            }

            Assert.Null(topics.Get(CalculateKey3()));
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey4()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key4 = Key(english, LessonType.Curs);
                return key4;
            }

            Assert.Null(topics.Get(CalculateKey4()));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task InterleavedSeriesPreserveTypesLanguagesPartitionsAndSequenceOrder()
    {
        string path1 = Path.GetTempPath();
        string toString = Guid.NewGuid().ToString("N");
        string text = $"topic-series-{toString}";
        string path2 = (string) text;
        var directory = Path.Combine(path1, path2);
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
            global::ScheduleLib.GroupId group2 = russian;
            global::ScheduleLib.LessonType type2 = LessonType.Lab;
            global::ScheduleLib.SubGroup subgroup2 = new SubGroup("I");
            AddLesson(group2, type2, subgroup2);
            AddLesson(english, LessonType.Lab, SubGroup.All);
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
            global::ScheduleLib.FilteredSchedule schedule2 = schedule.Filter(new());
            global::Microsoft.Extensions.Logging.ILogger<global::ScheduleLib.Application.Core.Topics.AllLessonTopicsDatabaseBuilder> logger = NullLogger<AllLessonTopicsDatabaseBuilder>.Instance;
            var builder = new AllLessonTopicsDatabaseBuilder(schedule2, logger);
            await builder.AddFromManifest(new(manifest, directory), lookup, default);
            var topics = builder.Build();
            Check(russian, LessonType.Curs, GroupPartitionKey.All, first: "Lecture one", second: "Lecture two");
            global::ScheduleLib.GroupId group3 = russian;
            global::ScheduleLib.LessonType type3 = LessonType.Lab;
            global::ScheduleLib.SubGroup subGroup = new SubGroup("I");
            global::ScheduleLib.Specialization specialization = Specialization.All;
            global::ScheduleLib.GroupPartitionKey partition2 = new(subGroup, specialization);
            string first2 = "Russian lab one";
            string second2 = "Russian lab two";
            Check(group3, type3, partition2, first: first2, second: second2);
            Check(english, LessonType.Lab, GroupPartitionKey.All, first: "English lab one", second: "English lab two");
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = Key(russian, LessonType.Lab, GroupPartitionKey.All, 0);
                return key;
            }

            Assert.Null(topics.Get(CalculateKey()));
            global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = Key(english, LessonType.Curs, GroupPartitionKey.All, 0);
                return key2;
            }

            Assert.Null(topics.Get(CalculateKey2()));

            AttendanceLookupKey Key(GroupId group, LessonType type, GroupPartitionKey partition, int index)
            {
                return new(new FoundGroups
                {
                    Value = [group],
                    IsWildcard = false
                }, partition, course, type, index, default);
            }

            void Check(GroupId group, LessonType type, GroupPartitionKey partition, string first, string second)
            {
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey()
                {
                    global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key = Key(group, type, partition, 0);
                    return key;
                }

                Assert.Equal(expected: first, actual: topics.Get(CalculateKey()));
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey2()
                {
                    global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key2 = Key(group, type, partition, 1);
                    return key2;
                }

                Assert.Equal(expected: second, actual: topics.Get(CalculateKey2()));
                global::ScheduleLib.OnlineRegistry.AttendanceLookupKey CalculateKey3()
                {
                    global::ScheduleLib.OnlineRegistry.AttendanceLookupKey key3 = Key(group, type, partition, 2);
                    return key3;
                }

                Assert.Null(topics.Get(CalculateKey3()));
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
