using System.Text;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class GroupCombinationTests
{
    private static ScheduleBuilder CreateBuilder()
    {
        var builder = new ScheduleBuilder
        {
            GroupParseContext = GroupParseContext.Create(new()
            {
                CurrentStudyYear = 2025,
            }),
        };
        builder.EnableLookupModule();
        return builder;
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
        string? alternative = null,
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
        if (alternative is { } alt)
        {
            lesson.Alternative(new(alt));
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
    public void SharedSpringIsSharedForFacultyIAndRestrictedForFacultyIA()
    {
        var builder = CreateBuilder();
        var iGroup = builder.Group("I2401").Id;
        var iaGroup = builder.Group("IA2401").Id;

        AddLesson(builder, "I2401", subGroup: "I");
        AddLesson(builder, "I2401", subGroup: "II");
        AddLesson(builder, "IA2401", subGroup: "I");
        AddLesson(builder, "IA2401", subGroup: "II");
        AddLesson(builder, "IA2401", specialization: "React");
        AddLesson(builder, "IA2401", specialization: "GA3D");
        AddLesson(builder, "IA2401", specialization: "DJ");

        var sharedCourse = builder.Course("Development of Enterprise Applications");
        var sharedSpring = builder.RegularLesson();
        sharedSpring.Course(sharedCourse);
        sharedSpring.Groups([iGroup, iaGroup]);
        sharedSpring.TimeSlot(new(0));
        sharedSpring.DayOfWeek(DayOfWeek.Monday);
        sharedSpring.Specialization(Specializations.Spring);

        var schedule = builder.Build();
        var iInfo = schedule.GetGroupSplitInfo()[iGroup];
        var iaInfo = schedule.GetGroupSplitInfo()[iaGroup];
        var sharedLesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Course == sharedCourse);

        Assert.False(iInfo.SpecializationActive);
        Assert.All(iInfo.Combinations, c => Assert.True(iInfo.IncludesLesson(c, sharedLesson.Lesson)));

        var spring = iaInfo.Combinations.Single(c => c.Specialization == Specializations.Spring
            && c.Numeric == SubGroup.CreateNumeric(1));
        var react = iaInfo.Combinations.Single(c => c.Specialization == Specializations.React
            && c.Numeric == SubGroup.CreateNumeric(1));
        Assert.True(iaInfo.IncludesLesson(spring, sharedLesson.Lesson));
        Assert.False(iaInfo.IncludesLesson(react, sharedLesson.Lesson));

        var iFiltered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [iGroup],
                SubGroups = [SubGroup.CreateNumeric(1)],
                Specializations = [],
            },
        });
        var iaFiltered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [iaGroup],
                SubGroups = [SubGroup.CreateNumeric(1)],
                Specializations = [Specializations.React],
            },
        });
        Assert.Contains(sharedLesson.Id, iFiltered.Lessons);
        Assert.DoesNotContain(sharedLesson.Id, iaFiltered.Lessons);

        var multiGroupFiltered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [iGroup, iaGroup],
                SubGroups = [SubGroup.CreateNumeric(1)],
                Specializations = [Specializations.React],
            },
        });
        Assert.Contains(sharedLesson.Id, multiGroupFiltered.Lessons);
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
    public void AlternativesJoinTheCartesianProductWhenSeveralAreObserved()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I", alternative: "A1", courseName: "C1");
            AddLesson(s, "IA2401", subGroup: "II", alternative: "A2", courseName: "C2");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;

        Assert.True(info.AlternativeActive);
        Assert.Equal(["A1-I", "A1-II", "A2-I", "A2-II"], info.Combinations.Select(NameOf));
    }

    [Fact]
    public void SingletonAlternativeIsSharedForItsGroup()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I");
            AddLesson(s, "IA2401", subGroup: "II");
            AddLesson(s, "IA2401", alternative: "Psihologie", courseName: "Elective");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var elective = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("Psihologie"));

        Assert.False(info.AlternativeActive);
        Assert.Equal(["I", "II"], info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c => Assert.True(info.IncludesLesson(c, elective.Lesson)));
    }

    [Fact]
    public void ActiveAlternativeExcludesOtherAlternativesLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", subGroup: "I", courseName: "Numeric");
            AddLesson(s, "IA2401", alternative: "A1", courseName: "Elective A1");
            AddLesson(s, "IA2401", alternative: "A2", courseName: "Elective A2");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var a1Lesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("A1"));
        var a2Lesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("A2"));
        var numericLesson = GetLessonBySubGroup(schedule, "I");

        Assert.Equal(["A1-I", "A2-I"], info.Combinations.Select(NameOf));

        var a1Combination = info.Combinations.Single(x => x.Alternative == new Alternative("A1"));
        var a2Combination = info.Combinations.Single(x => x.Alternative == new Alternative("A2"));
        Assert.True(info.IncludesLesson(a1Combination, a1Lesson.Lesson));
        Assert.False(info.IncludesLesson(a2Combination, a1Lesson.Lesson));
        Assert.True(info.IncludesLesson(a2Combination, a2Lesson.Lesson));

        // The numeric lesson matches both combinations.
        Assert.All(info.Combinations, c => Assert.True(info.IncludesLesson(c, numericLesson.Lesson)));
    }

    [Fact]
    public void AlternativeFilterSelectsOnlyMatchingLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", courseName: "Shared");
            AddLesson(s, "IA2401", alternative: "A1", courseName: "Elective A1");
            AddLesson(s, "IA2401", alternative: "A2", courseName: "Elective A2");
        });
        var group = AddAndReturnGroup(schedule, "IA2401");

        var filtered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [group.Id],
                Alternatives = [new Alternative("A1")],
            },
        });

        var courseNames = filtered.EnumerateLessons()
            .Select(x => schedule.Get(x.Lesson.Course).FullName)
            .OrderBy(x => x)
            .ToArray();
        Assert.True(new[] { "Elective A1", "Shared" }.SequenceEqual(courseNames));
    }

    [Fact]
    public async Task SerializationRoundTripsTheAlternative()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", alternative: "Psihologie", courseName: "Elective");
        });

        using var stream = new MemoryStream();
        await ScheduleSerializer.Serialize(schedule, stream, "hash", CancellationToken.None);
        stream.Position = 0;
        var model = await ScheduleSerializer.Deserialize(stream, CancellationToken.None);

        var builder = CreateBuilder();
        ScheduleSerializer.AddToBuilder(builder, model);
        var rebuilt = builder.Build();

        var lesson = Assert.Single(rebuilt.EnumerateAllLessons());
        Assert.Equal(new Alternative("Psihologie"), lesson.Lesson.Alternative);
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

        Assert.True(new[] { Specializations.CV, Specializations.DJ }
            .SequenceEqual(registry.PermittedFor(in both)));
        Assert.True(new[] { Specializations.CV }
            .SequenceEqual(registry.PermittedFor(in otherFaculty)));
        Assert.True(new[] { Specializations.DJ }
            .SequenceEqual(registry.PermittedFor(in otherGrade)));
    }

    [Fact]
    public void DefaultRegistryUsesTheIAFirstYearDualSelector()
    {
        var dual = new Group
        {
            Name = "IA2501 Dual",
            Grade = new(1),
            GroupNumber = 1,
            QualificationType = QualificationType.Licenta,
            Faculty = new("IA"),
            AttendanceMode = AttendanceMode.Dual,
            Language = Language.Ro,
        };

        var permitted = SpecializationRegistryHelper.CreateDefault().PermittedFor(in dual);

        Assert.True(new[] { Specializations.AlgoritmicaGrafurilor, Specializations.Logica }
            .SequenceEqual(permitted));
    }

    [Fact]
    public void RemappingsRunBeforeSpecializationCombinationDiscovery()
    {
        var builder = CreateBuilder();
        Config.ConfigureRemappings(builder.Remappings);
        AddLesson(builder, "IA2401", subGroup: "AG");
        AddLesson(builder, "IA2401", subGroup: "GR");
        AddLesson(builder, "IA2401", subGroup: "Node");

        var schedule = builder.Build();
        var info = schedule.GetGroupSplitInfo().Single().Value;

        Assert.True(info.SpecializationActive);
        Assert.Equal(
            ["Algoritmica Grafurilor", "GA2D", "UI"],
            info.ObservedSpecializations
                .Select(x => x.Value)
                .OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(
            ["Algoritmica Grafurilor", "GA2D", "UI"],
            info.Combinations
                .Select(NameOf));
    }

    [Fact]
    public void FilterCombinesSharedNumericAndSpecializationLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", courseName: "Shared");
            AddLesson(s, "IA2401", subGroup: "I", courseName: "Numeric I");
            AddLesson(s, "IA2401", subGroup: "II", courseName: "Numeric II");
            AddLesson(s, "IA2401", specialization: "CV", courseName: "CV");
            AddLesson(s, "IA2401", specialization: "DJ", courseName: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401");

        var filtered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [group.Id],
                SubGroups = [SubGroup.CreateNumeric(1)],
                Specializations = [Specializations.CV],
            },
        });

        var courseNames = filtered.EnumerateLessons()
            .Select(x => schedule.Get(x.Lesson.Course).FullName)
            .OrderBy(x => x)
            .ToArray();
        Assert.True(new[] { "CV", "Numeric I", "Shared" }.SequenceEqual(courseNames));
    }

    [Fact]
    public void CombinationIncludesItsSelectedDimensionsAndExcludesConflicts()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401", courseName: "Shared");
            AddLesson(s, "IA2401", subGroup: "I", courseName: "Numeric I");
            AddLesson(s, "IA2401", subGroup: "II", courseName: "Numeric II");
            AddLesson(s, "IA2401", subGroup: "ro", courseName: "Romanian");
            AddLesson(s, "IA2401", subGroup: "ru", courseName: "Russian");
            AddLesson(s, "IA2401", subGroup: "începători", courseName: "Language");
            AddLesson(s, "IA2401", courseName: "Language");
            AddLesson(s, "IA2401", specialization: "CV", courseName: "CV");
            AddLesson(s, "IA2401", specialization: "DJ", courseName: "DJ");
        });

        var info = schedule.GetGroupSplitInfo().Single().Value;
        var combination = info.Combinations.Single(c => c.Specialization == Specializations.CV
            && c.Proficiency == SpecialSubGroups.Beginners
            && c.Language == SpecialSubGroups.Ro
            && c.Numeric == SubGroup.CreateNumeric(1));

        Assert.True(info.IncludesLesson(combination, Lesson("Shared")));
        Assert.True(info.IncludesLesson(combination, Lesson("Numeric I")));
        Assert.True(info.IncludesLesson(combination, Lesson("Romanian")));
        Assert.True(info.IncludesLesson(combination, Lesson("Language", SpecialSubGroups.Beginners)));
        Assert.True(info.IncludesLesson(combination, Lesson("CV")));

        Assert.False(info.IncludesLesson(combination, Lesson("Numeric II")));
        Assert.False(info.IncludesLesson(combination, Lesson("Russian")));
        Assert.False(info.IncludesLesson(combination, Lesson("Language", SpecialSubGroups.NonBeginners)));
        Assert.False(info.IncludesLesson(combination, Lesson("DJ")));

        LessonData Lesson(string courseName, SubGroup? subGroup = null)
        {
            return schedule.EnumerateAllLessons()
                .Single(x => schedule.Get(x.Lesson.Course).FullName == courseName
                    && (subGroup is null || x.Lesson.SubGroup == subGroup))
                .Lesson;
        }
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
