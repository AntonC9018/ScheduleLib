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

public struct NamesConfigSource()
{
    public string TokensFile = "tokens.json";
    public string TokenCookieName = "ForDecanat";
    public string RegistryBaseUrl = "http://crd.usm.md/studregistry/";
    public string RegistryLoginPath = "Account/Login";
    public string LessonsPath = "LessonAttendance";

    public readonly NamesConfig Build()
    {
        var reg = new Uri(RegistryBaseUrl);
        var login = new Uri(reg, RegistryLoginPath);
        var lessons = new Uri(reg, LessonsPath);
        return new()
        {
            TokensFile = TokensFile,
            TokenCookieName = TokenCookieName,
            LoginUrl = login,
            LessonsUrl = lessons,
            BaseUrl = reg,
        };
    }
}

public sealed class NamesConfig
{
    public static readonly NamesConfig Default = new NamesConfigSource().Build();

    public required string TokensFile { get; init; }
    public required string TokenCookieName { get; init; }
    public required Uri BaseUrl { get; init; }
    public required Uri LoginUrl { get; init; }
    public required Uri LessonsUrl { get; init; }
}
