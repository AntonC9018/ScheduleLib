namespace QuizModels;

public sealed class MoodleDateTimeParseException(string dateTimeText)
    : InvalidOperationException($"Cannot parse datetime: {dateTimeText}")
{
    public string DateTimeText { get; } = dateTimeText;
}

public sealed class UnknownMoodleSummaryTableHeaderException(string header)
    : InvalidOperationException($"Unknown summary table header: {header}")
{
    public string Header { get; } = header;
}
