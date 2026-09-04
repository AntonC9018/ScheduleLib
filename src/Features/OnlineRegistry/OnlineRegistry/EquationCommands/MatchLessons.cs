using System.Diagnostics;
using ScheduleLib.Builders;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonSearchFilter
{
    public readonly CourseId CourseId;
    public readonly FoundGroups Groups;
    public GroupPartitionKey GroupPartition { get; init; }

    public LessonSearchFilter(
        CourseId courseId,
        FoundGroups groups,
        GroupPartitionKey groupPartition)
    {
        CourseId = courseId;
        Groups = groups;
        GroupPartition = groupPartition;
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
            foreach (var x in MatchLessonsImpl(p, p.Filter.GroupPartition))
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
            var groupPartition = p.Filter.GroupPartition;
            if (groupPartition != GroupPartitionKey.All)
            {
                groupPartition = new(SpecialSubGroups.Optional, Specialization.All);
            }
            foreach (var x in MatchLessonsImpl(p, groupPartition))
            {
                yield return x;
            }
        }
    }

    private static IEnumerable<AnyLessonId> MatchLessonsImpl(
        LessonMatchParams p,
        GroupPartitionKey groupPartition)
    {
        var lessonsOfCourse = p.Lookup[p.Filter.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (lesson.Lesson.GroupPartitionKey != groupPartition)
            {
                continue;
            }

            ref readonly var g = ref p.Filter.Groups;

            if (g.IsWildcard)
            {
                if (!g.Value.IsSubSetOf(lesson.Lesson.Groups))
                {
                    continue;
                }
            }
            else
            {
                Debug.Assert(g.Value.IsSingleGroup);
                if (!lesson.Lesson.Groups.Contains(g.Value[0]))
                {
                    continue;
                }
            }

            yield return lessonId;
        }
    }
}
