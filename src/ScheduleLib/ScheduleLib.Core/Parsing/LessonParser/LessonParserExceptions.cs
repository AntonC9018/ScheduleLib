using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing.Lesson;

public sealed class LessonParserBaseStackPopException()
    : InvalidOperationException("Cannot pop the base layer of the parsing state stack.");

public sealed class LessonParserUnbalancedStackException : InvalidOperationException
{
    public LessonParserUnbalancedStackException(int expectedStackDepth, int actualStackDepth)
        : base(
            $"Parser iteration unbalanced state stack: expected depth {expectedStackDepth}, actual {actualStackDepth}.")
    {
        ExpectedStackDepth = expectedStackDepth;
        ActualStackDepth = actualStackDepth;
    }

    public int ExpectedStackDepth { get; }
    public int ActualStackDepth { get; }
}

public sealed class LessonParserInfiniteLoopException(string stepName, LexerPosition position)
    : InvalidOperationException(
        $"Parser iteration made no progress at step '{stepName}' and position {position.Value}.")
{
    public string StepName { get; } = stepName;
    public LexerPosition Position { get; } = position;
}
