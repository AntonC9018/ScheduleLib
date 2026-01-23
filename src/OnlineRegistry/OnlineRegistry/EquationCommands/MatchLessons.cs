using System.Diagnostics;
using ScheduleLib.Builders;
using ScheduleLib.Generation;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonSearchFilter
{
    public readonly CourseId CourseId;
    public readonly FoundGroups Groups;
    public SubGroup SubGroup { get; init; }

    public LessonSearchFilter(
        CourseId courseId,
        FoundGroups groups,
        SubGroup subGroup)
    {
        CourseId = courseId;
        Groups = groups;
        SubGroup = subGroup;
    }
}

internal readonly record struct LessonMatchParams
{
    public readonly LessonSearchFilter Filter;
    public readonly LessonsByCourseMap Lookup;
    public readonly Schedule Schedule;

    public LessonMatchParams(
        LessonSearchFilter filter,
        LessonsByCourseMap lookup,
        Schedule schedule)
    {
        Filter = filter;
        Lookup = lookup;
        Schedule = schedule;
    }
}

internal static class MatchLessonHelper
{
    internal static IEnumerable<AnyLessonId> MatchLessonsInSchedule(LessonMatchParams p)
    {
        {
            bool yielded = false;
            foreach (var x in MatchLessonsImpl(p, p.Filter.SubGroup))
            {
                yield return x;
                yielded = true;
            }
            if (yielded)
            {
                yield break;
            }
        }

        {
            var subGroup = p.Filter.SubGroup;
            if (p.Filter.SubGroup != SubGroup.All)
            {
                subGroup = SpecialSubGroups.Optional;
            }
            foreach (var x in MatchLessonsImpl(p, subGroup))
            {
                yield return x;
            }
        }
    }

    private static IEnumerable<AnyLessonId> MatchLessonsImpl(
        LessonMatchParams p,
        SubGroup subGroup)
    {
        var lessonsOfCourse = p.Lookup[p.Filter.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (lesson.Lesson.SubGroup != subGroup)
            {
                continue;
            }

            if (p.Filter.Groups.IsWildcard)
            {
                if (!p.Filter.Groups.Value.IsSubSetOf(lesson.Lesson.Groups))
                {
                    continue;
                }
            }
            else
            {
                Debug.Assert(p.Filter.Groups.Value.IsSingleGroup);
                if (!lesson.Lesson.Groups.Contains(p.Filter.Groups.Value[0]))
                {
                    continue;
                }
            }

            yield return lessonId;
        }
    }
}
