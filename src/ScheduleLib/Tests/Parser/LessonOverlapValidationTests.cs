using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.ParserTests;

public sealed class LessonOverlapValidationTests
{
    private static ScheduleBuilder CreateBuilder(LessonOverlapValidationConfig? config = null)
    {
        var builder = new ScheduleBuilder
        {
            OverlapValidationConfig = config ?? new LessonOverlapValidationConfig(),
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
        };
        builder.EnableLookupModule();
        return builder;
    }

    private static void AddWeeklyLesson(
        ScheduleBuilder s,
        string groupName,
        string courseName = "Course",
        DayOfWeek? day = null,
        int timeSlot = 0,
        Parity? parity = null,
        string? subGroup = null,
        string? specialization = null,
        string? alternative = null)
    {
        var lesson = s.RegularLesson();
        var lessonBuilder = lesson;
        var course = s.Course(courseName);
        lessonBuilder.Course(course);
        var lessonBuilder2 = lesson;
        var group = s.Group(groupName);
        GroupId group2 = group;
        lessonBuilder2.Group(group2);
        var lessonBuilder3 = lesson;
        TimeSlot timeSlot2 = new(timeSlot);
        lessonBuilder3.TimeSlot(timeSlot2);
        lesson.DayOfWeek(day ?? DayOfWeek.Monday);
        if (parity is { } p)
        {
            lesson.Parity(p);
        }
        if (subGroup is { } sub)
        {
            var lessonBuilder4 = lesson;
            SubGroup subGroup2 = new(sub);
            lessonBuilder4.SubGroup(subGroup2);
        }
        if (specialization is { } spec)
        {
            var lessonBuilder5 = lesson;
            Specialization specialization2 = new(spec);
            lessonBuilder5.Specialization(specialization2);
        }
        if (alternative is { } alt)
        {
            var lessonBuilder6 = lesson;
            Alternative alternative2 = new(alt);
            lessonBuilder6.Alternative(alternative2);
        }
    }

    [Fact]
    public void EveryOffendingPairIsReportedInOneError()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X",
            timeSlot: 0);
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y",
            timeSlot: 0);
        AddWeeklyLesson(builder,
            groupName: "M2401",
            courseName: "P",
            day: DayOfWeek.Tuesday,
            timeSlot: 1);
        AddWeeklyLesson(builder,
            groupName: "M2401",
            courseName: "Q",
            day: DayOfWeek.Tuesday,
            timeSlot: 1);

        var error = Assert.Throws<OverlappingLessonsException>(() => builder.Build());
        Assert.Contains(expectedSubstring: "'X'", actualString: error.Message);
        Assert.Contains(expectedSubstring: "'Y'", actualString: error.Message);
        Assert.Contains(expectedSubstring: "'P'", actualString: error.Message);
        Assert.Contains(expectedSubstring: "'Q'", actualString: error.Message);
        Assert.Contains(expectedSubstring: "2 overlapping lesson pairs", actualString: error.Message);
    }

    [Fact]
    public void DisjointWeekParitiesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X",
            parity: Parity.OddWeek);
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y",
            parity: Parity.EvenWeek);

        builder.Build();
    }

    [Fact]
    public void EveryWeekIntersectsBothParities()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X",
            parity: Parity.EveryWeek);
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y",
            parity: Parity.OddWeek);

        Assert.Throws<OverlappingLessonsException>(() => builder.Build());
    }

    [Fact]
    public void DifferentSpecializationsDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "X",
            specialization: "DJ");
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "Y",
            specialization: "CV");

        builder.Build();
    }

    [Fact]
    public void DifferentSubGroupsDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "X",
            subGroup: "I");
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "Y",
            subGroup: "II");

        builder.Build();
    }

    [Fact]
    public void DifferentAlternativesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "X",
            alternative: "Antreprenoriat");
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "Y",
            alternative: "Psihologie");

        builder.Build();
    }

    [Fact]
    public void WholeGroupLessonsConflictWithSplitOnes()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "X");
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "Y",
            subGroup: "I");

        Assert.Throws<OverlappingLessonsException>(() => builder.Build());
    }

    [Fact]
    public void DisjointGroupsOrDifferentTimesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "X",
            timeSlot: 0);
        AddWeeklyLesson(builder,
            groupName: "IA2404",
            courseName: "Y",
            timeSlot: 0);
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "P",
            day: DayOfWeek.Tuesday,
            timeSlot: 0);
        AddWeeklyLesson(builder,
            groupName: "IA2403",
            courseName: "Q",
            day: DayOfWeek.Tuesday,
            timeSlot: 1);

        builder.Build();
    }

    [Fact]
    public void AllowlistedPairsAreTolerated()
    {
        var config = new LessonOverlapValidationConfig
        {
            Allowlist =
            [
                new LessonOverlapAllowlistEntry
                {
                    CourseA = "X",
                    CourseB = "Y",
                    GroupName = "IA2401",
                    Day = DayOfWeek.Monday,
                    TimeSlot = new(0),
                    Reason = "Known data bug.",
                },
            ],
        };
        var builder = CreateBuilder(config);
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X");
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y");

        builder.Build();
    }

    [Fact]
    public void AllowlistEntriesAreMatchedExactly()
    {
        var config = new LessonOverlapValidationConfig
        {
            Allowlist =
            [
                new LessonOverlapAllowlistEntry
                {
                    CourseA = "X",
                    CourseB = "Y",
                    GroupName = "IA2402",
                    Reason = "Narrow entry for another group.",
                },
            ],
        };
        var builder = CreateBuilder(config);
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X");
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y");

        Assert.Throws<OverlappingLessonsException>(() => builder.Build());
    }

    [Fact]
    public void OneTimeLessonsAreNotChecked()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X");
        var oneTime = builder.OneTimeLesson();
        var lessonBuilder = oneTime;
        var course = builder.Course("Y");
        lessonBuilder.Course(course);
        var lessonBuilder2 = oneTime;
        var group = builder.Group("IA2401");
        GroupId group2 = group;
        lessonBuilder2.Group(group2);
        var lessonBuilder3 = oneTime;
        TimeSlot timeSlot = new(0);
        lessonBuilder3.TimeSlot(timeSlot);
        var lessonBuilder4 = oneTime;
        var date = new DateOnly(year: 2026,
            month: 9,
            day: 7);
        lessonBuilder4.Date(date);

        builder.Build();
    }

    [Fact]
    public void ValidationOnlyRunsWhenConfigured()
    {
        var builder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
        };
        builder.EnableLookupModule();
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "X");
        AddWeeklyLesson(builder,
            groupName: "IA2401",
            courseName: "Y",
            timeSlot: 0);

        builder.Build();
    }
}
