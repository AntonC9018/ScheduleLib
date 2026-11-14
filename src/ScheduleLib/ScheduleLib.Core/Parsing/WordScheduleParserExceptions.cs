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

public sealed class UnsupportedWordTableWidthLayoutException(int? expectedColumnCount)
    : NotSupportedException("Unable to infer Word table column widths from headerless table.")
{
    public int? ExpectedColumnCount { get; } = expectedColumnCount;
}

public sealed class WordTableColumnCountOverflowException : NotSupportedException
{
    public WordTableColumnCountOverflowException(int remainingColumns, int cellColSpan)
        : base($"The column count is off: {remainingColumns} remaining columns for colSpan {cellColSpan}.")
    {
        RemainingColumns = remainingColumns;
        CellColSpan = cellColSpan;
    }

    public int RemainingColumns { get; }
    public int CellColSpan { get; }
}

public sealed class MissingDayVerticalMergeException(string cellText)
    : NotSupportedException($"Expected vertical merge metadata on day cell '{cellText}'.")
{
    public string CellText { get; } = cellText;
}

public enum MergedCellBaselineColumn
{
    DayOfWeek,
    TimeSlot,
}

public sealed class MissingMergedCellBaselineException(MergedCellBaselineColumn column)
    : NotSupportedException($"Merged continuation cell in {column} column has no preceding baseline value.")
{
    public MergedCellBaselineColumn Column { get; } = column;
}

public sealed class MissingScheduleDayNameException()
    : NotSupportedException("The day name column must include the day name");

public sealed class EmptyTimeSlotCellException(string cellText)
    : NotSupportedException($"Invalid time slot cell (expected paragraphs): '{cellText}'.")
{
    public string CellText { get; } = cellText;
}

public sealed class InvalidTimeSlotRomanNumeralException(string cellText)
    : NotSupportedException($"The time slot number should be a roman numeral in '{cellText}'.")
{
    public string CellText { get; } = cellText;
}

public sealed class OutOfOrderTimeSlotOrdinalException : NotSupportedException
{
    public OutOfOrderTimeSlotOrdinalException(int expectedOrdinal, int actualOrdinal)
        : base($"The time slot number must be in order: expected {expectedOrdinal}, got {actualOrdinal}.")
    {
        ExpectedOrdinal = expectedOrdinal;
        ActualOrdinal = actualOrdinal;
    }

    public int ExpectedOrdinal { get; }
    public int ActualOrdinal { get; }
}

public sealed class NonConsecutiveTimeSlotException : NotSupportedException
{
    public NonConsecutiveTimeSlotException(TimeOnly expectedStart, TimeOnly actualStart)
        : base($"The time slots must follow each other: expected {expectedStart}, got {actualStart}.")
    {
        ExpectedStart = expectedStart;
        ActualStart = actualStart;
    }

    public TimeOnly ExpectedStart { get; }
    public TimeOnly ActualStart { get; }
}

public sealed class MissingTimeRangeInTimeSlotException()
    : NotSupportedException("The time slot must contain the time range second");

public sealed class EmptyWordScheduleTableException()
    : NotSupportedException("Empty table not supported");

public sealed class MissingWordHeaderGroupColumnsException()
    : NotSupportedException("Header columns expected after the left header");

public sealed class MissingSecondHeaderRowException()
    : NotSupportedException("Expected the second row");

public sealed class MismatchedHeaderColumnWidthException : NotSupportedException
{
    public MismatchedHeaderColumnWidthException(int expectedWidth, int actualWidth)
        : base($"The second row width ({actualWidth}) must match the first row width ({expectedWidth}).")
    {
        ExpectedWidth = expectedWidth;
        ActualWidth = actualWidth;
    }

    public int ExpectedWidth { get; }
    public int ActualWidth { get; }
}

public sealed class MissingSecondFormatDateIntervalException(int semesterNumber)
    : NotSupportedException($"Expected the date interval below semester {semesterNumber} in the table header.")
{
    public int SemesterNumber { get; } = semesterNumber;
}
