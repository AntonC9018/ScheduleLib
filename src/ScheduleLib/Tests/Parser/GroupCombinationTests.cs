using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                CurrentStudyYear = new(2025),
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
        if (subGroup is { } sub)
        {
            global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder4 = lesson;
            global::ScheduleLib.SubGroup subGroup2 = new(sub);
            lessonBuilder4.SubGroup(subGroup2);
        }
        if (specialization is { } spec)
        {
            global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder5 = lesson;
            global::ScheduleLib.Specialization specialization2 = new(spec);
            lessonBuilder5.Specialization(specialization2);
        }
        if (alternative is { } alt)
        {
            global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder6 = lesson;
            global::ScheduleLib.Alternative alternative2 = new(alt);
            lessonBuilder6.Alternative(alternative2);
        }
    }

    private static Accessor<Group, GroupId> AddAndReturnGroup(
        Schedule schedule,
        string groupName)
    {
        return schedule.EnumerateGroups()
            .Single(x => x.Item.Name == groupName);
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
        global::ScheduleLib.ListStringBuilder CalculateLb()
        {
            global::ScheduleLib.ListStringBuilder lb = new ListStringBuilder(sb, "-");
            return lb;
        }

        c.AppendFileNamePart(CalculateLb());
        return sb.ToString();
    }

    [Fact]
    public void EnumeratesTheCartesianProductOfActivePartitions()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I", courseName: "C1");
            AddLesson(s, groupName: "IA2401", subGroup: "II");
            AddLesson(s, groupName: "IA2401", subGroup: "ro");
            AddLesson(s, groupName: "IA2401", subGroup: "ru");
            AddLesson(s, groupName: "IA2401", subGroup: "începători", courseName: "C2");
            AddLesson(s, groupName: "IA2401", courseName: "C2");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "GA2D");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;

        Assert.True(info.SpecializationActive);
        Assert.Equal(expected: 16, actual: info.Combinations.Length);
        string expected2 = "CV-începători-ro-I";
        string actual2 = NameOf(info.Combinations[0]);
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "CV-începători-ru-I";
        string actual3 = NameOf(info.Combinations[1]);
        Assert.Equal(expected: expected3, actual: actual3);
        string expected4 = "CV-nuîncepători-ro-I";
        string actual4 = NameOf(info.Combinations[2]);
        Assert.Equal(expected: expected4, actual: actual4);
        string expected5 = "GA2D-nuîncepători-ru-II";
        string actual5 = NameOf(info.Combinations[^1]);
        Assert.Equal(expected: expected5, actual: actual5);
    }

    [Fact]
    public void NoActiveSplitProducesNoCombinations()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, "IA2401");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;

        Assert.Empty(info.Combinations);
        Assert.False(info.SpecializationActive);
    }

    [Fact]
    public void SingletonSpecializationIsSharedForItsGroup()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", subGroup: "II");
            AddLesson(s, groupName: "IA2401", specialization: "Spring");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var springLesson = GetLessonBySpecialization(schedule, "Spring");

        Assert.False(info.SpecializationActive);
        Assert.Equal(expected: ["I", "II"], actual: info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c =>
        {
            bool condition = info.IncludesLesson(c, springLesson.Lesson);
            Assert.True(condition);
        });
    }

    [Fact]
    public void SharedSpringIsSharedForFacultyIAndRestrictedForFacultyIA()
    {
        var builder = CreateBuilder();
        var iGroup = builder.Group("I2401").Id;
        var iaGroup = builder.Group("IA2401").Id;

        AddLesson(builder, groupName: "I2401", subGroup: "I");
        AddLesson(builder, groupName: "I2401", subGroup: "II");
        AddLesson(builder, groupName: "IA2401", subGroup: "I");
        AddLesson(builder, groupName: "IA2401", subGroup: "II");
        AddLesson(builder, groupName: "IA2401", specialization: "React");
        AddLesson(builder, groupName: "IA2401", specialization: "GA3D");
        AddLesson(builder, groupName: "IA2401", specialization: "DJ");

        var sharedCourse = builder.Course("Development of Enterprise Applications");
        var sharedSpring = builder.RegularLesson();
        sharedSpring.Course(sharedCourse);
        sharedSpring.Groups([iGroup, iaGroup]);
        global::ScheduleLib.Builders.LessonBuilder<global::ScheduleLib.Builders.WeeklyLessonBuilderModel> lessonBuilder = sharedSpring;
        global::TimeSlot timeSlot = new(0);
        lessonBuilder.TimeSlot(timeSlot);
        sharedSpring.DayOfWeek(DayOfWeek.Monday);
        sharedSpring.Specialization(Specialization.Spring);

        var schedule = builder.Build();
        var iInfo = schedule.GetGroupPartitionInfo()[iGroup];
        var iaInfo = schedule.GetGroupPartitionInfo()[iaGroup];
        var sharedLesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Course == sharedCourse);

        Assert.False(iInfo.SpecializationActive);
        Assert.All(iInfo.Combinations, c =>
        {
            bool condition = iInfo.IncludesLesson(c, sharedLesson.Lesson);
            Assert.True(condition);
        });

        var spring = iaInfo.Combinations.Single(c => c.Specialization == Specialization.Spring
            && c.Numeric == SubGroup.CreateNumeric(1));
        var react = iaInfo.Combinations.Single(c => c.Specialization == Specialization.React
            && c.Numeric == SubGroup.CreateNumeric(1));
        bool condition2 = iaInfo.IncludesLesson(spring, sharedLesson.Lesson);
        Assert.True(condition2);
        bool condition3 = iaInfo.IncludesLesson(react, sharedLesson.Lesson);
        Assert.False(condition3);

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
                Specializations = [Specialization.React],
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
                Specializations = [Specialization.React],
            },
        });
        Assert.Contains(sharedLesson.Id, multiGroupFiltered.Lessons);
    }

    [Fact]
    public void ActiveSpecializationExcludesOtherSpecializationsLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var djLesson = GetLessonBySpecialization(schedule, "DJ");

        Assert.Equal(expected: ["CV-I", "DJ-I"], actual: info.Combinations.Select(NameOf));

        var cvCombination = info.Combinations.Single(x => x.Specialization == new Specialization("CV"));
        var djCombination = info.Combinations.Single(x => x.Specialization == new Specialization("DJ"));
        bool condition = info.IncludesLesson(cvCombination, cvLesson.Lesson);
        Assert.True(condition);
        bool condition2 = info.IncludesLesson(djCombination, cvLesson.Lesson);
        Assert.False(condition2);
        bool condition3 = info.IncludesLesson(djCombination, djLesson.Lesson);
        Assert.True(condition3);

        // The numeric lesson matches both combinations.
        var numericLesson = GetLessonBySubGroup(schedule, "I");
        Assert.All(info.Combinations, c =>
        {
            bool condition = info.IncludesLesson(c, numericLesson.Lesson);
            Assert.True(condition);
        });
    }

    [Fact]
    public void AlternativesJoinTheCartesianProductWhenSeveralAreObserved()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I", alternative: "A1", courseName: "C1");
            AddLesson(s, groupName: "IA2401", subGroup: "II", alternative: "A2", courseName: "C2");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;

        Assert.True(info.AlternativeActive);
        Assert.Equal(expected: ["A1-I", "A1-II", "A2-I", "A2-II"], actual: info.Combinations.Select(NameOf));
    }

    [Fact]
    public void SingletonAlternativeIsSharedForItsGroup()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", subGroup: "II");
            AddLesson(s, groupName: "IA2401", alternative: "Psihologie", courseName: "Elective");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var elective = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("Psihologie"));

        Assert.False(info.AlternativeActive);
        Assert.Equal(expected: ["I", "II"], actual: info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c =>
        {
            bool condition = info.IncludesLesson(c, elective.Lesson);
            Assert.True(condition);
        });
    }

    [Fact]
    public void ActiveAlternativeExcludesOtherAlternativesLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I", courseName: "Numeric");
            AddLesson(s, groupName: "IA2401", alternative: "A1", courseName: "Elective A1");
            AddLesson(s, groupName: "IA2401", alternative: "A2", courseName: "Elective A2");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var a1Lesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("A1"));
        var a2Lesson = schedule.EnumerateAllLessons()
            .Single(x => x.Lesson.Alternative == new Alternative("A2"));
        var numericLesson = GetLessonBySubGroup(schedule, "I");

        Assert.Equal(expected: ["A1-I", "A2-I"], actual: info.Combinations.Select(NameOf));

        var a1Combination = info.Combinations.Single(x => x.Alternative == new Alternative("A1"));
        var a2Combination = info.Combinations.Single(x => x.Alternative == new Alternative("A2"));
        bool condition = info.IncludesLesson(a1Combination, a1Lesson.Lesson);
        Assert.True(condition);
        bool condition2 = info.IncludesLesson(a2Combination, a1Lesson.Lesson);
        Assert.False(condition2);
        bool condition3 = info.IncludesLesson(a2Combination, a2Lesson.Lesson);
        Assert.True(condition3);

        // The numeric lesson matches both combinations.
        Assert.All(info.Combinations, c =>
        {
            bool condition = info.IncludesLesson(c, numericLesson.Lesson);
            Assert.True(condition);
        });
    }

    [Fact]
    public void AlternativeFilterSelectsOnlyMatchingLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", courseName: "Shared");
            AddLesson(s, groupName: "IA2401", alternative: "A1", courseName: "Elective A1");
            AddLesson(s, groupName: "IA2401", alternative: "A2", courseName: "Elective A2");
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
        bool condition = new[] { "Elective A1", "Shared" }.SequenceEqual(courseNames);
        Assert.True(condition);
    }

    [Fact]
    public async Task SerializationRoundTripsTheAlternative()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", alternative: "Psihologie", courseName: "Elective");
        });

        using var stream = new MemoryStream();
        await ScheduleSerializer.Serialize()
            .Input(schedule)
            .Output(stream)
            .Hash("hash")
            .ExecuteAsync(CancellationToken.None);
        stream.Position = 0;
        var model = await ScheduleSerializer.Deserialize(stream, CancellationToken.None);

        var builder = CreateBuilder();
        ScheduleSerializer.AddToBuilder(builder, model);
        var rebuilt = builder.Build();

        var lesson = Assert.Single(rebuilt.EnumerateAllLessons());
        global::ScheduleLib.Alternative expected2 = new Alternative("Psihologie");
        global::ScheduleLib.Alternative actual2 = lesson.Lesson.Alternative;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public async Task SerializationUsesFlatSiblingFieldsWithNullForAll()
    {
        // Spec-6: lessons serialize SubGroup/Specialization/Alternative as flat
        // sibling fields with null = All; GroupPartitionKey is never serialized.
        // Committed caches predate Alternative and omit the field, which
        // deserializes as All (see LessonBaseModel.Alternative comment).
        // The AllThingsWork Verify snapshot was intentionally removed in
        // 05cfdc8/c3f5a3a (a machine-specific path baked into the expectation
        // made it brittle); it is not restored here.
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I", specialization: "GA2D", alternative: "Psihologie", courseName: "Elective");
            AddLesson(s, groupName: "IA2401", courseName: "Shared");
        });

        string json;
        using (var stream = new MemoryStream())
        {
            await ScheduleSerializer.Serialize()
                .Input(schedule)
                .Output(stream)
                .Hash("hash")
                .ExecuteAsync(CancellationToken.None);
            json = Encoding.UTF8.GetString(stream.ToArray());
        }

        Assert.DoesNotContain(expectedSubstring: "GroupPartitionKey", actualString: json, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(json);
        var lessons = doc.RootElement.GetProperty("RegularLessons");
        Assert.Equal(expected: 2, actual: lessons.GetArrayLength());

        var elective = lessons.EnumerateArray()
            .Single(e => e.GetProperty("Alternative").ValueKind == JsonValueKind.String);
        Assert.Equal(expected: "I", actual: elective.GetProperty("SubGroup")
            .GetString());
        Assert.Equal(expected: "GA2D", actual: elective.GetProperty("Specialization")
            .GetString());
        Assert.Equal(expected: "Psihologie", actual: elective.GetProperty("Alternative")
            .GetString());

        var shared = lessons.EnumerateArray()
            .Single(e => e.GetProperty("Alternative").ValueKind == JsonValueKind.Null);
        Assert.Equal(expected: JsonValueKind.Null, actual: shared.GetProperty("SubGroup").ValueKind);
        Assert.Equal(expected: JsonValueKind.Null, actual: shared.GetProperty("Specialization").ValueKind);

        // Legacy caches omit Alternative entirely; they still load as All.
        var node = JsonNode.Parse(json)!;
        foreach (var lessonNode in node["RegularLessons"]!.AsArray())
        {
            lessonNode!.AsObject()
                .Remove("Alternative");
        }
        using var legacyStream = new MemoryStream(Encoding.UTF8.GetBytes(node.ToJsonString()));
        var legacyModel = await ScheduleSerializer.Deserialize(legacyStream, CancellationToken.None);
        var legacyBuilder = CreateBuilder();
        ScheduleSerializer.AddToBuilder(legacyBuilder, legacyModel);
        var rebuiltLegacy = legacyBuilder.Build();
        Assert.All(
            rebuiltLegacy.EnumerateAllLessons(),
            l => Assert.Equal(expected: Alternative.All, actual: l.Lesson.Alternative));

        // Fresh serialize→deserialize preserves Alternative (and siblings).
        byte[] CalculateBuffer()
        {
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            return buffer;
        }

        using var freshStream = new MemoryStream(CalculateBuffer());
        var freshModel = await ScheduleSerializer.Deserialize(freshStream, CancellationToken.None);
        var freshBuilder = CreateBuilder();
        ScheduleSerializer.AddToBuilder(freshBuilder, freshModel);
        var rebuiltFresh = freshBuilder.Build();
        var freshLessons = rebuiltFresh.EnumerateAllLessons()
            .ToList();
        Assert.Equal(expected: 2, actual: freshLessons.Count);
        var freshElective = Assert.Single(
            freshLessons.Where(l => l.Lesson.Alternative == new Alternative("Psihologie")));
        global::ScheduleLib.SubGroup expected2 = new SubGroup("I");
        global::ScheduleLib.SubGroup actual2 = freshElective.Lesson.SubGroup;
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.Specialization expected3 = new Specialization("GA2D");
        global::ScheduleLib.Specialization actual3 = freshElective.Lesson.Specialization;
        Assert.Equal(expected: expected3, actual: actual3);
        Assert.All(
            freshLessons.Where(l => l.Lesson.Alternative == Alternative.All),
            l =>
            {
                Assert.Equal(expected: SubGroup.All, actual: l.Lesson.SubGroup);
                Assert.Equal(expected: Specialization.All, actual: l.Lesson.Specialization);
            });
    }

    [Fact]
    public void OpaqueSubGroupValuesDoNotEnterCombinations()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", subGroup: "S21");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var opaqueLesson = GetLessonBySubGroup(schedule, "S21");

        Assert.Equal(expected: ["I"], actual: info.Combinations.Select(NameOf));
        bool condition = info.IncludesLesson(info.Combinations[0], opaqueLesson.Lesson);
        Assert.False(condition);
    }

    [Fact]
    public void RegistryRestrictsTheSpecializationValues()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401").Item;

        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.CV])
            .ApplyTo(x =>
        {
            x.Grade = group.Grade;
            x.Faculty = group.Faculty;
            x.AttendanceMode = group.AttendanceMode;
            x.Qualification = group.QualificationType;
        });

        var info = schedule.GetGroupPartitionInfo(b.Build())
            .Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var djLesson = GetLessonBySpecialization(schedule, "DJ");

        // Only CV is permitted, so the partition is inactive: no specialization
        // dimension, and both the permitted and the filtered-out values behave
        // as shared for the group instead of vanishing from every combination.
        Assert.False(info.SpecializationActive);
        Assert.Equal(expected: ["I"], actual: info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c => Assert.Null(c.Specialization));
        bool condition = info.IncludesLesson(info.Combinations[0], cvLesson.Lesson);
        Assert.True(condition);
        bool condition2 = info.IncludesLesson(info.Combinations[0], djLesson.Lesson);
        Assert.True(condition2);
    }

    [Fact]
    public void MissingRegistryEntriesAllowNoSpecializationValues()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401").Item;

        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.CV])
            .ApplyTo(x =>
        {
            x.Grade = new(group.Grade.Value + 1);
        });

        var info = schedule.GetGroupPartitionInfo(b.Build())
            .Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var djLesson = GetLessonBySpecialization(schedule, "DJ");

        // Nothing is permitted, so the partition is inactive and both observed
        // values behave as shared for the group.
        Assert.False(info.SpecializationActive);
        Assert.Equal(expected: ["I"], actual: info.Combinations.Select(NameOf));
        Assert.All(info.Combinations, c => Assert.Null(c.Specialization));
        bool condition = info.IncludesLesson(info.Combinations[0], cvLesson.Lesson);
        Assert.True(condition);
        bool condition2 = info.IncludesLesson(info.Combinations[0], djLesson.Lesson);
        Assert.True(condition2);
    }

    [Fact]
    public void RegistryFilteringKeepsThePermittedSubsetActive()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ");
            AddLesson(s, groupName: "IA2401", specialization: "React");
        });
        var group = AddAndReturnGroup(schedule, "IA2401").Item;

        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.CV, Specialization.DJ])
            .ApplyTo(x =>
        {
            x.Grade = group.Grade;
            x.Faculty = group.Faculty;
            x.AttendanceMode = group.AttendanceMode;
            x.Qualification = group.QualificationType;
        });

        var info = schedule.GetGroupPartitionInfo(b.Build())
            .Single().Value;
        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var reactLesson = GetLessonBySpecialization(schedule, "React");

        // Two permitted values keep the partition active on the permitted
        // subset only; the filtered-out value has no combination.
        Assert.True(info.SpecializationActive);
        Assert.Equal(expected: ["CV", "DJ"], actual: info.PermittedSpecializations.Select(x => x.Value));
        Assert.Equal(expected: ["CV-I", "DJ-I"], actual: info.Combinations.Select(NameOf));

        var cvCombination = info.Combinations.Single(x => x.Specialization == Specialization.CV);
        bool condition = info.IncludesLesson(cvCombination, cvLesson.Lesson);
        Assert.True(condition);
        bool condition2 = info.IncludesLesson(cvCombination, reactLesson.Lesson);
        Assert.False(condition2);
        Assert.All(info.Combinations, c =>
        {
            bool condition = info.IncludesLesson(c, reactLesson.Lesson);
            Assert.False(condition);
        });
    }

    [Fact]
    public void RegistryDeactivatedCombinationFilterIncludesAllSpecializationLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", subGroup: "I");
            AddLesson(s, groupName: "IA2401", specialization: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401");

        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.CV])
            .ApplyTo(x =>
        {
            x.Grade = group.Item.Grade;
            x.Faculty = group.Item.Faculty;
            x.AttendanceMode = group.Item.AttendanceMode;
            x.Qualification = group.Item.QualificationType;
        });

        var info = schedule.GetGroupPartitionInfo(b.Build())
            .Single().Value;
        Assert.False(info.SpecializationActive);
        var combination = Assert.Single(info.Combinations);

        // The combination PDF path must use IncludesLesson semantics: the
        // partition is inactive, so the filter carries no specialization
        // restriction and both the permitted and the filtered-out values
        // behave as shared. (An empty array here would drop both lessons,
        // because FilteredSchedule counts raw observed values.)
        var filtered = schedule.Filter(new()
        {
            GroupFilter = combination.ToGroupFilter(group.Id),
        });

        var cvLesson = GetLessonBySpecialization(schedule, "CV");
        var djLesson = GetLessonBySpecialization(schedule, "DJ");
        Assert.Contains(cvLesson.Id, filtered.Lessons);
        Assert.Contains(djLesson.Id, filtered.Lessons);
    }

    [Fact]
    public void RegistrySelectorsOmitFieldsToMatchEveryValueAndUnionOverlaps()
    {
        var b = new SpecializationRegistryBuilder();
        b.Set([Specialization.CV])
            .ApplyTo(x =>
        {
            x.Grade = new(2);
        });
        b.Set([Specialization.DJ])
            .ApplyTo(x =>
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

        global::ScheduleLib.Specialization[] value = new[] { Specialization.CV, Specialization.DJ };
        global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.Specialization> second = registry.PermittedFor(in both);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Specialization> second2 = second;
        bool condition = value.SequenceEqual(second2);
        Assert.True(condition);
        global::ScheduleLib.Specialization[] value2 = new[] { Specialization.CV };
        global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.Specialization> second3 = registry.PermittedFor(in otherFaculty);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Specialization> second4 = second3;
        bool condition2 = value2.SequenceEqual(second4);
        Assert.True(condition2);
        global::ScheduleLib.Specialization[] value3 = new[] { Specialization.DJ };
        global::System.Collections.Immutable.ImmutableArray<global::ScheduleLib.Specialization> second5 = registry.PermittedFor(in otherGrade);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Specialization> second6 = second5;
        bool condition3 = value3.SequenceEqual(second6);
        Assert.True(condition3);
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
            AttendanceMode = AttendanceMode.DualZi,
            Language = Language.Ro,
        };

        var permitted = Config.SpecializationRegistry.PermittedFor(in dual);

        Assert.True(new[] { Specialization.AlgoritmicaGrafurilor, Specialization.Logica }
            .SequenceEqual(permitted));
    }

    [Fact]
    public void RemappingsRunBeforeSpecializationCombinationDiscovery()
    {
        var builder = CreateBuilder();
        Config.ConfigureRemappings(builder.Remappings);
        AddLesson(builder, groupName: "IA2401", subGroup: "AG");
        AddLesson(builder, groupName: "IA2401", subGroup: "GR");
        AddLesson(builder, groupName: "IA2401", subGroup: "Node");

        var schedule = builder.Build();
        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;

        Assert.True(info.SpecializationActive);
        Assert.Equal(
            expected: ["Algoritmica Grafurilor", "GA2D", "UI"],
            actual: info.PermittedSpecializations
                .Select(x => x.Value)
                .OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(
            expected: ["Algoritmica Grafurilor", "GA2D", "UI"],
            actual: info.Combinations
                .Select(NameOf));
    }

    [Fact]
    public void FilterCombinesSharedNumericAndSpecializationLessons()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", courseName: "Shared");
            AddLesson(s, groupName: "IA2401", subGroup: "I", courseName: "Numeric I");
            AddLesson(s, groupName: "IA2401", subGroup: "II", courseName: "Numeric II");
            AddLesson(s, groupName: "IA2401", specialization: "CV", courseName: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ", courseName: "DJ");
        });
        var group = AddAndReturnGroup(schedule, "IA2401");

        var filtered = schedule.Filter(new()
        {
            GroupFilter = new()
            {
                OneOfGroupIds = [group.Id],
                SubGroups = [SubGroup.CreateNumeric(1)],
                Specializations = [Specialization.CV],
            },
        });

        var courseNames = filtered.EnumerateLessons()
            .Select(x => schedule.Get(x.Lesson.Course).FullName)
            .OrderBy(x => x)
            .ToArray();
        bool condition = new[] { "CV", "Numeric I", "Shared" }.SequenceEqual(courseNames);
        Assert.True(condition);
    }

    [Fact]
    public void CombinationIncludesItsSelectedDimensionsAndExcludesConflicts()
    {
        var schedule = Build(s =>
        {
            AddLesson(s, groupName: "IA2401", courseName: "Shared");
            AddLesson(s, groupName: "IA2401", subGroup: "I", courseName: "Numeric I");
            AddLesson(s, groupName: "IA2401", subGroup: "II", courseName: "Numeric II");
            AddLesson(s, groupName: "IA2401", subGroup: "ro", courseName: "Romanian");
            AddLesson(s, groupName: "IA2401", subGroup: "ru", courseName: "Russian");
            AddLesson(s, groupName: "IA2401", subGroup: "începători", courseName: "Language");
            AddLesson(s, groupName: "IA2401", courseName: "Language");
            AddLesson(s, groupName: "IA2401", specialization: "CV", courseName: "CV");
            AddLesson(s, groupName: "IA2401", specialization: "DJ", courseName: "DJ");
        });

        var info = schedule.GetGroupPartitionInfo()
            .Single().Value;
        var combination = info.Combinations.Single(c =>
        {
            bool CheckCondition()
            {
                if (!(c.Specialization == Specialization.CV))
                {
                    return false;
                }

                if (!(c.Proficiency == SpecialSubGroups.Beginners))
                {
                    return false;
                }

                if (!(c.Language == SpecialSubGroups.Ro))
                {
                    return false;
                }

                if (!(c.Numeric == SubGroup.CreateNumeric(1)))
                {
                    return false;
                }

                return true;
            }

            return CheckCondition();
        });

        global::ScheduleLib.LessonData CalculateLesson()
        {
            global::ScheduleLib.LessonData lesson = Lesson("Shared");
            return lesson;
        }

        bool condition = info.IncludesLesson(combination, CalculateLesson());
        Assert.True(condition);
        global::ScheduleLib.LessonData CalculateLesson2()
        {
            global::ScheduleLib.LessonData lesson2 = Lesson("Numeric I");
            return lesson2;
        }

        bool condition2 = info.IncludesLesson(combination, CalculateLesson2());
        Assert.True(condition2);
        global::ScheduleLib.LessonData CalculateLesson3()
        {
            global::ScheduleLib.LessonData lesson3 = Lesson("Romanian");
            return lesson3;
        }

        bool condition3 = info.IncludesLesson(combination, CalculateLesson3());
        Assert.True(condition3);
        global::ScheduleLib.LessonData CalculateLesson4()
        {
            global::ScheduleLib.LessonData lesson4 = Lesson("Language", SpecialSubGroups.Beginners);
            return lesson4;
        }

        bool condition4 = info.IncludesLesson(combination, CalculateLesson4());
        Assert.True(condition4);
        global::ScheduleLib.LessonData CalculateLesson5()
        {
            global::ScheduleLib.LessonData lesson5 = Lesson("CV");
            return lesson5;
        }

        bool condition5 = info.IncludesLesson(combination, CalculateLesson5());
        Assert.True(condition5);

        global::ScheduleLib.LessonData CalculateLesson6()
        {
            global::ScheduleLib.LessonData lesson6 = Lesson("Numeric II");
            return lesson6;
        }

        bool condition6 = info.IncludesLesson(combination, CalculateLesson6());
        Assert.False(condition6);
        global::ScheduleLib.LessonData CalculateLesson7()
        {
            global::ScheduleLib.LessonData lesson7 = Lesson("Russian");
            return lesson7;
        }

        bool condition7 = info.IncludesLesson(combination, CalculateLesson7());
        Assert.False(condition7);
        global::ScheduleLib.LessonData CalculateLesson8()
        {
            global::ScheduleLib.LessonData lesson8 = Lesson("Language", SpecialSubGroups.NonBeginners);
            return lesson8;
        }

        bool condition8 = info.IncludesLesson(combination, CalculateLesson8());
        Assert.False(condition8);
        global::ScheduleLib.LessonData CalculateLesson9()
        {
            global::ScheduleLib.LessonData lesson9 = Lesson("DJ");
            return lesson9;
        }

        bool condition9 = info.IncludesLesson(combination, CalculateLesson9());
        Assert.False(condition9);

        LessonData Lesson(string courseName, SubGroup? subGroup = null)
        {
            return schedule.EnumerateAllLessons()
                .Single(x =>
                {
                    bool CalculateConditionResult()
                    {
                        if (schedule.Get(x.Lesson.Course).FullName == courseName)
                        {
                            if (subGroup is null)
                            { return true; }
                            else
                            {
                                if (x.Lesson.SubGroup == subGroup)
                                {
                                    return true;
                                }

                                return false;
                            }
                        }
                        else
                        { return false; }
                    }

                    bool conditionResult = CalculateConditionResult();

                    if (conditionResult)
                    { return true; }
                    return false;
                })
                .Lesson;
        }
    }

    [Fact]
    public void NumericSubGroupsMustFormAContiguousPrefix()
    {
        var s = CreateBuilder();
        AddLesson(s, groupName: "IA2401", subGroup: "I");
        AddLesson(s, groupName: "IA2401", subGroup: "III");

        var error = Assert.Throws<InvalidSubGroupPartitionException>(() => s.Build());

        Assert.Contains(expectedSubstring: "contiguous", actualString: error.Message);
        Assert.Contains(expectedSubstring: "II", actualString: error.Message);
    }

    [Fact]
    public void SingleLanguageSubGroupFails()
    {
        var s = CreateBuilder();
        AddLesson(s, groupName: "IA2401", subGroup: "ru");

        var error = Assert.Throws<InvalidSubGroupPartitionException>(() => s.Build());

        Assert.Contains(expectedSubstring: "language subgroup", actualString: error.Message);
    }
}
