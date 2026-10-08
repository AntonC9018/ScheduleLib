using AngleSharp.Dom;
using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

/// <summary>Reads and submissions are separate so plan construction cannot submit forms.</summary>
public interface IRegistrySyncNavigator
{
    Task<IEnumerable<CourseLink>> GetCourses(Semester semester);
    Task<IEnumerable<GroupLink>> GetGroups(CourseLink course);
    Task<IDocument> GetHtml(Uri uri);
    Task SubmitLesson(IDocument document);
    Task SubmitDelete(IDocument document);
}

public sealed class RegistrySyncAction(string kind, string destination, string course, string groups,
    DateTime date, string? topic, int? attendanceCount, bool enabled, string? omission,
    Func<CancellationToken, Task> execute)
{
    public string Kind { get; } = kind;
    public string Destination { get; } = destination;
    public string Course { get; } = course;
    public string Groups { get; } = groups;
    public DateTime Date { get; } = date;
    public string? Topic { get; } = topic;
    public int? AttendanceCount { get; } = attendanceCount;
    public bool Enabled { get; } = enabled;
    public string? Omission { get; } = omission;
    public Task Execute(CancellationToken token) => Enabled ? execute(token) : Task.CompletedTask;
}

/// <summary>Distinguishes failed reads/form preparation from attempted remote submissions.</summary>
public sealed class RegistryActionExecutionException(bool submissionStarted, Exception inner)
    : Exception("Registry action failed.", inner)
{
    public bool SubmissionStarted { get; } = submissionStarted;
}

public sealed class RegistrySubmissionRejectedException(string message) : InvalidOperationException(message);

public sealed class RegistryActionCancelledException(bool submissionStarted, OperationCanceledException inner)
    : OperationCanceledException("Registry action cancelled.", inner, inner.CancellationToken)
{
    public bool SubmissionStarted { get; } = submissionStarted;
}
