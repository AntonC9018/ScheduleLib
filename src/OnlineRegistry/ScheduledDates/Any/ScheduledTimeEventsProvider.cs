using AutoConstructor.Attributes;

namespace ScheduleLib.OnlineRegistry;

public readonly record struct TimeEvent(
    AnyLessonId LessonId,
    Event Event,
    TimeOnly Time);

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
                yield return new(x.LessonId, ev, x.Time);
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
        if (TryGetWeeklyParams(p) is { } datesParams)
        {
            return eventsProvider.Events(datesParams);
        }
        else if (TryGetSingleDate(p) is { } singleDate)
        {
            if (singleDate.Date is { } d)
            {
                return [Event.CreateSingle(d)];
            }
            return [];
        }
        else
        {
            throw Unreachable();
        }
    }
}
