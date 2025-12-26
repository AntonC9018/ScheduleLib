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

    public void CourseNotFound(string courseName)
    {
        Console.WriteLine($"Course not found: {courseName}");
    }

    public void StudentsNotInDbButInRegistry(StudentsInGroup students)
    {
        {
            var groupName = students.Groups.Value.ToString(students.Schedule);
            var lesson = students.Schedule.Get(students.LessonId);
            var lessonType = lesson.Lesson.Type;
            var course = students.Schedule.Get(lesson.Lesson.Course).FullName;
            var subGroup = lesson.Lesson.SubGroup.Value ?? "all subgroups";
            Console.WriteLine($"Group {groupName} ({subGroup}), Course {course} ({lessonType})");
        }

        foreach (var student in students.Students)
        {
            Console.WriteLine($"Student not in DB but in registry: {student}");
        }
    }

    public void LessonWithoutName()
    {
        Console.WriteLine("Lesson without name");
    }

    public void CustomLessonType(ReadOnlySpan<char> ch)
    {
        Console.WriteLine($"Custom lesson type: {ch.ToString()}");
    }

    public ExtraLessonInstanceAction ExtraLessonInstanceFound(DateTime date)
    {
        Console.WriteLine($"Extra lesson instance found: {date}");
        return ExtraLessonAction;
    }

    public void GroupNotFound(string groupName)
    {
        Console.WriteLine($"Group not found: {groupName}");
    }
}
