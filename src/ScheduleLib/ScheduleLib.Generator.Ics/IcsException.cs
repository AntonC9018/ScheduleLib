namespace ScheduleLib.Generation.Ics;

public sealed class MissingVTimeZoneDefinitionException(string timeZoneId)
    : NotSupportedException($"No VTIMEZONE definition for '{timeZoneId}'.")
{
    public string TimeZoneId { get; } = timeZoneId;
}
