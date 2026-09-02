using System.Runtime.CompilerServices;
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
    public readonly AnyLessonId LessonId;
    public readonly FoundGroups Groups;

    public StudentsInGroup(
        IEnumerable<Name> students,
        Schedule schedule,
        FoundGroups groups,
        AnyLessonId lessonId)
    {
        Students = students;
        Schedule = schedule;
        Groups = groups;
        LessonId = lessonId;
    }
}

public readonly record struct ErroneousLesson(object? Context);

public interface IRegistryErrorHandler : IRegistryLessonParserErrorHandler
{
    void CourseNotFound(string courseName);
    void StudentsNotInDbButInRegistry(StudentsInGroup students);
    void GroupNotFound(string groupName);
    void LessonWithoutName();
    void LessonsDecidedErroneous(ErroneousLesson lessons);
    void GroupParsingError(GroupParseErrorContext context);

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
            var groupSplit = lesson.Lesson.GroupSplitKey.ToDisplayString() ?? "all subgroups";
            return _logger.BeginScope(new{
                groupName,
                groupSplit,
                course,
                lessonType,
            });
        }
    }

    private static KeyValuePair<string, object?>[] KVA(
        KeyValuePair<string, object?>[] arr)
    {
        return arr;
    }

    private static KeyValuePair<string, object?> KV(
        object? value,
        [CallerArgumentExpression("value")] string? name = null)
    {
        return new(name!, value);
    }

    public void LessonWithoutName() => LogLessonWithoutName();

    public void LessonsDecidedErroneous(ErroneousLesson lesson)
    {
        using var scope = _logger.BeginScope(new
        {
            ErrorContext = lesson.Context,
        });
        _logger.LogError("Found lesson that should not be in the registry of the current person");
    }

    public void GroupParsingError(GroupParseErrorContext context)
    {
        _logger.LogError(
            context.Exception,
            "Could not parse group {Group}",
            context.String.Trim());
    }

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
