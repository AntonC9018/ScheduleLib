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
        return MatchLessonCandidates(p)
            .Where(id => !IsUnsafeIntakeLab(p.Filter.Groups, p.Schedule.Get(id).Lesson));
    }

    internal static List<AnyLessonId> FindUnsafeIntakeLabs(LessonMatchParams p)
    {
        return MatchLessonCandidates(p)
            .Where(id => IsUnsafeIntakeLab(p.Filter.Groups, p.Schedule.Get(id).Lesson))
            .ToList();
    }

    private static bool IsUnsafeIntakeLab(FoundGroups groups, LessonData lesson)
    {
        bool conditionResult;
        if (lesson.Type == LessonType.Lab)
        {
            if (groups.IsWildcard)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = groups.Value.IsSingleGroup;
                conditionResult = !conditionResult;
            }
        }
        else
        {
            conditionResult = false;
        }

        return conditionResult;
    }

    internal static IEnumerable<AnyLessonId> MatchLessonCandidates(LessonMatchParams p)
    {
        {
            bool yielded = false;
            foreach (var x in MatchLessonsImpl(p, p.Filter.GroupPartition))
            {
                yield return x;
                yielded = true;
            }

            bool CalculateConditionResult()
            {
                if (yielded)
                {
                    return true;
                }
                return p.Filter.Groups.IsCombined;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
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

    internal static bool MatchesPartition(
        Schedule schedule,
        LessonData lesson,
        GroupPartitionKey partition)
    {
        if (lesson.GroupPartitionKey == partition)
        {
            return true;
        }

        bool CheckCondition()
        {
            if (partition != GroupPartitionKey.All)
            {
                return true;
            }

            if (lesson.Specialization != Specialization.All)
            {
                return true;
            }

            if (lesson.Alternative != Alternative.All)
            {
                return true;
            }

            return lesson.Groups.IsEmpty;
        }

        if (CheckCondition())
        {
            return false;
        }
        // The ordinary registry destination represents the parent's teaching language;
        // another language subgroup has its own destination.
        if (lesson.SubGroup.IsLanguageSubGroup()
            && lesson.Groups.All(group =>
            {
                Language CalculateLang()
                {
                    var get = schedule.Get(group);
                    var lang = get.Language;
                    return lang;
                }

                return schedule.Get(group).Language is Language.Ro or Language.Ru or Language.En
                    && SpecialSubGroups.FromLanguage(CalculateLang()) == lesson.SubGroup;
            }))
        {
            return true;
        }

        return false;
    }

    private static IEnumerable<AnyLessonId> MatchLessonsImpl(
        LessonMatchParams p,
        GroupPartitionKey groupPartition)
    {
        var lessonsOfCourse = p.Lookup[p.Filter.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            if (!p.Schedule.IsLessonActive(lessonId))
            {
                continue;
            }

            var lesson = p.Schedule.Get(lessonId);
            bool condition;
            if (p.Filter.Groups.IsCombined)
            {
                condition = lesson.Lesson.GroupPartitionKey != groupPartition;
            }
            else
            {
                bool matchesPartition = MatchesPartition(p.Schedule,
                    lesson.Lesson,
                    groupPartition);
                condition = !matchesPartition;
            }

            if (condition)
            {
                continue;
            }

            ref readonly var g = ref p.Filter.Groups;

            if (g.IsCombined)
            {
                // Combined registry cohorts can have separate parent-group lessons.
                // A lesson must be entirely within the confirmed resolved parents.
                if (lesson.Lesson.Groups.IsEmpty || !lesson.Lesson.Groups.IsSubSetOf(g.Value))
                {
                    continue;
                }
            }
            else if (g.IsWildcard)
            {
                bool conditionResult;
                if (lesson.Lesson.Groups.IsEmpty)
                {
                    conditionResult = true;
                }
                else
                {
                    if (!g.Value.IsSubSetOf(lesson.Lesson.Groups))
                    {
                        conditionResult = lesson.Lesson.Groups.IsSubSetOf(g.Value);
                        conditionResult = !conditionResult;
                    }
                    else
                    {
                        conditionResult = false;
                    }
                }

                if (conditionResult)
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
