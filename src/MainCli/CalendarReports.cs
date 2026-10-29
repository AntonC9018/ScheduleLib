using System.Collections.Immutable;

namespace ScheduleLib.Cli;

public readonly record struct CalendarPlanStep(
    string Id,
    string Action,
    string? CalendarId,
    int? DesiredEventIndex);

public readonly record struct CalendarReport(
    string Id,
    string Status,
    object? Item,
    ICliMessage Human);

public readonly record struct CalendarActionMessage(CalendarActionOutcome Outcome) : ICliMessage
{
    public string Format(IFormatProvider provider)
    {
        return string.Create(
            provider,
            $"{Outcome.Action}: {Outcome.State}; calendar {Outcome.CalendarId}; event {Outcome.EventId}; desired index {Outcome.DesiredEventIndex}");
    }
}

public readonly record struct CalendarNotAttemptedMessage(string Action, int? DesiredEventIndex) : ICliMessage
{
    public string Format(IFormatProvider provider) =>
        string.Create(provider, $"{Action}: not-attempted; desired index {DesiredEventIndex}");
}

public readonly record struct CalendarDeletedEventMessage(string Id, string? Summary) : ICliMessage
{
    public string Format(IFormatProvider provider) => $"Delete event {Id}: {Summary}";
}

public readonly record struct CalendarDesiredEventMessage(
    int Index,
    string? Summary,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    ImmutableArray<string> Recurrence) : ICliMessage
{
    public string Format(IFormatProvider provider)
    {
        return string.Create(
            provider,
            $"Create event [{Index}]: {Summary}; {Start}–{End}; {string.Join(", ", Recurrence)}");
    }
}

public readonly record struct CalendarDeleteAction(string CalendarId, int EventCount) : ICliMessage
{
    public string Format(IFormatProvider provider) =>
        string.Create(provider, $"Delete calendar {CalendarId} and its {EventCount} events");
}

public readonly record struct CalendarCreateAction(string CalendarName) : ICliMessage
{
    public string Format(IFormatProvider provider) => $"Create calendar {CalendarName} with a new ID";
}

public readonly record struct CalendarCreateEventsAction(int EventCount) : ICliMessage
{
    public string Format(IFormatProvider provider) =>
        string.Create(provider, $"Create {EventCount} events with new IDs");
}
