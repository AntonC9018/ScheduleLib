namespace QuizModels;

/// <summary>
/// One entry of the Moodle page breadcrumb. The last entry, the current page,
/// has no link.
/// </summary>
public readonly record struct MoodleBreadcrumbItem(string Name, string? Href);

public sealed class QuizAttemptsPage
{
    public required List<QuizAttempt> Attempts { get; init; }
    public required List<MoodleBreadcrumbItem> Path { get; init; }
}

public sealed class QuizAttempt
{
    public string AttemptId { get; set; } = "";
    public required string UserName { get; set; }
    public string? Email { get; set; }
    public AttemptState State { get; set; }
    public DateTime? TimeStart { get; set; }
    public DateTime? TimeFinish { get; set; }
    public TimeSpan? Duration { get; set; }
    public float? Grade { get; set; }
    public string? ReviewLink { get; set; }
    public List<QuestionGrade> QuestionGrades { get; set; } = [];
}

public sealed class QuestionGrade
{
    public int QuestionNumber { get; set; }
    public float? Grade { get; set; }
    public QuestionStatus Status { get; set; }
    public required string ReviewLink { get; set; }
}

public readonly record struct AttemptState(string Value)
{
    public static AttemptState Unknown => new("unknown");
    public static AttemptState Finished => new("Finalizate");
    public static AttemptState InProgress => new("În progres");
    public static AttemptState Abandoned => new("Abandoned");

    public static AttemptState Create(string value)
    {
        return value switch
        {
            "Finalizate" => Finished,
            "În progres" => InProgress,
            "Abandoned" => Abandoned,
            "unknown" => Unknown,
            _ => new AttemptState(value),
        };
    }
}

public readonly record struct QuestionStatus(string Value)
{
    public static QuestionStatus Unknown => new("unknown");
    public static QuestionStatus Correct => new("correct");
    public static QuestionStatus Incorrect => new("incorrect");
    public static QuestionStatus PartiallyCorrect => new("partiallycorrect");
    public static QuestionStatus NotGraded => new("requiresgrading");

    public static QuestionStatus Create(string value)
    {
        return value switch
        {
            "correct" => Correct,
            "incorrect" => Incorrect,
            "partiallycorrect" => PartiallyCorrect,
            "requiresgrading" => NotGraded,
            "unknown" => Unknown,
            _ => new QuestionStatus(value),
        };
    }
}
