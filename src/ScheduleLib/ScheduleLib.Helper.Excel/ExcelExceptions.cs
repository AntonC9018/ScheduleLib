using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Helper.Excel;

public enum ExcelRangeSyntaxError
{
    MissingSeparator,
    TrailingContent,
}

public sealed class InvalidExcelRangeSyntaxException : InvalidOperationException
{
    private InvalidExcelRangeSyntaxException(
        string rangeText,
        ExcelRangeSyntaxError error)
        : base(CreateMessage(rangeText, error))
    {
        RangeText = rangeText;
        Error = error;
    }

    public string RangeText { get; }
    public ExcelRangeSyntaxError Error { get; }

    public static InvalidExcelRangeSyntaxException ForMissingSeparator(string rangeText)
    {
        return new(
            rangeText: rangeText,
            error: ExcelRangeSyntaxError.MissingSeparator);
    }

    public static InvalidExcelRangeSyntaxException ForTrailingContent(string rangeText)
    {
        return new(
            rangeText: rangeText,
            error: ExcelRangeSyntaxError.TrailingContent);
    }

    private static string CreateMessage(string rangeText, ExcelRangeSyntaxError error)
    {
        return error switch
        {
            ExcelRangeSyntaxError.MissingSeparator => $"Expected range syntax with ':' in '{rangeText}'.",
            ExcelRangeSyntaxError.TrailingContent => $"Unexpected trailing content after range '{rangeText}'.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(error),
                error,
                null),
        };
    }
}

public enum ExcelCellPositionError
{
    TrailingContent,
    UnexpectedEnd,
    MissingRowNumber,
}

public sealed class InvalidExcelCellPositionException : InvalidOperationException
{
    private InvalidExcelCellPositionException(
        string cellText,
        ExcelCellPositionError error)
        : base(CreateMessage(cellText, error))
    {
        CellText = cellText;
        Error = error;
    }

    public string CellText { get; }
    public ExcelCellPositionError Error { get; }

    public static InvalidExcelCellPositionException ForTrailingContent(string cellText)
    {
        return new(
            cellText: cellText,
            error: ExcelCellPositionError.TrailingContent);
    }

    public static InvalidExcelCellPositionException ForUnexpectedEnd(string cellText)
    {
        return new(
            cellText: cellText,
            error: ExcelCellPositionError.UnexpectedEnd);
    }

    public static InvalidExcelCellPositionException ForMissingRowNumber(string cellText)
    {
        return new(
            cellText: cellText,
            error: ExcelCellPositionError.MissingRowNumber);
    }

    private static string CreateMessage(string cellText, ExcelCellPositionError error)
    {
        return error switch
        {
            ExcelCellPositionError.TrailingContent => $"Expected valid cell position syntax in '{cellText}'.",
            ExcelCellPositionError.UnexpectedEnd => $"Invalid cell position '{cellText}'.",
            ExcelCellPositionError.MissingRowNumber => $"Expected row number after column letters in '{cellText}'.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(error),
                error,
                null),
        };
    }
}

public sealed class MissingColumnSearchRulesException(Type columnEnumType)
    : InvalidOperationException($"Search keys for some columns in {columnEnumType.Name} are missing.")
{
    public Type ColumnEnumType { get; } = columnEnumType;
}

public enum ExcelCellStringError
{
    NonTextCellType,
    InvalidSharedStringIndex,
    UnsupportedCellDataType,
}

public sealed class ExcelCellStringValueException : InvalidOperationException
{
    private ExcelCellStringValueException(
        ExcelCellStringError error,
        string? cellReference,
        CellValues? cellDataType,
        string? rawSharedStringIndex)
        : base(CreateMessage(
            error: error,
            cellReference: cellReference,
            cellDataType: cellDataType,
            rawSharedStringIndex: rawSharedStringIndex))
    {
        Error = error;
        CellReference = cellReference;
        CellDataType = cellDataType;
        RawSharedStringIndex = rawSharedStringIndex;
    }

    public ExcelCellStringError Error { get; }
    public string? CellReference { get; }
    public CellValues? CellDataType { get; }
    public string? RawSharedStringIndex { get; }

    public static ExcelCellStringValueException ForNonTextCellType(
        string? cellReference,
        CellValues cellDataType)
    {
        return new(
            error: ExcelCellStringError.NonTextCellType,
            cellReference: cellReference,
            cellDataType: cellDataType,
            rawSharedStringIndex: null);
    }

    public static ExcelCellStringValueException ForInvalidSharedStringIndex(
        string? cellReference,
        string? rawSharedStringIndex)
    {
        return new(
            error: ExcelCellStringError.InvalidSharedStringIndex,
            cellReference: cellReference,
            cellDataType: CellValues.SharedString,
            rawSharedStringIndex: rawSharedStringIndex);
    }

    public static ExcelCellStringValueException ForUnsupportedCellDataType(
        string? cellReference,
        CellValues cellDataType)
    {
        return new(
            error: ExcelCellStringError.UnsupportedCellDataType,
            cellReference: cellReference,
            cellDataType: cellDataType,
            rawSharedStringIndex: null);
    }

    private static string CreateMessage(
        ExcelCellStringError error,
        string? cellReference,
        CellValues? cellDataType,
        string? rawSharedStringIndex)
    {
        var location = cellReference ?? "<unknown>";
        return error switch
        {
            ExcelCellStringError.NonTextCellType => $"Expected a string in cell {location}, found {cellDataType}.",
            ExcelCellStringError.InvalidSharedStringIndex =>
                $"Invalid shared string index '{rawSharedStringIndex}' in cell {location}.",
            ExcelCellStringError.UnsupportedCellDataType =>
                $"Unsupported cell data type {cellDataType} in cell {location}.",
            _ => throw new ArgumentOutOfRangeException(
                nameof(error),
                error,
                null),
        };
    }
}
