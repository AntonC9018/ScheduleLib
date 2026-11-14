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

public sealed class InvalidMoodleBreadcrumbException()
    : InvalidOperationException("Expected span or anchor in path");

public sealed class MoodleAttemptsTableNotFoundException()
    : InvalidOperationException("Attempts table not found");

public sealed class MoodleAttemptsTableHeaderNotFoundException()
    : InvalidOperationException("Failed to find table in html.");

public sealed class MoodleAttemptReviewLinkMissingException(string attemptId)
    : InvalidOperationException($"Review link is missing for attempt '{attemptId}'.")
{
    public string AttemptId { get; } = attemptId;
}

public sealed class MoodleQuestionReviewLinkMissingException(int questionNumber)
    : InvalidOperationException($"Expecting a review link for question {questionNumber}.")
{
    public int QuestionNumber { get; } = questionNumber;
}
