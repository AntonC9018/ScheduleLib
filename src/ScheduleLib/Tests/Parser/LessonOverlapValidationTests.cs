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
        lesson.Course(s.Course(courseName));
        lesson.Group(s.Group(groupName));
        lesson.TimeSlot(new(timeSlot));
        lesson.DayOfWeek(day ?? DayOfWeek.Monday);
        if (parity is { } p)
        {
            lesson.Parity(p);
        }
        if (subGroup is { } sub)
        {
            lesson.SubGroup(new(sub));
        }
        if (specialization is { } spec)
        {
            lesson.Specialization(new(spec));
        }
        if (alternative is { } alt)
        {
            lesson.Alternative(new(alt));
        }
    }

    [Fact]
    public void EveryOffendingPairIsReportedInOneError()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2401", courseName: "X", timeSlot: 0);
        AddWeeklyLesson(builder, "IA2401", courseName: "Y", timeSlot: 0);
        AddWeeklyLesson(builder, "M2401", courseName: "P", day: DayOfWeek.Tuesday, timeSlot: 1);
        AddWeeklyLesson(builder, "M2401", courseName: "Q", day: DayOfWeek.Tuesday, timeSlot: 1);

        var error = Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Contains("'X'", error.Message);
        Assert.Contains("'Y'", error.Message);
        Assert.Contains("'P'", error.Message);
        Assert.Contains("'Q'", error.Message);
        Assert.Contains("2 overlapping lesson pairs", error.Message);
    }

    [Fact]
    public void DisjointWeekParitiesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2401", courseName: "X", parity: Parity.OddWeek);
        AddWeeklyLesson(builder, "IA2401", courseName: "Y", parity: Parity.EvenWeek);

        builder.Build();
    }

    [Fact]
    public void EveryWeekIntersectsBothParities()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2401", courseName: "X", parity: Parity.EveryWeek);
        AddWeeklyLesson(builder, "IA2401", courseName: "Y", parity: Parity.OddWeek);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void DifferentSpecializationsDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2403", courseName: "X", specialization: "DJ");
        AddWeeklyLesson(builder, "IA2403", courseName: "Y", specialization: "CV");

        builder.Build();
    }

    [Fact]
    public void DifferentSubGroupsDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2403", courseName: "X", subGroup: "I");
        AddWeeklyLesson(builder, "IA2403", courseName: "Y", subGroup: "II");

        builder.Build();
    }

    [Fact]
    public void DifferentAlternativesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2403", courseName: "X", alternative: "Antreprenoriat");
        AddWeeklyLesson(builder, "IA2403", courseName: "Y", alternative: "Psihologie");

        builder.Build();
    }

    [Fact]
    public void WholeGroupLessonsConflictWithSplitOnes()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2403", courseName: "X");
        AddWeeklyLesson(builder, "IA2403", courseName: "Y", subGroup: "I");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void DisjointGroupsOrDifferentTimesDoNotConflict()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2403", courseName: "X", timeSlot: 0);
        AddWeeklyLesson(builder, "IA2404", courseName: "Y", timeSlot: 0);
        AddWeeklyLesson(builder, "IA2403", courseName: "P", day: DayOfWeek.Tuesday, timeSlot: 0);
        AddWeeklyLesson(builder, "IA2403", courseName: "Q", day: DayOfWeek.Tuesday, timeSlot: 1);

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
        AddWeeklyLesson(builder, "IA2401", courseName: "X");
        AddWeeklyLesson(builder, "IA2401", courseName: "Y");

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
        AddWeeklyLesson(builder, "IA2401", courseName: "X");
        AddWeeklyLesson(builder, "IA2401", courseName: "Y");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void OneTimeLessonsAreNotChecked()
    {
        var builder = CreateBuilder();
        AddWeeklyLesson(builder, "IA2401", courseName: "X");
        var oneTime = builder.OneTimeLesson();
        oneTime.Course(builder.Course("Y"));
        oneTime.Group(builder.Group("IA2401"));
        oneTime.TimeSlot(new(0));
        oneTime.Date(new DateOnly(2026, 9, 7));

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
        AddWeeklyLesson(builder, "IA2401", courseName: "X");
        AddWeeklyLesson(builder, "IA2401", courseName: "Y", timeSlot: 0);

        builder.Build();
    }
}
