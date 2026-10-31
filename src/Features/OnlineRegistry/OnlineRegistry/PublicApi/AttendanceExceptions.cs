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
