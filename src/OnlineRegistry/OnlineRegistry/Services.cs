using AutoConstructor.Attributes;
using Microsoft.Extensions.Logging;
using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

public interface IRegistryLessonParserErrorHandler
{
    // May want to pull this out.
    void CustomLessonType(ReadOnlySpan<char> ch);
}

public readonly struct StudentsInGroup
{
    public readonly IEnumerable<Name> Students;
    public readonly Schedule Schedule;
    public readonly RegularLessonId LessonId;
    public readonly FoundGroups Groups;

    public StudentsInGroup(
        IEnumerable<Name> students,
        Schedule schedule,
        FoundGroups groups,
        RegularLessonId lessonId)
    {
        Students = students;
        Schedule = schedule;
        Groups = groups;
        LessonId = lessonId;
    }
}

public interface IRegistryErrorHandler : IRegistryLessonParserErrorHandler
{
    void CourseNotFound(string courseName);
    void StudentsNotInDbButInRegistry(StudentsInGroup students);
    void GroupNotFound(string groupName);
    void LessonWithoutName();

    // TODO: Needs to be passed the context.
    ExtraLessonInstanceAction ExtraLessonInstanceFound(DateTime date);
}

public enum ExtraLessonInstanceAction
{
    LeaveAlone,
    Delete,

    // Unimplemented
    DeleteWithoutDataLoss,
}

[AutoConstructor]
public sealed partial class RegistryErrorLogger : IRegistryErrorHandler
{
    public ExtraLessonInstanceAction ExtraLessonAction { get; set; } = ExtraLessonInstanceAction.LeaveAlone;
    private readonly ILogger _logger;

    public void CourseNotFound(string courseName) => LogCourseNotFound(courseName);

    public void StudentsNotInDbButInRegistry(StudentsInGroup students)
    {
        using var loggerScope = LoggerScope();
        foreach (var student in students.Students)
        {
            LogStudentNotInDbButInRegistry(student);
        }

        IDisposable? LoggerScope()
        {
            var groupName = students.Groups.Value.ToString(students.Schedule);
            var lesson = students.Schedule.Get(students.LessonId);
            var lessonType = lesson.Lesson.Type;
            var course = students.Schedule.Get(lesson.Lesson.Course).FullName;
            var subGroup = lesson.Lesson.SubGroup.Value ?? "all subgroups";
            return _logger.BeginScope(new
            {
                groupName,
                subGroup,
                course,
                lessonType,
            });
        }
    }

    public void LessonWithoutName() => LogLessonWithoutName();

    public void CustomLessonType(ReadOnlySpan<char> ch) => LogCustomLessonType(ch.ToString());

    public ExtraLessonInstanceAction ExtraLessonInstanceFound(DateTime date)
    {
        LogExtraLessonInstanceFound(date);
        return ExtraLessonAction;
    }

    public void GroupNotFound(string groupName) => LogGroupNotFound(groupName);

    [LoggerMessage(LogLevel.Warning, "Course not found: {CourseName}")]
    partial void LogCourseNotFound(string CourseName);

    [LoggerMessage(LogLevel.Warning, "Lesson without name")]
    partial void LogLessonWithoutName();

    [LoggerMessage(LogLevel.Warning, "Custom lesson type: {LessonType}")]
    partial void LogCustomLessonType(string LessonType);

    [LoggerMessage(LogLevel.Information, "Extra lesson instance found: {Date}")]
    partial void LogExtraLessonInstanceFound(DateTime Date);

    [LoggerMessage(LogLevel.Warning, "Group not found: {GroupName}")]
    partial void LogGroupNotFound(string GroupName);

    [LoggerMessage(LogLevel.Warning, "Student not in DB but in registry: {Student}")]
    partial void LogStudentNotInDbButInRegistry(Name Student);
}
