using ScheduleLib;
using ScheduleLib.Builders;

namespace ReaderApp.OnlineRegistry;

internal readonly struct GetDateTimesOfScheduledLessonsParams
{
    public required IEnumerable<RegularLessonId> Lessons { get; init; }
    public required Schedule Schedule { get; init; }
    public required LessonTimeConfig TimeConfig { get; init; }
    public required IAllScheduledDateProvider DateProvider { get; init; }
}

internal readonly record struct LessonInstance
{
    public required RegularLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
    // TODO: add topics
}

internal readonly record struct LessonMatchParams
{
    public required CourseId CourseId { get; init; }
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required LessonsByCourseMap Lookup { get; init; }
    public required Schedule Schedule { get; init; }
}

public static class MissingLessonDetection
{
    internal static IEnumerable<LessonInstance> GetDateTimesOfScheduledLessons(
        GetDateTimesOfScheduledLessonsParams p)
    {
        foreach (var lessonId in p.Lessons)
        {
            var lesson = p.Schedule.Get(lessonId);
            var lessonDate = lesson.Date;

            var timeSlot = lessonDate.TimeSlot;
            var startTime = p.TimeConfig.GetTimeSlotInterval(timeSlot).Start;

            var dates = p.DateProvider.Dates(new()
            {
                Day = lessonDate.DayOfWeek,
                Parity = lessonDate.Parity,
            });
            foreach (var date in dates)
            {
                var dateTime = new DateTime(
                    date: date,
                    time: startTime);
                yield return new()
                {
                    LessonId = lessonId,
                    DateTime = dateTime,
                };
            }
        }
    }

    internal static IEnumerable<RegularLessonId> MatchLessons(LessonMatchParams p)
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
