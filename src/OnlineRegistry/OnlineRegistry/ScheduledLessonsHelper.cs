namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonWithDate : IDateTime
{
    public required AnyLessonId LessonId { get; init; }
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
            var timeSlot = lesson.GetTimeSlot();
            var startTime = p.TimeConfig.GetTimeSlotInterval(timeSlot).Start;
            var semester = p.SemesterIntervalProvider.GetSemesterInterval(new()
            {
                Schedule = p.Schedule,
                GroupId = lesson.Lesson.Group,
                Semester = p.Semester,
            });
            var dates = lesson.GetProgrammedDates(new()
            {
                DateProvider = p.DateProvider,
                Schedule = p.Schedule,
                Semester = semester,
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

    private readonly struct GetProgrammedDatesParams
    {
        public required Schedule Schedule { get; init; }
        public required IAllScheduledDateProvider DateProvider { get; init; }
        public required SemesterDateRange Semester { get; init; }
    }

    private static IEnumerable<DateOnly> GetProgrammedDates(
        this AnyLessonAccessor lesson,
        GetProgrammedDatesParams p)
    {
        if (lesson.Weekly is { } weekly)
        {
            var lessonDate = weekly.Date;
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
                if (p.Semester.End < datesParams.To)
                {
                    datesParams.To = p.Semester.End;
                }
                if (p.Semester.Start > datesParams.From)
                {
                    datesParams.From = p.Semester.Start;
                }
            }
            return p.DateProvider.Dates(datesParams);
        }
        else if (lesson.OneTime is { } oneTime)
        {
            var date = oneTime.Date.Date;
            if (p.Semester.Contains(date))
            {
                return [oneTime.Date.Date];
            }
            return [];
        }
        else
        {
            throw Unreachable();
        }
    }
}
