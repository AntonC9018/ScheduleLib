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
    private readonly IWeeklyScheduledDateProvider _dateProvider;
    private readonly ScheduleDateProviderHelper _helper;

    public IEnumerable<LessonWithDate> Get(GetDateTimesParams p)
    {
        foreach (var x in _helper.Iterate(p))
        {
            foreach (var date in _dateProvider.GetDatesForAnyLessons(x.Params))
            {
                yield return new()
                {
                    LessonId = x.LessonId,
                    DateTime = new(date, x.TimeInterval.Start),
                };
            }
        }
    }
}

public partial class AnyLessonSchedulingDateHelper
{
    public static IEnumerable<DateOnly> GetDatesForAnyLessons(
        this IWeeklyScheduledDateProvider dateProvider,
        GetProgrammedParams p)
    {
        if (TryGetWeeklyParams(p) is { } datesParams)
        {
            return dateProvider.Dates(datesParams);
        }
        else if (TryGetSingleDate(p) is { } singleDate)
        {
            if (singleDate.Date is { } d)
            {
                return [d];
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
        GetDateTimesParams p)
    {
        var t = provider.Get(p);
        // TODO: Do this in the previous function immediately?
        t = t.OrderBy(x => x.DateTime);
        return t;
    }
}
