namespace ScheduleLib.Helper.Parsing;

public sealed class LexerEndOfInputException : InvalidOperationException
{
    public LexerEndOfInputException(int requestedOffset, int bufferedTokenCount)
        : base($"Not enough tokens: requested offset {requestedOffset}, buffered {bufferedTokenCount}.")
    {
        RequestedOffset = requestedOffset;
        BufferedTokenCount = bufferedTokenCount;
    }

    public int RequestedOffset { get; }
    public int BufferedTokenCount { get; }
}

public sealed class LexerScopeDisplacementMismatchException(string currentTokenText)
    : InvalidOperationException(
        $"Cannot apply displacement to this parser, because the lexer is on a different line now (token '{currentTokenText}').")
{
    public string CurrentTokenText { get; } = currentTokenText;
}
