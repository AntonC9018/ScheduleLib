using System.Text.Json;
using System.Text.Json.Serialization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Dates;
using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

public interface IRegistryGradeNavigator
{
    Task<IEnumerable<CourseLink>> GetCourses(Semester semester);
    Task<IEnumerable<GroupLink>> GetGroups(CourseLink course);
    Task<IDocument> GetHtml(Uri uri);

    Task SubmitGrades(
        IDocument document,
        CancellationToken token,
        Action? onSubmissionStarted = null);
}

public sealed record RegistryGradeChange(
    string Student,
    float SourceGrade,
    int RoundedGrade,
    string? PreviousGrade);

/// <summary>The category of one preflight grade-mapping result.</summary>
[JsonConverter(typeof(RegistryGradeNoticeKindJsonConverter))]
public enum RegistryGradeNoticeKind
{
    Omitted,
    Unsupported,
    Unmatched,
}

public static class RegistryGradeNoticeKindExtensions
{
    public static string ToWire(this RegistryGradeNoticeKind kind)
    {
        return kind switch
        {
            RegistryGradeNoticeKind.Omitted => "omitted",
            RegistryGradeNoticeKind.Unsupported => "unsupported",
            RegistryGradeNoticeKind.Unmatched => "unmatched",
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null),
        };
    }
}

/// <summary>Writes an enum as its wire string, also as a dictionary key. Reading is unsupported.</summary>
public abstract class WireNameJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract string ToWire(T value);

    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException($"{typeof(T).Name} is written to output only.");

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(ToWire(value));

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WritePropertyName(ToWire(value));
}

public sealed class RegistryGradeNoticeKindJsonConverter : WireNameJsonConverter<RegistryGradeNoticeKind>
{
    protected override string ToWire(RegistryGradeNoticeKind value) => value.ToWire();
}

/// <summary>One preflight grade-mapping result. Factories own the event's category and message.</summary>
public sealed record RegistryGradeNotice
{
    private RegistryGradeNotice(RegistryGradeNoticeKind kind, string detail)
    {
        Kind = kind;
        Detail = detail;
    }

    public RegistryGradeNoticeKind Kind { get; }

    public string Detail { get; }

    public static RegistryGradeNotice MoodleNameUnparsed(string userName) =>
        new(RegistryGradeNoticeKind.Omitted, $"Moodle name could not be parsed: {userName}");

    public static RegistryGradeNotice MoodleGradeMissing(string userName) =>
        new(RegistryGradeNoticeKind.Omitted, $"Moodle student has no grade: {userName}");

    public static RegistryGradeNotice MoodleGradeUnsupported(string userName) =>
        new(RegistryGradeNoticeKind.Unsupported, $"Moodle grade is not a finite supported number: {userName}");

    public static RegistryGradeNotice MoodleAttemptSuperseded(Name name) =>
        new(RegistryGradeNoticeKind.Omitted, $"Earlier attempt replaced by the last graded attempt: {name}");

    public static RegistryGradeNotice MoodlePathUnparsed() =>
        new(RegistryGradeNoticeKind.Unsupported, "Moodle course/test path could not be parsed.");

    public static RegistryGradeNotice MoodleCourseUnresolved() =>
        new(RegistryGradeNoticeKind.Unsupported, "Moodle course name does not resolve in the configured schedule.");

    public static RegistryGradeNotice RegistryGroupUnsupported(Uri evaluationUri) =>
        new(RegistryGradeNoticeKind.Unsupported, $"Wildcard or empty registry group is unsupported: {evaluationUri}");

    public static RegistryGradeNotice RegistryTestMissing(int testNumber, Uri evaluationUri) =>
        new(RegistryGradeNoticeKind.Unsupported, $"Testarea {testNumber} is missing for {evaluationUri}");

    public static RegistryGradeNotice RegistryStudentExpelled(string registryName) =>
        new(RegistryGradeNoticeKind.Omitted, $"Expelled registry student: {registryName}");

    public static RegistryGradeNotice RegistryNameUnparsed(string registryName) =>
        new(RegistryGradeNoticeKind.Unsupported, $"Registry name could not be parsed: {registryName}");

    public static RegistryGradeNotice RegistryNameTrailingTextUnsupported(string registryName) =>
        new(RegistryGradeNoticeKind.Unsupported, $"Registry name contains unsupported trailing text: {registryName}");

    public static RegistryGradeNotice RegistryStudentUnmatched(Name name) =>
        new(RegistryGradeNoticeKind.Unmatched, $"Registry student has no matching graded Moodle attempt: {name}");

    public static RegistryGradeNotice NoGradesToSubmit(string testUrl) =>
        new(RegistryGradeNoticeKind.Omitted, $"No mapped grades to submit for {testUrl}");

    public static RegistryGradeNotice NoTestFormsPlanned() =>
        new(RegistryGradeNoticeKind.Unmatched, "No registry test forms with supported mapped grades were planned.");

    public static RegistryGradeNotice MoodleStudentUnmatched(Name name, float grade) =>
        new(RegistryGradeNoticeKind.Unmatched, $"Moodle student was not matched in registry: {name} ({grade})");
}

public sealed record RegistryGradePlan(
    IReadOnlyList<RegistryGradeAction> Actions,
    IReadOnlyList<RegistryGradeNotice> Notices);

public sealed class RegistryGradeAction(
    string destination,
    string course,
    string groups,
    int testNumber,
    IReadOnlyList<RegistryGradeChange> grades,
    Func<CancellationToken, Task> execute)
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
        var inputs = form.QuerySelectorAll<IHtmlInputElement>("input[type=text]")
            .ToArray();

        bool HasInvalidOrDuplicateGradeInputs()
        {
            if (inputs.Length == 0)
            {
                return true;
            }

            if (inputs.Any(input => string.IsNullOrWhiteSpace(input.Name)))
            {
                return true;
            }

            if (inputs.Select(input => input.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Count()
                != inputs.Length)
            {
                return true;
            }

            return false;
        }

        if (HasInvalidOrDuplicateGradeInputs())
        {
            throw new InvalidOperationException("Registry grade fields cannot be identified for confirmation.");
        }

        return form;
    }
}
