namespace ScheduleLib.Generation.Ics;

/// <summary>
/// One VEVENT in a generated calendar.
/// </summary>
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

    /// <summary>
    /// Overrides the default UID, which is a stable hash of the event content.
    /// </summary>
    public string? Uid { get; init; }
}

/// <summary>
/// One VCALENDAR: a named set of timed events sharing one timezone.
/// </summary>
public sealed record IcsCalendar
{
    /// <summary>The calendar display name (X-WR-CALNAME).</summary>
    public required string Name { get; init; }

    /// <summary>An IANA timezone id, e.g. Europe/Chisinau.</summary>
    public string TimeZoneId { get; init; } = "Europe/Chisinau";

    public required IReadOnlyList<IcsEvent> Events { get; init; }
}
