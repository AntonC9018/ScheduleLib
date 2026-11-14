using ScheduleLib.Builders;

namespace ScheduleLib.Dates;

public enum ParityWeekIntervalError
{
    TrailingText,
    InvalidDayCount,
    OutOfOrderWeek,
    NotStartingOnMonday,
    MissingDayNumber,
    DayNumberTooLong,
    MissingMonthName,
    UnknownMonthName,
    InvalidYear,
}

public sealed class ParityWeekIntervalException : ScheduleBuildException
{
    private ParityWeekIntervalException(
        ParityWeekIntervalError error,
        string rawText,
        DateOnly? startDate = null,
        DateOnly? endDate = null,
        DateOnly? previousWeekEnd = null)
        : base(CreateMessage(
            error: error,
            rawText: rawText,
            startDate: startDate,
            endDate: endDate,
            previousWeekEnd: previousWeekEnd))
    {
        Error = error;
        RawText = rawText;
        StartDate = startDate;
        EndDate = endDate;
        PreviousWeekEnd = previousWeekEnd;
    }

    public ParityWeekIntervalError Error { get; }
    public string RawText { get; }
    public DateOnly? StartDate { get; }
    public DateOnly? EndDate { get; }
    public DateOnly? PreviousWeekEnd { get; }

    public static ParityWeekIntervalException ForTrailingText(string weekText)
    {
        return new(
            error: ParityWeekIntervalError.TrailingText,
            rawText: weekText);
    }

    public static ParityWeekIntervalException ForInvalidDayCount(
        string weekText,
        DateOnly start,
        DateOnly end)
    {
        return new(
            error: ParityWeekIntervalError.InvalidDayCount,
            rawText: weekText,
            startDate: start,
            endDate: end);
    }

    public static ParityWeekIntervalException ForOutOfOrderWeek(
        string weekText,
        DateOnly start,
        DateOnly previousEnd)
    {
        return new(
            error: ParityWeekIntervalError.OutOfOrderWeek,
            rawText: weekText,
            startDate: start,
            previousWeekEnd: previousEnd);
    }

    public static ParityWeekIntervalException ForNotStartingOnMonday(string weekText, DateOnly start)
    {
        return new(
            error: ParityWeekIntervalError.NotStartingOnMonday,
            rawText: weekText,
            startDate: start);
    }

    public static ParityWeekIntervalException ForMissingDayNumber(string remainingText)
    {
        return new(
            error: ParityWeekIntervalError.MissingDayNumber,
            rawText: remainingText);
    }

    public static ParityWeekIntervalException ForDayNumberTooLong(string dayText)
    {
        return new(
            error: ParityWeekIntervalError.DayNumberTooLong,
            rawText: dayText);
    }

    public static ParityWeekIntervalException ForMissingMonthName(string remainingText)
    {
        return new(
            error: ParityWeekIntervalError.MissingMonthName,
            rawText: remainingText);
    }

    public static ParityWeekIntervalException ForUnknownMonthName(string monthName)
    {
        return new(
            error: ParityWeekIntervalError.UnknownMonthName,
            rawText: monthName);
    }

    public static ParityWeekIntervalException ForInvalidYear(string remainingText)
    {
        return new(
            error: ParityWeekIntervalError.InvalidYear,
            rawText: remainingText);
    }

    private static string CreateMessage(
        ParityWeekIntervalError error,
        string rawText,
        DateOnly? startDate,
        DateOnly? endDate,
        DateOnly? previousWeekEnd)
    {
        _ = endDate;
        return error switch
        {
            ParityWeekIntervalError.TrailingText => $"Invalid date range format: '{rawText}'.",
            ParityWeekIntervalError.InvalidDayCount => $"The date range '{rawText}' must have 6 days in total.",
            ParityWeekIntervalError.OutOfOrderWeek =>
                $"The week interval '{rawText}' must be in chronological order after {previousWeekEnd}.",
            ParityWeekIntervalError.NotStartingOnMonday =>
                $"The week interval '{rawText}' must start on a Monday (got {startDate?.DayOfWeek}).",
            ParityWeekIntervalError.MissingDayNumber => $"Day number expected in '{rawText}'.",
            ParityWeekIntervalError.DayNumberTooLong => $"Day number '{rawText}' is too long (max 2 digits).",
            ParityWeekIntervalError.MissingMonthName => $"Month name expected in '{rawText}'.",
            ParityWeekIntervalError.UnknownMonthName => $"Month name '{rawText}' not recognized.",
            ParityWeekIntervalError.InvalidYear => $"Year not parsed in '{rawText}'.",
            _ => $"Invalid parity week interval '{rawText}' ({error}).",
        };
    }
}

public enum ParityValueError
{
    TrailingText,
    UnrecognizedValue,
}

