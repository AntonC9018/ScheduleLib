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

    /// <summary>A course matches when any of its names equals the key.</summary>
    public IReadOnlyDictionary<string, Specialization> SpecializationCourses { get; }

    /// <summary>A course matches when any of its names equals the key.</summary>
    public IReadOnlyDictionary<string, Alternative> AlternativeCourses { get; }

    /// <summary>
    /// A null <paramref name="currentStudyYear"/> matches no year-pinned scope;
    /// only scopes with a null <see cref="StudyYear"/> can match.
    /// </summary>
    public bool Matches(
        StudyYear? currentStudyYear,
        GroupCategory group)
    {
        if (StudyYear is { } year && currentStudyYear != year)
        {
            return false;
        }
        if (Grade is { } g && group.Grade != g)
        {
            return false;
        }
        if (Faculty is { } f && group.Faculty != f)
        {
            return false;
        }
        if (AttendanceMode is { } a && group.AttendanceMode != a)
        {
            return false;
        }

        if (Qualification is { } q && group.QualificationType != q)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// Mirrors <see cref="SpecializationRegistryBuilder"/>: one <c>Scope</c> call per group
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
/// Fields left null match every value in that category, like
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
