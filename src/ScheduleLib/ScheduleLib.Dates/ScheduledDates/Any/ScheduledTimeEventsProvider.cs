using AutoConstructor.Attributes;

namespace ScheduleLib.Dates;

public readonly record struct TimeEvent(
    AnyLessonId LessonId,
    Event Event,
    TimeSlotInterval TimeInterval);

[AutoConstructor]
public sealed partial class ScheduledTimeEventsProvider
{
    private readonly IWeeklyScheduledEventsProvider _eventsProvider;
    private readonly ScheduleDateProviderHelper _helper;

    public IEnumerable<TimeEvent> Get(GetDateTimesParams p)
    {
        foreach (var x in _helper.Iterate(p))
        {
            foreach (var ev in _eventsProvider.GetProgrammedEvents(x.Params))
            {
                yield return new(
                    x.LessonId,
                    ev,
                    x.TimeInterval);
            }
        }
    }
}

public partial class AnyLessonSchedulingDateHelper
{
    public static IEnumerable<Event> GetProgrammedEvents(
        this IWeeklyScheduledEventsProvider eventsProvider,
        GetProgrammedParams p)
    {
        if (p.TryGetWeeklyParams() is { } datesParams)
        {
            return eventsProvider.Events(datesParams);
        }
        if (p.TryGetSingleDate() is { } singleDate)
        {
            if (singleDate.Date is { } d)
            {
                return [Event.CreateSingle(d)];
            }
            return [];
        }
        throw Unreachable();
    }
}
