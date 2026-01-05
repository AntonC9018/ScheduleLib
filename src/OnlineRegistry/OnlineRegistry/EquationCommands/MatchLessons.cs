using System.Diagnostics;
using ScheduleLib.Builders;

namespace ScheduleLib.OnlineRegistry;

internal readonly record struct LessonMatchParams
{
    public readonly CourseId CourseId;
    public readonly FoundGroups Groups;
    public SubGroup SubGroup { get; init; }
    public readonly LessonsByCourseMap Lookup;
    public readonly Schedule Schedule;

    public LessonMatchParams(
        CourseId courseId,
        FoundGroups groups,
        SubGroup subGroup,
        LessonsByCourseMap lookup,
        Schedule schedule)
    {
        CourseId = courseId;
        Groups = groups;
        SubGroup = subGroup;
        Lookup = lookup;
        Schedule = schedule;
    }
}

internal static class MatchLessonHelper
{
    internal static IEnumerable<AnyLessonId> MatchLessonsInSchedule(LessonMatchParams p)
    {
        bool yielded = false;
        foreach (var x in MatchLessonsImpl(p))
        {
            yield return x;
            yielded = true;
        }
        if (yielded)
        {
            yield break;
        }

        if (p.SubGroup != SubGroup.All)
        {
            p = p with
            {
                SubGroup = SpecialSubGroups.Optional,
            };
        }
        foreach (var x in MatchLessonsImpl(p))
        {
            yield return x;
        }
    }

    private static IEnumerable<AnyLessonId> MatchLessonsImpl(
        LessonMatchParams p)
    {
        var lessonsOfCourse = p.Lookup[p.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (lesson.Lesson.SubGroup != p.SubGroup)
            {
                continue;
            }

            if (p.Groups.IsWildcard)
            {
                if (!p.Groups.Value.IsSubSetOf(lesson.Lesson.Groups))
                {
                    continue;
                }
            }
            else
            {
                Debug.Assert(p.Groups.Value.IsSingleGroup);
                if (!lesson.Lesson.Groups.Contains(p.Groups.Value[0]))
                {
                    continue;
                }
            }

            yield return lessonId;
        }
    }
}
