namespace QuizModels;

public sealed class MoodleDateTimeParseException(string dateTimeText)
    : InvalidOperationException($"Cannot parse datetime: {dateTimeText}")
{
    public string DateTimeText { get; } = dateTimeText;
}

public sealed class MoodleQuizPageLayoutException(string missingElement)
    : InvalidOperationException($"Expected Moodle quiz page element '{missingElement}' was not found.")
{
    public string MissingElement { get; } = missingElement;
}

public sealed class InvalidMoodleBreadcrumbException()
    : InvalidOperationException("Expected span or anchor in path");

public sealed class MoodleAttemptsTableNotFoundException()
    : InvalidOperationException("Attempts table not found");

public sealed class MoodleAttemptsTableHeaderNotFoundException()
    : InvalidOperationException("Attempts table header row not found.");

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
