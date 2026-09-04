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
    internal ImplicitSplitConfig(List<ImplicitSplitScope> scopes)
    {
        Scopes = scopes.ToArray();
    }

    public IReadOnlyList<ImplicitSplitScope> Scopes { get; }

    /// <summary>
    /// A stable textual form of the configuration. It feeds the schedule cache hash, so
    /// editing the configuration invalidates the cache instead of silently reusing one
    /// built without the assignments.
    /// </summary>
    public string DescribeForCacheHash()
    {
        var sb = new StringBuilder();
        // Scope order carries no meaning, so it is normalized: the same configuration
        // must produce the same hash regardless of how it was written down.
        var lines = new List<string>(Scopes.Count);
        foreach (var scope in Scopes)
        {
            var line = new StringBuilder();
            line.Append(scope.StudyYear?.ToString() ?? "*").Append('|')
                .Append(scope.Grade?.Value.ToString() ?? "*").Append('|')
                .Append(scope.Faculty?.Name ?? "*").Append('|')
                .Append(scope.AttendanceMode?.ToString() ?? "*").Append('|')
                .Append(scope.Qualification?.ToString() ?? "*");
            AppendCourses(line, scope.SpecializationCourses, x => x.Value);
            AppendCourses(line, scope.AlternativeCourses, x => x.Value);
            lines.Add(line.ToString());
        }
        lines.Sort(StringComparer.Ordinal);
        sb.AppendJoin('\n', lines);
        if (lines.Count > 0)
        {
            sb.Append('\n');
        }
        return sb.ToString();

        static void AppendCourses<T>(StringBuilder sb, IReadOnlyDictionary<string, T> courses, Func<T, string?> value)
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
    internal ImplicitSplitScope(
        StudyYear? studyYear,
        Grade? grade,
        Faculty? faculty,
        AttendanceMode? attendanceMode,
        QualificationType? qualification,
        Dictionary<string, Specialization> specializationCourses,
        Dictionary<string, Alternative> alternativeCourses)
    {
        StudyYear = studyYear;
        Grade = grade;
        Faculty = faculty;
        AttendanceMode = attendanceMode;
        Qualification = qualification;
        SpecializationCourses = new Dictionary<string, Specialization>(specializationCourses, StringComparer.Ordinal);
        AlternativeCourses = new Dictionary<string, Alternative>(alternativeCourses, StringComparer.Ordinal);
    }

    public StudyYear? StudyYear { get; }
    public Grade? Grade { get; }
    public Faculty? Faculty { get; }
    public AttendanceMode? AttendanceMode { get; }
    public QualificationType? Qualification { get; }

    /// <summary>
    /// Course name to the specialization its lessons implicitly belong to.
    /// A course matches when any of its names equals the key.
    /// </summary>
    public IReadOnlyDictionary<string, Specialization> SpecializationCourses { get; }

    /// <summary>
    /// Course name to the alternative its lessons implicitly belong to.
    /// A course matches when any of its names equals the key.
    /// </summary>
    public IReadOnlyDictionary<string, Alternative> AlternativeCourses { get; }

    public bool Matches(
        StudyYear? currentStudyYear,
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

/// <summary>
/// Builds an <see cref="ImplicitSplitConfig"/>. Mirrors
/// <see cref="SpecializationRegistryBuilder"/>: one <c>Scope</c> call per group
/// category, with the course mappings declared inside the scope.
/// </summary>
public sealed class ImplicitSplitConfigBuilder
{
    private readonly List<ImplicitSplitScope> _scopes = new();

    public void Scope(Action<ImplicitSplitScopeBuilder> configure)
    {
        var builder = new ImplicitSplitScopeBuilder();
        configure(builder);
        _scopes.Add(builder.Build());
    }

    public ImplicitSplitConfig Build()
    {
        return new(_scopes);
    }
}

/// <summary>
/// Configures one scope of an <see cref="ImplicitSplitConfig"/>. Fields left
/// null match every value in that category, like
/// <see cref="GroupSelectorBuilder"/>.
/// </summary>
public sealed class ImplicitSplitScopeBuilder
{
    public StudyYear? StudyYear { get; set; }
    public Grade? Grade { get; set; }
    public Faculty? Faculty { get; set; }
    public AttendanceMode? AttendanceMode { get; set; }
    public QualificationType? Qualification { get; set; }

    private readonly Dictionary<string, Specialization> _specializationCourses = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Alternative> _alternativeCourses = new(StringComparer.Ordinal);

    public void Specialization(string course, Specialization specialization)
    {
        _specializationCourses[course] = specialization;
    }

    public void Alternative(string course, Alternative alternative)
    {
        _alternativeCourses[course] = alternative;
    }

    internal ImplicitSplitScope Build()
    {
        return new(
            StudyYear,
            Grade,
            Faculty,
            AttendanceMode,
            Qualification,
            _specializationCourses,
            _alternativeCourses);
    }
}
