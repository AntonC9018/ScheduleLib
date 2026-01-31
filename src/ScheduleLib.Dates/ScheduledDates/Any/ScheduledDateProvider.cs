namespace ScheduleLib.Dates;

public struct GetScheduledDatesParams()
{
    public required Parity Parity { get; set; }
    public required DayOfWeek Day { get; set; }
    public DateOnly From { get; set; } = DateOnly.MinValue;
    public DateOnly To { get; set; } = DateOnly.MaxValue;
}

