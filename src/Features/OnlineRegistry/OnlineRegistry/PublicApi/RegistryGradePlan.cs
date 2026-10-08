using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

public interface IRegistryGradeNavigator
{
    Task<IEnumerable<CourseLink>> GetCourses(Semester semester);
    Task<IEnumerable<GroupLink>> GetGroups(CourseLink course);
    Task<IDocument> GetHtml(Uri uri);
    Task SubmitGrades(IDocument document, CancellationToken token, Action? onSubmissionStarted = null);
}

public sealed record RegistryGradeChange(string Student, float SourceGrade, int RoundedGrade, string? PreviousGrade);
public sealed record RegistryGradeNotice(string Kind, string Detail);
public sealed record RegistryGradePlan(IReadOnlyList<RegistryGradeAction> Actions, IReadOnlyList<RegistryGradeNotice> Notices);

public sealed class RegistryGradeAction(string destination, string course, string groups, int testNumber,
    IReadOnlyList<RegistryGradeChange> grades, Func<CancellationToken, Task> execute)
{
    public string Destination { get; } = destination;
    public string Course { get; } = course;
    public string Groups { get; } = groups;
    public int TestNumber { get; } = testNumber;
    public IReadOnlyList<RegistryGradeChange> Grades { get; } = grades;
    public Task Execute(CancellationToken token) => execute(token);
}

/// <summary>Shared preflight for planning and the production submission adapter.</summary>
public static class RegistryGradeForm
{
    public static IHtmlFormElement Validate(IDocument document)
    {
        var form = document.QuerySelector<IHtmlFormElement>("form")
            ?? throw new InvalidOperationException("Registry grade form is missing.");
        var inputs = form.QuerySelectorAll<IHtmlInputElement>("input[type=text]").ToArray();
        if (inputs.Length == 0 || inputs.Any(input => string.IsNullOrWhiteSpace(input.Name))
            || inputs.Select(input => input.Name).Distinct(StringComparer.Ordinal).Count() != inputs.Length)
            throw new InvalidOperationException("Registry grade fields cannot be identified for confirmation.");
        return form;
    }
}
