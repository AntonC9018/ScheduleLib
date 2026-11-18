using System.Collections.Frozen;
using System.Collections.Immutable;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib;

/// <summary>Immutable indexes over a built schedule. Ids are positions in its lists.</summary>
public sealed class ScheduleLookup
{
    public required CourseNameIndex Courses { get; init; }
    public required FrozenDictionary<NameParts<string?>, ImmutableArray<int>> TeachersByLastName { get; init; }
    public required FrozenDictionary<string, GroupId> Groups { get; init; }

    /// <summary>The lessons of each course, indexed by <see cref="CourseId.Id"/>.</summary>
    public required ImmutableArray<ImmutableArray<AnyLessonId>> LessonsByCourse { get; init; }

    public ImmutableArray<AnyLessonId> LessonsOf(CourseId courseId)
    {
        return LessonsByCourse[courseId.Id];
    }

    public static ScheduleLookup Create(Schedule schedule, CourseNameUnifierConfig courseNames)
    {
        return new()
        {
            Courses = CourseNameIndex.Create(schedule.Courses, courseNames),
            TeachersByLastName = IndexTeachers(schedule.Teachers),
            Groups = IndexGroups(schedule.Groups),
            LessonsByCourse = IndexLessons(schedule),
        };
    }

    private static FrozenDictionary<NameParts<string?>, ImmutableArray<int>> IndexTeachers(
        ImmutableArray<Teacher> teachers)
    {
        var comparer = IgnoreDiacriticsAndCase_Name_Comparer.Instance;
        return teachers.Select((teacher, id) => (teacher.PersonName.LastName, Id: id))
            .Where(x => x.LastName != default)
            .GroupBy(keySelector: x => x.LastName.Parts, comparer: comparer)
            .ToFrozenDictionary(
                keySelector: g => g.Key,
                elementSelector: Ids,
                comparer: comparer);

        static ImmutableArray<int> Ids(IGrouping<NameParts<string?>, (LastName LastName, int Id)> teachers)
        {
            return teachers.Select(x => x.Id)
                .ToImmutableArray();
        }
    }

    private static FrozenDictionary<string, GroupId> IndexGroups(ImmutableArray<Group> groups)
    {
        return groups.Select((group, id) => (group.Name, Id: new GroupId(id)))
            .ToFrozenDictionary(
                keySelector: x => x.Name,
                elementSelector: x => x.Id,
                comparer: StringComparer.OrdinalIgnoreCase);
    }

    private static ImmutableArray<ImmutableArray<AnyLessonId>> IndexLessons(Schedule schedule)
    {
        var lessons = schedule.Courses.Select(_ => ImmutableArray.CreateBuilder<AnyLessonId>())
            .ToArray();
        for (int i = 0; i < schedule.WeeklyLessons.Length; i++)
        {
            var course = schedule.WeeklyLessons[i].Base.Data.Course;
            lessons[course.Id].Add(new(LessonRegularity.Weekly, i));
        }
        for (int i = 0; i < schedule.OneTimeLessons.Length; i++)
        {
            var course = schedule.OneTimeLessons[i].Base.Data.Course;
            lessons[course.Id].Add(new(LessonRegularity.OneTime, i));
        }
        return lessons.Select(x => x.ToImmutable())
            .ToImmutableArray();
    }
}

/// <summary>A schedule as workflows see it, with the indexes built from it.</summary>
public sealed record LoadedSchedule(Schedule Schedule, ScheduleLookup Lookup)
{
    public static LoadedSchedule Create(Schedule schedule, CourseNameUnifierConfig courseNames)
    {
        var lookup = ScheduleLookup.Create(schedule, courseNames);
        return new(schedule, lookup);
    }
}
