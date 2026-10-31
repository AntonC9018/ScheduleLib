using ScheduleLib;
using ScheduleLib.Builders;

namespace OnlineRegistry.AttendanceExcel;

public sealed class AttendanceLessonNotFoundException(string worksheetName)
    : InvalidOperationException($"Not found lesson for string {worksheetName}")
{
    public string WorksheetName { get; } = worksheetName;
}

public sealed class InvalidAttendanceLessonTypeException(string tokenText)
    : InvalidOperationException($"Lesson type {tokenText} is not a valid lesson type")
{
    public string TokenText { get; } = tokenText;
}

public sealed class AttendanceCourseNotFoundException(string courseName)
    : InvalidOperationException($"Course {courseName} not found")
{
    public string CourseName { get; } = courseName;
}

public sealed class AttendanceGroupNotFoundException(string groupName)
    : InvalidOperationException($"Group {groupName} not found")
{
    public string GroupName { get; } = groupName;
}

public sealed class AmbiguousMergedLabCourseException(string worksheetName)
    : InvalidOperationException(
        $"Multiple merged lab courses match worksheet '{worksheetName}'. Specify its course.")
{
    public string WorksheetName { get; } = worksheetName;
}

public sealed class MultiplePartialAttendanceLessonMatchesException(LessonModelDiffMask differences)
    : InvalidOperationException($"Multiple matches to the partial key {differences}")
{
    public LessonModelDiffMask Differences { get; } = differences;
}

public sealed class AttendanceDateValidationWithLessonTypesHeaderException(string worksheetName)
    : InvalidOperationException(
        $"Sheet '{worksheetName}': date validation requires a date header row, but the header holds lesson types.")
{
    public string WorksheetName { get; } = worksheetName;
}

public sealed class AttendanceDateValidationWithoutRecognizedHeaderException(string worksheetName)
    : InvalidOperationException(
        $"Sheet '{worksheetName}': date validation is enabled but no header row was recognized. Declare a date in every attendance column of the top row.")
{
    public string WorksheetName { get; } = worksheetName;
}

public sealed class InvalidAttendanceCellValueException(string value)
    : InvalidOperationException($"Expecting empty, 'a', 'na' or 'am', got '{value}'")
{
    public string Value { get; } = value;
}
