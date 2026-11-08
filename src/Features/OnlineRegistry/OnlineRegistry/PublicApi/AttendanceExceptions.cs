namespace ScheduleLib.OnlineRegistry;

public sealed class StudentAttendanceDayCountMismatchException(
    string studentName,
    int actualDayCount,
    int expectedDayCount)
    : InvalidOperationException(
        $"Student {studentName} has {actualDayCount} days, expected {expectedDayCount}")
{
    public string StudentName { get; } = studentName;
    public int ActualDayCount { get; } = actualDayCount;
    public int ExpectedDayCount { get; } = expectedDayCount;
}

public sealed class DuplicateAttendanceNameException(string name)
    : InvalidOperationException($"Duplicate name in DB: {name}")
{
    public string Name { get; } = name;
}

/// <summary>
/// Two registry roster students resolve to the same attendance student,
/// typically because they differ only by patronymic.
/// </summary>
public sealed class AmbiguousRegistryStudentException(string firstStudent, string secondStudent)
    : InvalidOperationException(
        $"Registry students {firstStudent} and {secondStudent} both match the same attendance student. Add a student name alias to distinguish them.")
{
    public string FirstStudent { get; } = firstStudent;
    public string SecondStudent { get; } = secondStudent;
}

/// <summary>An attendance source lists the same student (by last and first name) twice.</summary>
public sealed class DuplicateAttendanceStudentException(string student)
    : InvalidOperationException($"Duplicate student name in attendance data: {student}")
{
    public string Student { get; } = student;
}
