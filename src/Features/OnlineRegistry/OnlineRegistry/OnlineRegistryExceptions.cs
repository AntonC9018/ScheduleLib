using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

public sealed class UnsupportedSemesterException(Semester semester)
    : InvalidOperationException($"Unsupported semester: {semester}")
{
    public Semester Semester { get; } = semester;
}

public sealed class UnexpectedGroupLinkCountException(int actualCount, int expectedCount)
    : InvalidOperationException($"Expected {expectedCount} group URLs, found {actualCount}.")
{
    public int ActualCount { get; } = actualCount;
    public int ExpectedCount { get; } = expectedCount;
}

public sealed class NonWildcardGroupMatchRequiredException()
    : InvalidOperationException("Must match a single group if not wildcard.");

public sealed class AttendanceLessonCountMismatchException(int editableIndex, int attendanceLessonCount)
    : InvalidOperationException(
        $"Attendance lesson count ({attendanceLessonCount}) does not match editable lessons at index {editableIndex}.")
{
    public int EditableIndex { get; } = editableIndex;
    public int AttendanceLessonCount { get; } = attendanceLessonCount;
}

public sealed class RegistryDateTimeParseException(string value, string expectedFormat)
    : NotSupportedException($"The date time '{value}' did not match format '{expectedFormat}'.")
{
    public string Value { get; } = value;
    public string ExpectedFormat { get; } = expectedFormat;
}

public sealed class UnsupportedAttendanceHeaderDateFormatException(string headerText)
    : NotSupportedException($"Unsupported format of date in attendance table: '{headerText}'.")
{
    public string HeaderText { get; } = headerText;
}

public sealed class RegistryLessonFormMissingException()
    : InvalidOperationException("Registry lesson form is missing.");

public sealed class RegistryFormAttendanceCountMismatchException(int attendanceCount, int studentRowCount)
    : InvalidOperationException(
        $"Attendance length ({attendanceCount}) does not match the number of students in the HTML ({studentRowCount}).")
{
    public int AttendanceCount { get; } = attendanceCount;
    public int StudentRowCount { get; } = studentRowCount;
}

public sealed class RegistryFormStudentMismatchException(int index, string expectedName, string actualName)
    : InvalidOperationException($"Student mismatch at index {index}: expected '{expectedName}', found '{actualName}'.")
{
    public int Index { get; } = index;
    public string ExpectedName { get; } = expectedName;
    public string ActualName { get; } = actualName;
}

public sealed class RegistryFormAttendanceColumnMissingException()
    : InvalidOperationException("Could not find attendance/grade column.");

public sealed class RegistryLessonTypeNotParsedFullyException(string input)
    : NotSupportedException($"Lesson type not parsed fully: '{input}'.")
{
    public string Input { get; } = input;
}

public sealed class RegistryGradeFormMissingException()
    : InvalidOperationException("Registry grade form is missing.");

public sealed class RegistryGradeFieldsUnidentifiableException()
    : InvalidOperationException("Registry grade fields cannot be identified for confirmation.");

public sealed class InvalidRegistryHttpCallDelayException(TimeSpan delay)
    : InvalidOperationException($"Registry HttpCallDelay must be nonnegative, got {delay}.")
{
    public TimeSpan Delay { get; } = delay;
}

public sealed class InvalidCourseNameMaxDistanceException(int maxDistance)
    : InvalidOperationException($"Registry CourseNameMaxDistance must be nonnegative, got {maxDistance}.")
{
    public int MaxDistance { get; } = maxDistance;
}

public sealed class MissingRegistryCredentialsConfigException()
    : InvalidOperationException("No credentials configured.");

public sealed class MissingRegistryCommandDerivationConfigException()
    : InvalidOperationException("No command derivation configured.");

public sealed class OnlineRegistryNotEnabledException()
    : InvalidOperationException("Registry not enabled.");

public sealed class OnlineRegistryNotConfiguredException()
    : InvalidOperationException("Online registry not configured.");

public sealed class MixedLabAttendanceSourcesException()
    : InvalidOperationException("Merged lab attendance cannot mix dated and ordinal sources.");

public sealed class ExceededLessonTypesCountException(int attendanceDayCount, int lessonTypeCount)
    : InvalidOperationException(
        $"Attendance day count ({attendanceDayCount}) exceeds specified lesson types ({lessonTypeCount}).")
{
    public int AttendanceDayCount { get; } = attendanceDayCount;
    public int LessonTypeCount { get; } = lessonTypeCount;
}

public sealed class ExceededAttendanceDatesCountException(int attendanceDayCount, int dateCount)
    : InvalidOperationException(
        $"Attendance day count ({attendanceDayCount}) exceeds specified attendance dates ({dateCount}).")
{
    public int AttendanceDayCount { get; } = attendanceDayCount;
    public int DateCount { get; } = dateCount;
}

public sealed class MissingAttendanceStudentNameException()
    : InvalidOperationException("Student name not set.");

public sealed class AttendanceKeyLessonTypeMismatchException(StudentsLookupKey key, bool lessonTypesSpecified)
    : InvalidOperationException(
        $"LessonType in builder key {key} must be None only if per-lesson types are specified (specified: {lessonTypesSpecified}).")
{
    public StudentsLookupKey Key { get; } = key;
    public bool LessonTypesSpecified { get; } = lessonTypesSpecified;
}

