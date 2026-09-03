using System.Text;

namespace ScheduleLib;

/// <summary>
/// Configures the implicit split dimensions of groups: courses whose lessons belong to a
/// specialization or an alternative even though the schedule source does not say so.
/// Scopes are matched per study year and group category. A lesson receives a value only
/// when every group it belongs to matches a scope; lessons shared with groups outside
/// every scope stay unattributed.
/// </summary>
public sealed class ImplicitSplitConfig
{
    public List<ImplicitSplitScope> Scopes { get; init; } = [];

    /// <summary>
    /// A stable textual form of the configuration. It feeds the schedule cache hash, so
    /// editing the configuration invalidates the cache instead of silently reusing one
    /// built without the assignments.
    /// </summary>
    public string DescribeForCacheHash()
    {
        var sb = new StringBuilder();
        foreach (var scope in Scopes)
        {
            sb.Append(scope.StudyYear?.ToString() ?? "*").Append('|')
                .Append(scope.Grade?.Value.ToString() ?? "*").Append('|')
                .Append(scope.Faculty?.Name ?? "*").Append('|')
                .Append(scope.AttendanceMode?.ToString() ?? "*").Append('|')
                .Append(scope.Qualification?.ToString() ?? "*");
            AppendCourses(sb, scope.SpecializationCourses, x => x.Value);
            AppendCourses(sb, scope.AlternativeCourses, x => x.Value);
            sb.Append('\n');
        }
        return sb.ToString();

        static void AppendCourses(StringBuilder sb, Dictionary<string, T> courses, Func<T, string?> value)
            where T : struct
        {
            foreach (var (name, v) in courses.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                sb.Append('|').Append(name).Append('=').Append(value(v));
            }
        }
    }
}

public sealed class ImplicitSplitScope
{
    public int? StudyYear { get; init; }
    public Grade? Grade { get; init; }
    public Faculty? Faculty { get; init; }
    public AttendanceMode? AttendanceMode { get; init; }
    public QualificationType? Qualification { get; init; }

    /// <summary>
    /// Course name to the specialization its lessons implicitly belong to.
    /// A course matches when any of its names equals the key.
    /// </summary>
    public Dictionary<string, Specialization> SpecializationCourses { get; init; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Course name to the alternative its lessons implicitly belong to.
    /// A course matches when any of its names equals the key.
    /// </summary>
    public Dictionary<string, Alternative> AlternativeCourses { get; init; } = new(StringComparer.Ordinal);

    public bool Matches(
        int? currentStudyYear,
        Grade grade,
        Faculty faculty,
        AttendanceMode attendanceMode,
        QualificationType qualificationType)
    {
        if (StudyYear is { } year && currentStudyYear != year)
        {
            return false;
        }
        if (Grade is { } g && grade != g)
        {
            return false;
        }
        if (Faculty is { } f && faculty != f)
        {
            return false;
        }
        if (AttendanceMode is { } a && attendanceMode != a)
        {
            return false;
        }
        if (Qualification is { } q && qualificationType != q)
        {
            return false;
        }
        return true;
    }
}
