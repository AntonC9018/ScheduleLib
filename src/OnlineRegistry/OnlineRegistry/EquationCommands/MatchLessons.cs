using ScheduleLib.Builders;

namespace ScheduleLib.OnlineRegistry;

public readonly struct GetDateTimesOfScheduledLessonsParams
{
    public required IEnumerable<RegularLessonId> Lessons { get; init; }
    public required Schedule Schedule { get; init; }
    public required LessonTimeConfig TimeConfig { get; init; }
    public required IAllScheduledDateProvider DateProvider { get; init; }
    public required SemesterIntervalProvider SemesterIntervalProvider { get; init; }
    public required Semester Semester { get; init; }
}

internal readonly record struct LessonMatchParams
{
    public readonly CourseId CourseId;
    public readonly FoundGroups Groups;
    public readonly SubGroup SubGroup;
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
    internal static IEnumerable<RegularLessonId> MatchLessonsInSchedule(LessonMatchParams p)
    {
        var lessonsOfCourse = p.Lookup[p.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (p.Groups.IsWildcard)
            {
                if (!lesson.Lesson.Groups.IsSetEquals(p.Groups.Value))
                {
                    continue;
                }
            }
            else
            {
                if (!lesson.Lesson.Groups.Contains(p.Groups.Value[0]))
                {
                    continue;
                }
            }

            if (lesson.Lesson.SubGroup != p.SubGroup)
            {
                continue;
            }

            yield return lessonId;
        }
    }
}
