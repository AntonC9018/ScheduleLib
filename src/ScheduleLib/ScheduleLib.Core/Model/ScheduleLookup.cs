using System.Collections.Frozen;
using System.Collections.Immutable;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib;

/// <summary>Immutable indexes over a built schedule. Ids are positions in its lists.</summary>
public sealed class ScheduleLookup
{
    public readonly CourseNameIndex Courses;
    public readonly FrozenDictionary<NameParts<string?>, ImmutableArray<int>> TeachersByLastName;
    public readonly FrozenDictionary<string, GroupId> Groups;

    /// <summary>The lessons of each course, indexed by <see cref="CourseId.Id"/>.</summary>
    public readonly ImmutableArray<ImmutableArray<AnyLessonId>> LessonsByCourse;

    private ScheduleLookup(Schedule schedule, CourseNameUnifierConfig courseNames)
    {
        Courses = CourseNameIndex.Create(schedule.Courses, courseNames);
        TeachersByLastName = IndexTeachers(schedule.Teachers);
        Groups = IndexGroups(schedule.Groups);
        LessonsByCourse = IndexLessons(schedule);
    }

    public ImmutableArray<AnyLessonId> LessonsOf(CourseId courseId)
    {
        return LessonsByCourse[courseId.Id];
    }

    public static ScheduleLookup Create(Schedule schedule, CourseNameUnifierConfig courseNames)
    {
        return new(schedule, courseNames);
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

/// <summary>
/// A schedule as workflows see it, with the indexes built from it.
/// The indexes are always built from the schedule they are paired with.
/// </summary>
public sealed class LoadedSchedule
{
    public readonly Schedule Schedule;
    public readonly ScheduleLookup Lookup;

    private LoadedSchedule(Schedule schedule, ScheduleLookup lookup)
    {
        Schedule = schedule;
        Lookup = lookup;
    }

    /// <summary>Builds the indexes of <paramref name="schedule"/>.</summary>
    public static LoadedSchedule Create(Schedule schedule, CourseNameUnifierConfig courseNames)
    {
        var lookup = ScheduleLookup.Create(schedule, courseNames);
        return new(schedule, lookup);
    }
}
