namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonWithDate : IDateTime
{
    public required WeeklyLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
}

public static class ScheduledLessonsHelper
{
    public static IEnumerable<LessonWithDate> GetSortedScheduledLessons(
        GetDateTimesOfScheduledLessonsParams p)
    {
        var t = GetDateTimesOfScheduledLessons(p);
        // TODO: Do this in the previous function immediately?
        t = t.OrderBy(x => x.DateTime);
        return t;
    }

    private static IEnumerable<LessonWithDate> GetDateTimesOfScheduledLessons(
        GetDateTimesOfScheduledLessonsParams p)
    {
        foreach (var lessonId in p.Lessons)
        {
            var lesson = p.Schedule.Get(lessonId);

            var lessonDate = lesson.Date;
            var timeSlot = lessonDate.TimeSlot;
            var startTime = p.TimeConfig.GetTimeSlotInterval(timeSlot).Start;

            var datesParams = new GetScheduledDatesParams
            {
                Day = lessonDate.DayOfWeek,
                Parity = lessonDate.Parity,
            };
            {
                var periodId = lessonDate.Period;
                if (periodId.IsSpecified)
                {
                    var period = p.Schedule.Get(periodId);

                    datesParams.From = period.Start;
                    if (period.End is { } periodEnd)
                    {
                        datesParams.To = periodEnd;
                    }
                }
            }
            {
                var semester = p.SemesterIntervalProvider.GetSemesterInterval(new()
                {
                    Schedule = p.Schedule,
                    GroupId = lesson.Lesson.Group,
                    Semester = p.Semester,
                });
                if (semester.End < datesParams.To)
                {
                    datesParams.To = semester.End;
                }
                if (semester.Start > datesParams.From)
                {
                    datesParams.From = semester.Start;
                }
            }

            var dates = p.DateProvider.Dates(datesParams);
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
}