public sealed class InvalidParityValueException : ScheduleBuildException
{
    private InvalidParityValueException(
        ParityValueError error,
        string rawText)
        : base(CreateMessage(error, rawText))
    {
        Error = error;
        RawText = rawText;
    }

    public ParityValueError Error { get; }
    public string RawText { get; }

    public static InvalidParityValueException ForTrailingText(string parityText)
    {
        return new(
            error: ParityValueError.TrailingText,
            rawText: parityText);
    }

    public static InvalidParityValueException ForUnrecognizedValue(string parityText)
    {
        return new(
            error: ParityValueError.UnrecognizedValue,
            rawText: parityText);
    }

    private static string CreateMessage(ParityValueError error, string rawText)
    {
        return error switch
        {
            ParityValueError.TrailingText => $"Invalid parity format: '{rawText}'.",
            ParityValueError.UnrecognizedValue => $"Parity not recognized: '{rawText}'.",
            _ => $"Invalid parity value '{rawText}' ({error}).",
        };
    }
}

public enum ParityHeaderError
{
    MissingHeaderRow,
    MissingWeekHeader,
    UnexpectedWeekHeaderName,
    MissingMiddleHeader,
    MissingParityHeader,
    UnexpectedParityHeaderName,
    UnexpectedExtraHeaderColumns,
}

public sealed class ParityDocumentHeaderException : ScheduleBuildException
{
    private ParityDocumentHeaderException(
        ParityHeaderError error,
        string? expectedColumn = null,
        string? actualText = null)
        : base(CreateMessage(
            error: error,
            expectedColumn: expectedColumn,
            actualText: actualText))
    {
        Error = error;
        ExpectedColumn = expectedColumn;
        ActualText = actualText;
    }

    public ParityHeaderError Error { get; }
    public string? ExpectedColumn { get; }
    public string? ActualText { get; }

    public static ParityDocumentHeaderException ForMissingHeaderRow()
    {
        return new(error: ParityHeaderError.MissingHeaderRow);
    }

    public static ParityDocumentHeaderException ForMissingWeekHeader()
    {
        return new(
            error: ParityHeaderError.MissingWeekHeader,
            expectedColumn: "Saptamana");
    }

    public static ParityDocumentHeaderException ForUnexpectedWeekHeaderName(string actualText)
    {
        return new(
            error: ParityHeaderError.UnexpectedWeekHeaderName,
            expectedColumn: "Saptamana",
            actualText: actualText);
    }

    public static ParityDocumentHeaderException ForMissingMiddleHeader()
    {
        return new(error: ParityHeaderError.MissingMiddleHeader);
    }

    public static ParityDocumentHeaderException ForMissingParityHeader()
    {
        return new(
            error: ParityHeaderError.MissingParityHeader,
            expectedColumn: "Paritatea");
    }

    public static ParityDocumentHeaderException ForUnexpectedParityHeaderName(string actualText)
    {
        return new(
            error: ParityHeaderError.UnexpectedParityHeaderName,
            expectedColumn: "Paritatea",
            actualText: actualText);
    }

    public static ParityDocumentHeaderException ForUnexpectedExtraHeaderColumns()
    {
        return new(error: ParityHeaderError.UnexpectedExtraHeaderColumns);
    }

    private static string CreateMessage(
        ParityHeaderError error,
        string? expectedColumn,
        string? actualText)
    {
        _ = expectedColumn;
        return error switch
        {
            ParityHeaderError.MissingHeaderRow => "No header found.",
            ParityHeaderError.MissingWeekHeader => "No cell found for the week header.",
            ParityHeaderError.UnexpectedWeekHeaderName =>
                $"Week header should be first; unexpected name '{actualText}'.",
            ParityHeaderError.MissingMiddleHeader => "An insignificant column must follow after the week column.",
            ParityHeaderError.MissingParityHeader => "No cell found for the parity header.",
            ParityHeaderError.UnexpectedParityHeaderName =>
                $"Parity header should be third; unexpected name '{actualText}'.",
            ParityHeaderError.UnexpectedExtraHeaderColumns => "Parity header must only have 3 columns.",
            _ => $"Invalid parity document header ({error}).",
        };
    }
}

public enum ParityRowCellError
{
    MissingCell,
    UnexpectedExtraCell,
}

public sealed class ParityRowCellException : ScheduleBuildException
{
    private ParityRowCellException(ParityRowCellError error, string message)
        : base(message)
    {
        Error = error;
    }

    public ParityRowCellError Error { get; }

    public static ParityRowCellException ForMissingCell()
    {
        return new(ParityRowCellError.MissingCell, "Expected a cell in parity table row.");
    }

    public static ParityRowCellException ForUnexpectedExtraCell()
    {
        return new(ParityRowCellError.UnexpectedExtraCell, "Expected no more cells in parity table row.");
    }
}
