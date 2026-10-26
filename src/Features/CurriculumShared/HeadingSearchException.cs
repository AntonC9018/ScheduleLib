namespace ScheduleLib.Curriculum;

public enum HeadingSearchFailure
{
    Unrecognized,
    PartiallyMatched,
}

public sealed class HeadingSearchException(HeadingSearchFailure failure, string text)
    : NotSupportedException(CreateMessage(failure, text))
{
    public HeadingSearchFailure Failure { get; } = failure;
    public string Text { get; } = text;

    private static string CreateMessage(HeadingSearchFailure failure, string text)
    {
        return failure switch
        {
            HeadingSearchFailure.Unrecognized => $"Unrecognized heading '{text}'.",
            HeadingSearchFailure.PartiallyMatched => $"Partially matched heading '{text}'.",
            _ => throw new ArgumentOutOfRangeException(nameof(failure),
                failure,
                null),
        };
    }
}
