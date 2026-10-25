using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class ImplicitSplitAssignmentTests
{
    private static ScheduleBuilder CreateBuilder()
    {
        var builder = new ScheduleBuilder
        {
            ImplicitSplitConfig = Config,
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
        };
        builder.EnableLookupModule();
        return builder;
    }

    // The context carries the study year, the config declares it in full.
    private static ImplicitSplitConfig Config { get; } = CreateConfig();

    private static ImplicitSplitConfig CreateConfig()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.StudyYear = new(2026);
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.AttendanceMode = AttendanceMode.Zi;
            x.Qualification = QualificationType.Licenta;
            x.Specialization("RVA", Specialization.DJ);
            x.Specialization("SAWM", Specialization.DJ);
            global::ScheduleLib.ImplicitSplitScopeBuilder implicitSplitScopeBuilder = x;
            string course = "Antreprenoriat inovativ";
            global::ScheduleLib.Alternative alternative = new("Antreprenoriat");
            implicitSplitScopeBuilder.Alternative(course, alternative);
        });
        return builder.Build();
    }

    private static Schedule Build(Action<ScheduleBuilder> configure)
    {
        var s = CreateBuilder();
        configure(s);
        return s.Build();
    }

    private static void AddLesson(
        ScheduleBuilder s,
        string groupName,
        string courseName = "Course",
        string? specialization = null)
    {
        var lesson = s.RegularLesson();
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder = lesson;
        global::ScheduleLib.CourseId course = s.Course(courseName);
        lessonBuilder.Course(course);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder2 = lesson;
        global::ScheduleLib.Builders.GroupBuilder group = s.Group(groupName);
        global::ScheduleLib.GroupId group2 = group;
        lessonBuilder2.Group(group2);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder3 = lesson;
        global::TimeSlot timeSlot = new(0);
        lessonBuilder3.TimeSlot(timeSlot);
        lesson.DayOfWeek(DayOfWeek.Monday);
        if (specialization is { } spec)
        {
            global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder4 = lesson;
            global::ScheduleLib.Specialization specialization2 = new(spec);
            lessonBuilder4.Specialization(specialization2);
        }
    }

    private static Specialization SpecializationOf(Schedule schedule, string courseName)
    {
        return schedule.EnumerateAllLessons()
            .Single(x => schedule.Get(x.Lesson.Course).FullName == courseName)
            .Lesson.Specialization;
    }

    private static List<AnyLessonAccessor> LessonsOf(Schedule schedule, string courseName)
    {
        return schedule.EnumerateAllLessons()
            .Where(x => schedule.Get(x.Lesson.Course).FullName == courseName)
            .ToList();
    }

    private static List<string> GroupNamesOf(AnyLessonAccessor lesson, Schedule schedule)
    {
        var names = new List<string>();
        foreach (var groupId in lesson.Lesson.Groups)
        {
            names.Add(schedule.EnumerateGroups()
                .Single(x => x.Id == groupId).Item.Name);
        }
        return names;
    }

    private static Alternative AlternativeOf(Schedule schedule, string courseName)
    {
        return schedule.EnumerateAllLessons()
            .Single(x => schedule.Get(x.Lesson.Course).FullName == courseName)
            .Lesson.Alternative;
    }

    [Fact]
    public void AssignsSpecializationToLessonsOfConfiguredCourses()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2403", courseName: "RVA");
            AddLesson(s, groupName: "IA2403", courseName: "Cloud");
        });

        global::ScheduleLib.Specialization expected2 = Specialization.DJ;
        global::ScheduleLib.Specialization actual2 = SpecializationOf(schedule, "RVA");
        Assert.Equal(
            expected: expected2, actual: actual2);
        global::ScheduleLib.Specialization expected3 = Specialization.All;
        global::ScheduleLib.Specialization actual3 = SpecializationOf(schedule, "Cloud");
        Assert.Equal(
            expected: expected3, actual: actual3);
    }

    [Fact]
    public void AssignsAlternativeToLessonsOfConfiguredCourses()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2403", courseName: "Antreprenoriat inovativ");
        });

        global::ScheduleLib.Alternative expected2 = new Alternative("Antreprenoriat");
        global::ScheduleLib.Alternative actual2 = AlternativeOf(schedule, "Antreprenoriat inovativ");
        Assert.Equal(
            expected: expected2, actual: actual2);
    }

    [Fact]
    public void SplitsSharedLessonsWhenOnlySomeGroupsAreCovered()
    {
        var builder = CreateBuilder();
        AddLesson(builder, groupName: "IA2403", courseName: "RVA");
        var shared = builder.RegularLesson();
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder = shared;
        global::ScheduleLib.CourseId course = builder.Course("SAWM");
        lessonBuilder.Course(course);
        shared.Groups([builder.Group("IA2403"), builder.Group("M2403")]);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder2 = shared;
        global::TimeSlot timeSlot = new(0);
        lessonBuilder2.TimeSlot(timeSlot);
        shared.DayOfWeek(DayOfWeek.Monday);

        var schedule = builder.Build();

        global::ScheduleLib.Specialization expected2 = Specialization.DJ;
        global::ScheduleLib.Specialization actual2 = SpecializationOf(schedule, "RVA");
        Assert.Equal(expected: expected2, actual: actual2);

        // The covered groups get the specialization; the rest keep the lesson without
        // one, so the lesson is split in two, both parts keeping the shared time.
        var sawm = LessonsOf(schedule, "SAWM");
        Assert.Equal(expected: 2, actual: sawm.Count);
        var stamped = sawm.Single(x => x.Lesson.Specialization == Specialization.DJ);
        var unstamped = sawm.Single(x => x.Lesson.Specialization == Specialization.All);
        global::System.Collections.Generic.List<string> expected3 = ["IA2403"];
        global::System.Collections.Generic.List<string> actual3 = GroupNamesOf(stamped, schedule);
        Assert.Equal(expected: expected3, actual: actual3);
        global::System.Collections.Generic.List<string> expected4 = ["M2403"];
        global::System.Collections.Generic.List<string> actual4 = GroupNamesOf(unstamped, schedule);
        Assert.Equal(expected: expected4, actual: actual4);
        Assert.Equal(expected: stamped.Lesson.Room, actual: unstamped.Lesson.Room);
        Assert.Equal(
            expected: stamped.Weekly!.Value.Date.DayOfWeek,
            actual: unstamped.Weekly!.Value.Date.DayOfWeek);
        Assert.Equal(
            expected: stamped.Weekly!.Value.Date.TimeSlot,
            actual: unstamped.Weekly!.Value.Date.TimeSlot);
    }

    [Fact]
    public void AssignsSharedLessonWhenEveryGroupMatchesAScopeWithTheSameValue()
    {
        var configBuilder = new ImplicitSplitConfigBuilder();
        configBuilder.Scope(x =>
        {
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.Specialization("Realitate virtuală și augmentată", Specialization.DJ);
        });
        configBuilder.Scope(x =>
        {
            x.Grade = new(3);
            x.Faculty = new("M");
            x.Specialization("Realitate virtuală și augmentată", Specialization.DJ);
        });
        var config = configBuilder.Build();
        var builder = new ScheduleBuilder
        {
            ImplicitSplitConfig = config,
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = new(2026),
            }),
        };
        builder.EnableLookupModule();
        var lesson = builder.RegularLesson();
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder = lesson;
        global::ScheduleLib.CourseId course = builder.Course("Realitate virtuală și augmentată");
        lessonBuilder.Course(course);
        lesson.Groups([builder.Group("IA2403"), builder.Group("M2403")]);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder2 = lesson;
        global::TimeSlot timeSlot = new(0);
        lessonBuilder2.TimeSlot(timeSlot);
        lesson.DayOfWeek(DayOfWeek.Monday);

        var schedule = builder.Build();

        global::ScheduleLib.Specialization expected2 = Specialization.DJ;
        global::ScheduleLib.Specialization actual2 = SpecializationOf(schedule, "Realitate virtuală și augmentată");
        Assert.Equal(
            expected: expected2, actual: actual2);
    }

    [Fact]
    public void StudyYearOutsideOfTheScopeLeavesLessonsUntouched()
    {
        var builder = CreateBuilder();
        // Built for the 2026 study year; the scope only applies to 2025.
        var scopeBuilder = new ImplicitSplitConfigBuilder();
        scopeBuilder.Scope(x =>
        {
            x.StudyYear = new(2025);
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.Specialization("RVA", Specialization.DJ);
        });
        builder.ImplicitSplitConfig = scopeBuilder.Build();
        AddLesson(builder, groupName: "IA2403", courseName: "RVA");

        var schedule = builder.Build();

        global::ScheduleLib.Specialization expected2 = Specialization.All;
        global::ScheduleLib.Specialization actual2 = SpecializationOf(schedule, "RVA");
        Assert.Equal(
            expected: expected2, actual: actual2);
    }

    [Fact]
    public void WallClockDerivedStudyYearMatchingNoPinnedScopeThrows()
    {
        // No explicit study year, so ParseGroup defaults it from the wall clock,
        // while the only scope is pinned to 2000. Building must fail loudly
        // instead of silently skipping the assignment.
        var scopeBuilder = new ImplicitSplitConfigBuilder();
        scopeBuilder.Scope(x =>
        {
            x.StudyYear = new(2000);
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.Specialization("RVA", Specialization.DJ);
        });
        var builder = new ScheduleBuilder
        {
            ImplicitSplitConfig = scopeBuilder.Build(),
        };
        builder.EnableLookupModule();
        AddLesson(builder, groupName: "IA2403", courseName: "RVA");

        Assert.Throws<MissingImplicitSplitStudyYearException>(() => builder.Build());
    }

    [Fact]
    public void ExplicitSpecializationDifferentFromTheConfiguredOneThrows()
    {
        var builder = CreateBuilder();
        AddLesson(
            builder,
            groupName: "IA2403",
            courseName: "RVA",
            specialization: "CV");

        Assert.Throws<ConflictingImplicitAssignmentException>(() => builder.Build());
    }

    [Fact]
    public void ExplicitSpecializationMatchingTheConfiguredOneIsKept()
    {
        var builder = CreateBuilder();
        AddLesson(
            builder,
            groupName: "IA2403",
            courseName: "RVA",
            specialization: "DJ");

        var schedule = builder.Build();

        global::ScheduleLib.Specialization expected2 = Specialization.DJ;
        global::ScheduleLib.Specialization actual2 = SpecializationOf(schedule, "RVA");
        Assert.Equal(
            expected: expected2, actual: actual2);
    }

    [Fact]
    public void RegistryPermitsTheGrade3TrackValues()
    {
        var group = new Group
        {
            Name = "IA2403",
            Grade = new(3),
            GroupNumber = 3,
            QualificationType = QualificationType.Licenta,
            Faculty = new("IA"),
            AttendanceMode = AttendanceMode.Zi,
            Language = Language.Ru,
        };

        var permitted = ScheduleDefaults.Config.SpecializationRegistry.PermittedFor(in group);

        Assert.True(new[] { Specialization.DJ, Specialization.DezvoltareaAplicatiilor }
            .SequenceEqual(permitted));
    }
}
