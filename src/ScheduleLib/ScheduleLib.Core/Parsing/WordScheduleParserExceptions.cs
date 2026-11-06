using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing.WordDoc;

public sealed class UnsupportedMergeCellCommandException(EnumValue<MergedCellValues>? command)
    : NotSupportedException($"Unsupported merge cell command: {command}")
{
    public MergedCellValues? Command { get; } = command?.Value;
}

public sealed class InvalidScheduleDayNameException(string dayName)
    : NotSupportedException($"The day {dayName} is invalid")
{
    public string DayName { get; } = dayName;
}

public sealed class UnequalScheduleLessonDurationException(TimeSpan defaultDuration)
    : NotSupportedException(
        $"The lesson durations must all be equal to the default duration ({defaultDuration} minutes)")
{
    public TimeSpan DefaultDuration { get; } = defaultDuration;
}

public sealed class UnsupportedLessonSyntaxException(string syntax, WrongFormatException innerException)
    : NotSupportedException($"Unsupported syntax when parsing {syntax}", innerException)
{
    public string Syntax { get; } = syntax;
}

public sealed class UnexpectedEmptyHeaderColumnCountException(int expectedCount)
    : NotSupportedException($"Expected {expectedCount} empty header columns")
{
    public int ExpectedCount { get; } = expectedCount;
}
