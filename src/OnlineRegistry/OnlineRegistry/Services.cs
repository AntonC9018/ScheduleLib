using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

public interface IRegistryLessonParserErrorHandler
{
    // May want to pull this out.
    void CustomLessonType(ReadOnlySpan<char> ch);
}

public readonly struct StudentsInGroup
{
    public required IEnumerable<Name> Students { get; init; }
    public required Schedule Schedule { get; init; }
    public required RegularLessonId LessonId { get; init; }
    public required GroupId GroupId { get; init; }
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

public sealed class RegistryErrorLogger : IRegistryErrorHandler
{
    public ExtraLessonInstanceAction ExtraLessonAction { get; set; } = ExtraLessonInstanceAction.LeaveAlone;

    public void CourseNotFound(string courseName)
    {
        Console.WriteLine($"Course not found: {courseName}");
    }

    public void StudentsNotInDbButInRegistry(StudentsInGroup students)
    {
        {
            var groupName = students.Schedule.Get(students.GroupId).Name;
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
