using System.Text;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.ParserTests;

public sealed class GroupCombinationTests
{
    private static ScheduleBuilder CreateBuilder()
    {
        return new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = 2025,
            }),
        };
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
        string? subGroup = null,
        string? specialization = null,
        string courseName = "Course")
    {
        var lesson = s.RegularLesson();
        lesson.Course(s.Course(courseName));
        lesson.Group(s.Group(groupName));
        lesson.TimeSlot(new(0));
        lesson.DayOfWeek(DayOfWeek.Monday);
        if (subGroup is { } sub)
        {
            lesson.SubGroup(new(sub));
        }
        if (specialization is { } spec)
        {
            lesson.Specialization(new(spec));
        }
    }

    private static Accessor<Group, GroupId> AddAndReturnGroup(
        Schedule schedule,
        string groupName)
    {
        return schedule.EnumerateGroups().Single(x => x.Item.Name == groupName);
    }

    private static AnyLessonAccessor GetLessonBySpecialization(
        Schedule schedule,
        string specialization)
    {
        return schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Specialization == new Specialization(specialization));
    }

    private static AnyLessonAccessor GetLessonBySubGroup(
        Schedule schedule,
        string subGroup)
    {
        return schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.SubGroup == new SubGroup(subGroup));
    }

    private static string NameOf(GroupCombination c)
    {
        var sb = new StringBuilder();
        c.AppendFileNamePart(new ListStringBuilder(sb, "-"));
        return sb.ToString();
    }

    [Fact]
    public void EnumeratesTheCartesianProductOfActivePartitions()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I", courseName: "C1");
            AddLesson(s, "IA2401", subGroup: "II");
            AddLesson(s, "IA2401", subGroup: "ro");
            AddLesson(s, "IA2401", subGroup: "ru");
            AddLesson(s, "IA2401", subGroup: "începători", courseName: "C2");
            AddLesson(s, "IA2401", courseName: "C2");
            AddLesson(s, "IA2401", specialization: "CV");
            AddLesson(s, "IA2401", specialization: "GA2D");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;

        Assert.True(info.SpecializationActive);
        Assert.Equal(16, info.Combinations.Length);
        Assert.Equal("CV-începători-ro-I", NameOf(info.Combinations[0]));
        Assert.Equal("CV-începători-ru-I", NameOf(info.Combinations[1]));
        Assert.Equal("CV-nuîncepători-ro-I", NameOf(info.Combinations[2]));
        Assert.Equal("GA2D-nuîncepători-ru-II", NameOf(info.Combinations[^1]));
    }

    [Fact]
    public void NoActiveSplitProducesNoCombinations()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;

        Assert.Empty(info.Combinations);
        Assert.False(info.SpecializationActive);
    }

    [Fact]
    public void SingletonSpecializationIsSharedForItsGroup()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", subGroup: "II");
            AddLesson(s, "IA2401", specialization: "Spring");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var springLesson = GetLessonBySpecialization(schedule, "Spring");

        Assert.False(info.SpecializationActive);
        Assert.Equal(["I", "II"], info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c => Assert.True(info.IncludesLesson(c, springLesson.Lesson)));
    }

    [Fact]
    public void ActiveSpecializationExcludesOtherSpecializationsLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", specialization: "CV");
            AddLesson(s, "IA2401", specialization: "DJ");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var djLesson = GetLessonBySpecialization(schedule, "DJ");

        Assert.Equal(["CV-I", "DJ-I"], info.Combinations.Select(NameOf));

        var cvCombination = info.Combinations.Single(x => x.Specialization == new Specialization("CV"));
        var djCombination = info.Combinations.Single(x => x.Specialization == new Specialization("DJ"));
        Assert.True(info.IncludesLesson(cvCombination, cvLesson.Lesson));
        Assert.False(info.IncludesLesson(djCombination, cvLesson.Lesson));
        Assert.True(info.IncludesLesson(djCombination, djLesson.Lesson));

        // The numeric lesson matches both combinations.
        var numericLesson = GetLessonBySubGroup(schedule, "I");
        Assert.All(info.Combinations, c => Assert.True(info.IncludesLesson(c, numericLesson.Lesson)));
    }

    [Fact]
    public void OpaqueSubGroupValuesDoNotEnterCombinations()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", subGroup: "S21");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var opaqueLesson = GetLessonBySubGroup(schedule, "S21");

        Assert.Equal(["I"], info.Combinations.Select(NameOf));
        Assert.False(info.IncludesLesson(info.Combinations[0], opaqueLesson.Lesson));
    }

    [Fact]
    public void RegistryRestrictsTheSpecializationValues()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", specialization: "CV");
            AddLesson(s, "IA2401", specialization: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401").Item;

        var b = new SpecializationRegistryBuilder();
        b.Set([Specializations.CV]).ApplyTo(x =>
        {
            x.Grade = group.Grade;
            x.Faculty = group.Faculty;
            x.AttendanceMode = group.AttendanceMode;
            x.Qualification = group.QualificationType;
        });

        var info = schedule.GetGroupSplitInfo(b.Build()).Single().Value;
        var djLesson = GetLessonBySpecialization(schedule, "DJ");

        Assert.Equal(["CV-I"], info.Combinations.Select(NameOf));
        Assert.False(info.IncludesLesson(info.Combinations[0], djLesson.Lesson));
    }

    [Fact]
    public void MissingRegistryEntriesAllowNoSpecializationValues()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", specialization: "CV");
            AddLesson(s, "IA2401", specialization: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401").Item;

        var b = new SpecializationRegistryBuilder();
        b.Set([Specializations.CV]).ApplyTo(x =>
        {
            x.Grade = new(group.Grade.Value + 1);
        });

        var info = schedule.GetGroupSplitInfo(b.Build()).Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");

        Assert.True(info.SpecializationActive);
        Assert.Equal(["I"], info.Combinations.Select(NameOf));
        Assert.False(info.IncludesLesson(info.Combinations[0], cvLesson.Lesson));
    }

    [Fact]
    public void RegistrySelectorsOmitFieldsToMatchEveryValueAndUnionOverlaps()
    {
        var b = new SpecializationRegistryBuilder();
        b.Set([Specializations.CV]).ApplyTo(x =>
        {
            x.Grade = new(2);
        });
        b.Set([Specializations.DJ]).ApplyTo(x =>
        {
            x.Faculty = new("IA");
        });
        var registry = b.Build();

        var both = new Group
        {
            Name = "IA2401",
            Grade = new(2),
            GroupNumber = 1,
            QualificationType = QualificationType.Licenta,
            Faculty = new("IA"),
            AttendanceMode = AttendanceMode.Zi,
            Language = Language.Ro,
        };
        var otherFaculty = new Group
        {
            Name = "XX2401",
            Grade = new(2),
            GroupNumber = 1,
            QualificationType = QualificationType.Licenta,
            Faculty = new("XX"),
            AttendanceMode = AttendanceMode.Zi,
            Language = Language.Ro,
        };
        var otherGrade = new Group
        {
            Name = "IA2301",
            Grade = new(3),
            GroupNumber = 1,
            QualificationType = QualificationType.Licenta,
            Faculty = new("IA"),
            AttendanceMode = AttendanceMode.Zi,
            Language = Language.Ro,
        };

        Assert.Equal([Specializations.CV, Specializations.DJ], registry.PermittedFor(in both));
        Assert.Equal([Specializations.CV], registry.PermittedFor(in otherFaculty));
        Assert.Empty(registry.PermittedFor(in otherGrade));
    }

    [Fact]
    public void NumericSubGroupsMustFormAContiguousPrefix()
    {
        var s = CreateBuilder();
        AddLesson(s, "IA2401", subGroup: "I");
        AddLesson(s, "IA2401", subGroup: "III");

        var error = Assert.Throws<InvalidOperationException>(() => s.Build());

        Assert.Contains("contiguous", error.Message);
        Assert.Contains("II", error.Message);
    }

    [Fact]
    public void SingleLanguageSubGroupFails()
    {
        var s = CreateBuilder();
        AddLesson(s, "IA2401", subGroup: "ru");

        var error = Assert.Throws<InvalidOperationException>(() => s.Build());

        Assert.Contains("language subgroup", error.Message);
    }
}
