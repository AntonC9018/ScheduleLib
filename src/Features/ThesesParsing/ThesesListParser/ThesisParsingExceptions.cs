namespace ScheduleLib.Theses.Parsing;

public enum CommissionHeaderError
{
    ExpectedComisia,
    ExpectedWhitespaceAfterComisia,
    ExpectedRomanNumberAfterComisia,
}

public sealed class ThesisListSheetNotFoundException(ThesisType targetThesisType)
    : InvalidOperationException($"Sheet not found for thesis type {targetThesisType}.")
{
    public ThesisType TargetThesisType { get; } = targetThesisType;
}

public sealed class ThesisTeacherNameMissingException()
    : InvalidOperationException("Teacher name is required");

public sealed class ThesisGroupNameMissingException()
    : InvalidOperationException("Group name is required");

public sealed class ThesisListHeaderRowNotFoundException(ThesisType targetThesisType)
    : InvalidOperationException($"Header row not found for thesis type {targetThesisType}.")
{
    public ThesisType TargetThesisType { get; } = targetThesisType;
}

public sealed class TrailingThesisStudentNameTextException(string remainingText)
    : InvalidOperationException($"Parser not empty after student name: '{remainingText}'.")
{
    public string RemainingText { get; } = remainingText;
}

public sealed class TrailingThesisMentorNameTextException(string remainingText)
    : InvalidOperationException($"Parser not empty after mentor name: '{remainingText}'.")
{
    public string RemainingText { get; } = remainingText;
}

public sealed class CommissionWorkbookMissingException()
    : InvalidOperationException("Excel workbook part is missing.");

public sealed class UnsupportedCommissionExcelDateCellException(string? cellReference)
    : NotSupportedException($"Excel Date cell storage at '{cellReference ?? "<unknown>"}' is not supported.")
{
    public string? CellReference { get; } = cellReference;
}

public sealed class InvalidCommissionHeaderException(
    CommissionHeaderError error,
    string rawText)
    : InvalidOperationException(CreateMessage(error, rawText))
{
    public CommissionHeaderError Error { get; } = error;
    public string RawText { get; } = rawText;

    private static string CreateMessage(CommissionHeaderError error, string rawText)
    {
        return error switch
        {
            CommissionHeaderError.ExpectedComisia => $"Expected text 'Comisia' in '{rawText}'.",
            CommissionHeaderError.ExpectedWhitespaceAfterComisia =>
                $"Expected whitespace after 'Comisia' in '{rawText}'.",
            CommissionHeaderError.ExpectedRomanNumberAfterComisia =>
                $"Expected roman number after 'Comisia' in '{rawText}'.",
            _ => $"Invalid commission header '{rawText}' ({error}).",
        };
    }
}

public sealed class UnexpectedCommissionStudentColumnException(int columnPosition)
    : InvalidOperationException($"Students must start immediately (column {columnPosition}).")
{
    public int ColumnPosition { get; } = columnPosition;
}

public sealed class MissingCommissionColumnDateException(int columnPosition)
    : InvalidOperationException($"Student column {columnPosition} has no date.")
{
    public int ColumnPosition { get; } = columnPosition;
}
