namespace ScheduleLib.OnlineRegistry;

public interface IRegistryErrorHandler
{
    void CourseNotFound(string courseName);
    void GroupNotFound(string groupName);
    void LessonWithoutName();

    // May want to pull this out.
    void CustomLessonType(ReadOnlySpan<char> ch);

    // TODO: Needs to be passed the context.
    ExtraLessonInstanceAction ExtraLessonInstanceFound(DateTime date);
}

public enum ExtraLessonInstanceAction
{
    LeaveAlone,
    Delete,
    DeleteWithoutDataLoss,
}

public sealed class RegistryErrorLogger : IRegistryErrorHandler
{
    public void CourseNotFound(string courseName)
    {
        Console.WriteLine($"Course not found: {courseName}");
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
        return ExtraLessonInstanceAction.LeaveAlone;
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
