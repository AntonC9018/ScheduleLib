using AutoConstructor.Attributes;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonWithDate : IDateTime
{
    public required AnyLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
}

[AutoConstructor]
public sealed partial class ScheduledDateTimeProvider
{
    private readonly Schedule _schedule;
    private readonly LessonTimeConfig _timeConfig;
    private readonly IAllScheduledDateProvider _dateProvider;
    private readonly SemesterIntervalProvider _semesterIntervalProvider;

    public readonly struct Params
    {
        public required IEnumerable<AnyLessonId> Lessons { get; init; }
        public required Semester Semester { get; init; }
    }

    public IEnumerable<LessonWithDate> Get(Params p)
    {
        foreach (var lessonId in p.Lessons)
        {
            var lesson = _schedule.Get(lessonId);
            var timeSlot = lesson.GetTimeSlot();
            var startTime = _timeConfig.GetTimeSlotInterval(timeSlot).Start;
            var semester = _semesterIntervalProvider.GetSemesterInterval(new()
            {
                Schedule = _schedule,
                GroupId = lesson.Lesson.Group,
                Semester = p.Semester,
            });
            var dates = GetProgrammedDates(lesson, new()
            {
                DateProvider = _dateProvider,
                Schedule = _schedule,
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
        AnyLessonAccessor lesson,
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

public static class ScheduledDateTimeProviderExtensions
{
    public static IEnumerable<LessonWithDate> GetSorted(
        this ScheduledDateTimeProvider provider,
        ScheduledDateTimeProvider.Params p)
    {
        var t = provider.Get(p);
        // TODO: Do this in the previous function immediately?
        t = t.OrderBy(x => x.DateTime);
        return t;
    }
}
