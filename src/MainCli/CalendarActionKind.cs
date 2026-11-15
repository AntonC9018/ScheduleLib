using System.Text.Json.Serialization;
using ScheduleLib.Helper.JsonConverters;

namespace ScheduleLib.Cli;

[JsonConverter(typeof(CalendarActionKindJsonConverter))]
public enum CalendarActionKind
{
    DeleteCalendar,
    CreateCalendar,
    CreateEvent,
}

public static class CalendarActionKindExtensions
{
    public static string ToWire(this CalendarActionKind action)
    {
        return action switch
        {
            CalendarActionKind.DeleteCalendar => "delete calendar",
            CalendarActionKind.CreateCalendar => "create calendar",
            CalendarActionKind.CreateEvent => "create event",
            _ => throw new ArgumentOutOfRangeException(
                nameof(action),
                action,
                null),
        };
    }
}

public sealed class CalendarActionKindJsonConverter : WireNameJsonConverter<CalendarActionKind>
{
    protected override string ToWire(CalendarActionKind value) => value.ToWire();
}
