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
    void GroupNotFound(string courseName, string groupName) => GroupNotFound(groupName);
    void GroupParsingError(string courseName, GroupParseErrorContext context) => GroupParsingError(context);
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

public sealed partial class RegistryErrorLogger : IRegistryErrorHandler
{
    public ExtraLessonInstanceAction ExtraLessonAction { get; set; } = ExtraLessonInstanceAction.LeaveAlone;
    private readonly ILogger _logger;
    private readonly RegistrySyncDiagnostics? _diagnostics;

    public RegistryErrorLogger(ILogger<RegistryErrorLogger> logger, RegistrySyncDiagnostics? diagnostics = null)
    {
        _logger = logger;
        _diagnostics = diagnostics;
    }

    public void CourseNotFound(string courseName)
    {
        _diagnostics?.Add(kind: "unmatched-registry-course",
            course: courseName,
            @group: null,
            detail: "Add or correct the course mapping in the schedule configuration.");
        LogCourseNotFound(courseName);
    }

    public void StudentsNotInDbButInRegistry(StudentsInGroup students)
    {
        // Do not enumerate students or attach schedule/student context to structured logs.
        _logger.LogWarning("Students in registry could not be matched to the local database");
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
        _logger.LogError("Found lesson that should not be in the registry of the current person");
    }

    public void GroupParsingError(GroupParseErrorContext context)
    {
        _logger.LogError("Could not parse registry group");
    }

    public void CustomLessonType(ReadOnlySpan<char> ch) => LogCustomLessonType(ch.ToString());

    public ExtraLessonInstanceAction ExtraLessonInstanceFound(DateTime date)
    {
        LogExtraLessonInstanceFound(date);
        return ExtraLessonAction;
    }

    public void GroupNotFound(string groupName) => GroupNotFound("", groupName);

    public void GroupNotFound(string courseName, string groupName)
    {
        _diagnostics?.Add(kind: "unmatched-registry-group",
            course: courseName,
            @group: groupName,
            detail: "Check the parent group, language, and subgroup mapping in the schedule configuration.");
        LogGroupNotFound(CourseName: courseName, GroupName: groupName);
    }

    public void GroupParsingError(string courseName, GroupParseErrorContext context)
    {
        _diagnostics?.Add(kind: "unrecognized-registry-group",
            course: courseName,
            @group: context.String,
            detail: "Correct the registry group label or add support for its format.");
        _logger.LogWarning("Could not parse registry group {Group} for {Course}",
            context.String,
            courseName);
    }

    [LoggerMessage(LogLevel.Warning, "Course not found: {CourseName}")]
    partial void LogCourseNotFound(string CourseName);

    [LoggerMessage(LogLevel.Warning, "Lesson without name")]
    partial void LogLessonWithoutName();

    [LoggerMessage(LogLevel.Warning, "Custom lesson type: {LessonType}")]
    partial void LogCustomLessonType(string LessonType);

    [LoggerMessage(LogLevel.Information, "Extra lesson instance found: {Date}")]
    partial void LogExtraLessonInstanceFound(DateTime Date);

    [LoggerMessage(LogLevel.Warning,
        "Registry group {GroupName} for {CourseName} could not be matched to the local schedule")]
    partial void LogGroupNotFound(string CourseName, string GroupName);
}
