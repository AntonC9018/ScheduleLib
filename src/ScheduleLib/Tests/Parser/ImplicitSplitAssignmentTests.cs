using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

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
            x.Specialization("RVA", Specializations.DJ);
            x.Specialization("SAWM", Specializations.DJ);
            x.Alternative("Antreprenoriat inovativ", new("Antreprenoriat"));
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
        lesson.Course(s.Course(courseName));
        lesson.Group(s.Group(groupName));
        lesson.TimeSlot(new(0));
        lesson.DayOfWeek(DayOfWeek.Monday);
        if (specialization is { } spec)
        {
            lesson.Specialization(new(spec));
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
            names.Add(schedule.EnumerateGroups().Single(x => x.Id == groupId).Item.Name);
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
            AddLesson(s, "IA2403", courseName: "RVA");
            AddLesson(s, "IA2403", courseName: "Cloud");
        });

        Assert.Equal(
            Specializations.DJ,
            SpecializationOf(schedule, "RVA"));
        Assert.Equal(
            Specialization.All,
            SpecializationOf(schedule, "Cloud"));
    }

    [Fact]
    public void AssignsAlternativeToLessonsOfConfiguredCourses()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2403", courseName: "Antreprenoriat inovativ");
        });

        Assert.Equal(
            new Alternative("Antreprenoriat"),
            AlternativeOf(schedule, "Antreprenoriat inovativ"));
    }

    [Fact]
    public void SplitsSharedLessonsWhenOnlySomeGroupsAreCovered()
    {
        var builder = CreateBuilder();
        AddLesson(builder, "IA2403", courseName: "RVA");
        var shared = builder.RegularLesson();
        shared.Course(builder.Course("SAWM"));
        shared.Groups([builder.Group("IA2403"), builder.Group("M2403")]);
        shared.TimeSlot(new(0));
        shared.DayOfWeek(DayOfWeek.Monday);

        var schedule = builder.Build();

        Assert.Equal(Specializations.DJ, SpecializationOf(schedule, "RVA"));

        // The covered groups get the specialization; the rest keep the lesson without
        // one, so the lesson is split in two, both parts keeping the shared time.
        var sawm = LessonsOf(schedule, "SAWM");
        Assert.Equal(2, sawm.Count);
        var stamped = sawm.Single(x => x.Lesson.Specialization == Specializations.DJ);
        var unstamped = sawm.Single(x => x.Lesson.Specialization == Specialization.All);
        Assert.Equal(["IA2403"], GroupNamesOf(stamped, schedule));
        Assert.Equal(["M2403"], GroupNamesOf(unstamped, schedule));
        Assert.Equal(stamped.Lesson.Room, unstamped.Lesson.Room);
        Assert.Equal(
            stamped.Weekly!.Value.Date.DayOfWeek,
            unstamped.Weekly!.Value.Date.DayOfWeek);
        Assert.Equal(
            stamped.Weekly!.Value.Date.TimeSlot,
            unstamped.Weekly!.Value.Date.TimeSlot);
    }

    [Fact]
    public void AssignsSharedLessonWhenEveryGroupMatchesAScopeWithTheSameValue()
    {
        var configBuilder = new ImplicitSplitConfigBuilder();
        configBuilder.Scope(x =>
        {
            x.Grade = new(3);
            x.Faculty = new("IA");
            x.Specialization("Realitate virtuală și augmentată", Specializations.DJ);
        });
        configBuilder.Scope(x =>
        {
            x.Grade = new(3);
            x.Faculty = new("M");
            x.Specialization("Realitate virtuală și augmentată", Specializations.DJ);
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
        lesson.Course(builder.Course("Realitate virtuală și augmentată"));
        lesson.Groups([builder.Group("IA2403"), builder.Group("M2403")]);
        lesson.TimeSlot(new(0));
        lesson.DayOfWeek(DayOfWeek.Monday);

        var schedule = builder.Build();

        Assert.Equal(
            Specializations.DJ,
            SpecializationOf(schedule, "Realitate virtuală și augmentată"));
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
            x.Specialization("RVA", Specializations.DJ);
        });
        builder.ImplicitSplitConfig = scopeBuilder.Build();
        AddLesson(builder, "IA2403", courseName: "RVA");

        var schedule = builder.Build();

        Assert.Equal(
            Specialization.All,
            SpecializationOf(schedule, "RVA"));
    }

    [Fact]
    public void ExplicitSpecializationDifferentFromTheConfiguredOneThrows()
    {
        var builder = CreateBuilder();
        AddLesson(
            builder,
            "IA2403",
            courseName: "RVA",
            specialization: "CV");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void ExplicitSpecializationMatchingTheConfiguredOneIsKept()
    {
        var builder = CreateBuilder();
        AddLesson(
            builder,
            "IA2403",
            courseName: "RVA",
            specialization: "DJ");

        var schedule = builder.Build();

        Assert.Equal(
            Specializations.DJ,
            SpecializationOf(schedule, "RVA"));
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

        var permitted = SpecializationRegistryHelper.CreateDefault().PermittedFor(in group);

        Assert.True(new[] { Specializations.DJ, Specializations.DezvoltareaAplicatiilor }
            .SequenceEqual(permitted));
    }

    [Fact]
    public void CacheHashDescriptionIgnoresDeclarationOrder()
    {
        var a = BuildConfigA();
        var b = BuildConfigB();
        var c = BuildConfigC();

        Assert.Equal(a.DescribeForCacheHash(), b.DescribeForCacheHash());
        Assert.NotEqual(a.DescribeForCacheHash(), c.DescribeForCacheHash());
    }

    private static ImplicitSplitConfig BuildConfigA()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("A", Specializations.DJ);
            x.Specialization("B", Specializations.UI);
        });
        builder.Scope(x =>
        {
            x.Grade = new(2);
            x.Alternative("C", new("Alt"));
        });
        return builder.Build();
    }

    private static ImplicitSplitConfig BuildConfigB()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(2);
            x.Alternative("C", new("Alt"));
        });
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("B", Specializations.UI);
            x.Specialization("A", Specializations.DJ);
        });
        return builder.Build();
    }

    private static ImplicitSplitConfig BuildConfigC()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("A", Specializations.GA2D);
        });
        return builder.Build();
    }
}
