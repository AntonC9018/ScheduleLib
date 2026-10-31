namespace ScheduleLib.OnlineRegistry;

public sealed class AttendanceTableMissingLessonDateTimeException(DateTime lessonDateTime)
    : InvalidOperationException($"Attendance table is missing the datetime {lessonDateTime}")
{
    public DateTime LessonDateTime { get; } = lessonDateTime;
}
