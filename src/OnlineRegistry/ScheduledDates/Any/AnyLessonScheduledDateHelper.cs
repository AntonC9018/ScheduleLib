using AutoConstructor.Attributes;

namespace ScheduleLib.OnlineRegistry;

public readonly struct GetDateTimesParams
{
    public required IEnumerable<AnyLessonId> Lessons { get; init; }
    public required Semester Semester { get; init; }
}

public static partial class AnyLessonSchedulingDateHelper
{
    private static GetScheduledDatesParams? TryGetWeeklyParams(
        this GetProgrammedParams p)
    {
        if (p.LessonAccessor.Weekly is not { } weekly)
        {
            return null;
        }

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
        return datesParams;
    }

    public readonly struct GetProgrammedParams
    {
        public required Schedule Schedule { get; init; }
        public required SemesterDateRange Semester { get; init; }
        public required AnyLessonId Lesson { get; init; }

        public readonly AnyLessonAccessor LessonAccessor => Schedule.Get(Lesson);
    }

    private struct SingleDateResult
    {
        public required DateOnly? Date;
    }

    private static SingleDateResult? TryGetSingleDate(
        this GetProgrammedParams p)
    {
        if (p.LessonAccessor.OneTime is not { } oneTime)
        {
            return null;
        }
        var date = oneTime.Date.Date;
        if (p.Semester.Contains(date))
        {
            return new()
            {
                Date = date,
            };
        }
        return new()
        {
            Date = null,
        };
    }


}

[AutoConstructor]
public sealed partial class ScheduleDateProviderHelper
{
    private readonly Schedule _schedule;
    private readonly LessonTimeConfig _timeConfig;
    private readonly CurrentYearSemesterIntervalProvider _semesterIntervalProvider;

    public readonly record struct Values(
        TimeSlotInterval TimeInterval,
        AnyLessonSchedulingDateHelper.GetProgrammedParams Params);

    public Values Get(AnyLessonId lessonId, Semester sem)
    {
        var lesson = _schedule.Get(lessonId);

        var timeSlot = lesson.GetTimeSlot();
        var timeSlotInterval = _timeConfig.GetTimeSlotInterval(timeSlot);

        var semesterInterval = _semesterIntervalProvider.GetSemesterInterval(new()
        {
            Schedule = _schedule,
            GroupId = lesson.Lesson.Group,
            Semester = sem,
        });
        return new(timeSlotInterval, new()
        {
            Schedule = _schedule,
            Lesson = lessonId,
            Semester = semesterInterval,
        });
    }

    public readonly record struct LessonValues(
        AnyLessonId LessonId,
        TimeSlotInterval TimeInterval,
        AnyLessonSchedulingDateHelper.GetProgrammedParams Params);

    public IEnumerable<LessonValues> Iterate(GetDateTimesParams p)
    {
        foreach (var lessonId in p.Lessons)
        {
            var v = Get(lessonId, p.Semester);
            yield return new(lessonId, v.TimeInterval, v.Params);
        }
    }
}
