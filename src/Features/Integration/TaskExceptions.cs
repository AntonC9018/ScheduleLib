namespace ScheduleLib.Application.Core;

public sealed class InvalidScheduleFolderNameException(string folderName)
    : InvalidOperationException($"The folders must be named in the format 'DD.MM.YYYY'. Found this: {folderName}")
{
    public string FolderName { get; } = folderName;
}

public sealed class NoScheduleCellsFoundException(string directoryName)
    : FormatException($"No schedule cells in PDF sources under {directoryName}.")
{
    public string DirectoryName { get; } = directoryName;
}

internal sealed record InvalidPreDefenseCommissionMember(
    int CommissionNumber,
    int MemberNumber,
    string Name);

internal static class PreDefenseInvalidDataExceptionFactory
{
    internal static InvalidDataException ForInvalidMemberName(
        int commissionNumber,
        int memberNumber,
        string name,
        Exception innerException)
    {
        // Preserve the sealed framework type and its existing catch contract.
#pragma warning disable CR1600 // Typed context is attached below because this exception cannot be subclassed.
        var exception = new InvalidDataException(
            $"Pre-defense commission {commissionNumber} member {memberNumber} has an invalid name '{name}': {innerException.Message}",
            innerException);
#pragma warning restore CR1600
        exception.Data[typeof(InvalidPreDefenseCommissionMember)] = new InvalidPreDefenseCommissionMember(
            CommissionNumber: commissionNumber,
            MemberNumber: memberNumber,
            name);
        return exception;
    }
}

public sealed class GoogleCalendarConfigMissingException()
    : InvalidOperationException("No google drive config found.");

public sealed class PrimaryGoogleCalendarSyncNotSupportedException()
    : InvalidOperationException("Primary calendar not supported!");

public sealed class UnexpectedLessonParityException(global::Parity parity)
    : InvalidOperationException($"Unexpected lesson parity: {parity}.")
{
    public global::Parity Parity { get; } = parity;
}

public sealed class RegistryGradeTableNotFoundException()
    : InvalidOperationException("No table found");

public sealed class RegistryGradeInputMissingException()
    : InvalidOperationException("No input found in grade cell");

public sealed class AttendanceSourcePathMissingException()
    : InvalidOperationException("Misconfigured source with a null path.");

public sealed class DeadlinesConfigMissingException()
    : InvalidOperationException("Can't use the deadlines feature unless it's been configured.");

public sealed class SharedLabsDeadlinesNotImplementedException(ScheduleLib.CourseId courseId)
    : NotImplementedException($"Shared labs for course {courseId.Id} are not implemented.")
{
    public ScheduleLib.CourseId CourseId { get; } = courseId;
}

public sealed class MissingExcelTeacherNameException()
    : NotSupportedException("Expected a teacher name in the cell.");

public enum ExcelTeacherNameError
{
    MissingLastNameSeparator,
    MissingFirstNameAfterMaidenName,
    MissingSpaceAfterMaidenName,
    UnexpectedWhitespaceAfterFirstName,
}

public sealed class InvalidExcelTeacherNameFormatException(
    ExcelTeacherNameError error,
    string rawText)
    : NotSupportedException(CreateMessage(error, rawText))
{
    public ExcelTeacherNameError Error { get; } = error;
    public string RawText { get; } = rawText;

    private static string CreateMessage(ExcelTeacherNameError error, string rawText)
    {
        return error switch
        {
            ExcelTeacherNameError.MissingLastNameSeparator => $"Wrong teacher name format: '{rawText}'.",
            ExcelTeacherNameError.MissingFirstNameAfterMaidenName =>
                $"Expected first name after maiden name in '{rawText}'.",
            ExcelTeacherNameError.MissingSpaceAfterMaidenName => $"A space must follow the maiden name in '{rawText}'.",
            ExcelTeacherNameError.UnexpectedWhitespaceAfterFirstName =>
                $"No spaces should follow the first name in '{rawText}'.",
            _ => $"Invalid teacher name '{rawText}' ({error}).",
        };
    }
}
