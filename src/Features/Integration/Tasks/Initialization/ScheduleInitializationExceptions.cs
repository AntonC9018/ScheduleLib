using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Core;

public sealed class SelectedTeacherAbsentFromScheduleException : InvalidOperationException
{
    public SelectedTeacherAbsentFromScheduleException(Name teacherName)
        : this(teacherName.ToString())
    {
    }

    private SelectedTeacherAbsentFromScheduleException(string teacherName)
        : base($"Selected teacher '{teacherName}' is absent from this schedule.")
    {
        TeacherName = teacherName;
    }

    public string TeacherName { get; }
}
