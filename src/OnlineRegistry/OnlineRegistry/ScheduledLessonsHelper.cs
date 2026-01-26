using AutoConstructor.Attributes;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct LessonWithDate : IDateTime
{
    public required AnyLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
}

public readonly record struct ProgrammedRepeatableLesson
{
    public readonly AnyLessonId Id;
    public readonly Event Item;
    public readonly TimeOnly Time;

    public ProgrammedRepeatableLesson(
        AnyLessonId id,
        in Event item,
        TimeOnly time)
    {
        Id = id;
        Item = item;
        Time = time;
    }

    public readonly IEnumerable<DateTime> DateTimes()
    {
        foreach (var it in Item)
        {
            yield return new DateTime(it, Time);
        }
    }
}

file readonly record struct LessonHelper
{
    public readonly AnyLessonId LessonId;
    public readonly TimeOnly StartTime;

    public LessonHelper(
        AnyLessonId lessonId,
        TimeOnly startTime)
    {
        LessonId = lessonId;
        StartTime = startTime;
    }

    public LessonWithDate DateReturn(DateOnly date)
    {
        return new()
        {
            LessonId = LessonId,
            DateTime = new(date, StartTime),
        };
    }
}

[AutoConstructor]
public sealed partial class ScheduledDateTimeProvider
{
    private readonly Schedule _schedule;
    private readonly LessonTimeConfig _timeConfig;
    private readonly IAllScheduledDateProvider _dateProvider;
    private readonly CurrentYearSemesterIntervalProvider _semesterIntervalProvider;

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
            var helper = new LessonHelper(lessonId, startTime);

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
                yield return helper.DateReturn(date);
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
                if (p.Semester.EndInclusive < datesParams.To)
                {
                    datesParams.To = p.Semester.EndInclusive;
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
