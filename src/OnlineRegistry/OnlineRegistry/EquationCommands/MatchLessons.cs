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
    public required CourseId CourseId { get; init; }
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required LessonsByCourseMap Lookup { get; init; }
    public required Schedule Schedule { get; init; }
}


internal static class MatchLessonHelper
{
    internal static IEnumerable<RegularLessonId> MatchLessonsInSchedule(LessonMatchParams p)
    {
        var lessonsOfCourse = p.Lookup[p.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (!lesson.Lesson.Groups.Contains(p.GroupId))
            {
                continue;
            }
            if (lesson.Lesson.SubGroup != p.SubGroup)
            {
                continue;
            }

            yield return lessonId;
        }
    }
}
