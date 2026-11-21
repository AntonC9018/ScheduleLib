namespace ScheduleLib.Generation.Ics;

/// <remarks>
/// <see cref="Start"/> and <see cref="End"/> are local wall-clock times in the
/// calendar's timezone (<see cref="IcsCalendar.TimeZoneId"/>): they are written
/// with a TZID parameter, not converted to UTC.
/// </remarks>
public sealed record IcsEvent
{
    public required string Summary { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }
    public required DateTime Start { get; init; }
    public required DateTime End { get; init; }

    public string? Uid { get; init; }
}

public sealed record IcsCalendar
{
    public required string Name { get; init; }

    public string TimeZoneId { get; init; } = "Europe/Chisinau";

    public required IReadOnlyList<IcsEvent> Events { get; init; }
}