public sealed class DuplicateRegistryLabMergeRuleException(string course, string group)
    : InvalidOperationException($"Duplicate registry lab merge rules for {course} ({group}).")
{
    public string Course { get; } = course;
    public string Group { get; } = group;
}

public sealed class MissingRegistryLabMergeSurvivingSubGroupException(string course, string group)
    : InvalidOperationException($"Registry lab merge for {course} ({group}) requires a surviving subgroup.")
{
    public string Course { get; } = course;
    public string Group { get; } = group;
}

public sealed class SurvivingRegistryLabMergeLessonsNotFoundException(
    CourseId courseId,
    GroupId groupId,
    SubGroup subGroup)
    : InvalidOperationException(
        $"Surviving registry lab merge lessons not found for course {courseId}, group {groupId}, subgroup {subGroup}.")
{
    public CourseId CourseId { get; } = courseId;
    public GroupId GroupId { get; } = groupId;
    public SubGroup SubGroup { get; } = subGroup;
}

public sealed class RegistryLabChangedAfterCleanupPlanningException(Uri lessonUri)
    : InvalidOperationException(
        $"Registry lab changed after cleanup planning ({lessonUri}); preview again before deleting it.")
{
    public Uri LessonUri { get; } = lessonUri;
}

public sealed class SharedLectureRosterLengthMismatchException(int expectedLength, int actualLength)
    : InvalidOperationException(
        $"Shared lecture attendance roster lengths differ (expected {expectedLength}, got {actualLength}).")
{
    public int ExpectedLength { get; } = expectedLength;
    public int ActualLength { get; } = actualLength;
}

public sealed class SharedLectureAttendanceDisagreementException(DateTime dateTime, int studentIndex)
    : InvalidOperationException(
        $"Shared lecture attendance sources disagree for {dateTime} at student index {studentIndex}.")
{
    public DateTime DateTime { get; } = dateTime;
    public int StudentIndex { get; } = studentIndex;
}

public sealed class UnsupportedGradeAttendanceUpdateException()
    : NotImplementedException("Updating Grade attendance entries is not implemented.");

public sealed class UnscheduledAssessmentWithoutLessonTypeException(DateTime dateTime)
    : InvalidOperationException(
        $"An assessment outside the timetable at {dateTime} with no lesson type cannot be assigned a configured date.")
{
    public DateTime DateTime { get; } = dateTime;
}

public sealed class AmbiguousAssessmentLessonTypeException(DateTime dateTime)
    : InvalidOperationException(
        $"An assessment without a lesson type at {dateTime} requires an unambiguous scheduled lesson type.")
{
    public DateTime DateTime { get; } = dateTime;
}

public sealed class InvalidAssessmentRuleLessonTypeException(LessonType lessonType)
    : InvalidOperationException(
        $"Assessment rules require one explicit distinct lesson type per scope (invalid or duplicate: {lessonType}).")
{
    public LessonType LessonType { get; } = lessonType;
}

public sealed class DuplicateAssessmentDatesException(LessonType lessonType)
    : InvalidOperationException($"Assessment dates must be distinct within a scope ({lessonType}).")
{
    public LessonType LessonType { get; } = lessonType;
}

public sealed class AmbiguousAssessmentDateLessonException(LessonType lessonType, DateOnly date, int matchCount)
    : InvalidOperationException(
        $"Assessment date {date} for {lessonType} matched {matchCount} scheduled lessons; expected exactly one.")
{
    public LessonType LessonType { get; } = lessonType;
    public DateOnly Date { get; } = date;
    public int MatchCount { get; } = matchCount;
}

public sealed class MovedAssessmentOriginalLessonMissingException(DateTime dateTime, LessonType lessonType)
    : InvalidOperationException(
        $"Moving an assessment from {dateTime} ({lessonType}) requires its original lesson in the selected timetable.")
{
    public DateTime DateTime { get; } = dateTime;
    public LessonType LessonType { get; } = lessonType;
}

public sealed class MovedAssessmentRegularTopicMissingException(DateTime dateTime, LessonType lessonType)
    : InvalidOperationException(
        $"Moving an assessment from {dateTime} ({lessonType}) requires a regular topic for the freed occurrence.")
{
    public DateTime DateTime { get; } = dateTime;
    public LessonType LessonType { get; } = lessonType;
}

public sealed class MultipleAssessmentsForSameLessonException(DateTime dateTime, LessonType lessonType)
    : InvalidOperationException(
        $"Multiple assessments identify the same scheduled lesson ({dateTime}, {lessonType}).")
{
    public DateTime DateTime { get; } = dateTime;
    public LessonType LessonType { get; } = lessonType;
}

public sealed class RegistryDeleteFormMissingException()
    : InvalidOperationException("Registry delete form is missing.");

public sealed class RegistryMutationFormMethodNotPostException(string method)
    : InvalidOperationException($"Registry mutation form must use POST, got '{method}'.")
{
    public string Method { get; } = method;
}

public sealed class TooManySameDayLessonsException(string role, int lessonCount, int maxSupported)
    : NotSupportedException(
        $"Too many {role} lessons on a single day ({lessonCount}); at most {maxSupported} are supported.")
{
    public string Role { get; } = role;
    public int LessonCount { get; } = lessonCount;
    public int MaxSupported { get; } = maxSupported;
}

internal sealed class UnsupportedLessonPropertyException(LessonProperty property)
    : NotSupportedException($"Unsupported lesson property: {property}.")
{
    public LessonProperty Property { get; } = property;
}
