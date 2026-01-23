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

            // TODO:
            // Gonna need to add more logic like this.
            // Make an abstraction.
            // It might be better to add group remaps at the schedule level.

            var subGroup = p.Filter.SubGroup.Value;
            // Roman -> Letter mapping
            {
                var roman = NumberHelper.FromRoman(subGroup);
                if (roman is { } subGroupNum
                    && subGroupNum is >= 1 and <= 10)
                {
                    var subGroupLetter = ('a' + subGroupNum - 1).ToString();
                    foreach (var x in MatchLessonsImpl(p, new(subGroupLetter)))
                    {
                        yield return x;
                        yielded = true;
                    }
                }
            }
            // Letter -> Roman mapping
            {
                if (subGroup != null
                    && subGroup.Length == 1
                    && char.IsAsciiLetterLower(subGroup[0]))
                {
                    var subGroupNum = subGroup[0] - 'a' + 1;
                    var roman = NumberHelper.ToRoman(subGroupNum);
                    foreach (var x in MatchLessonsImpl(p, new(roman)))
                    {
                        yield return x;
                        yielded = true;
                    }
                }
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
