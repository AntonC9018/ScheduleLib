namespace ScheduleLib.Builders;

/// <summary>
/// The implicit split configuration assigns conflicting values to one course:
/// either two configuration entries disagree, or an explicit lesson value
/// contradicts the configured one.
/// </summary>
public sealed class ConflictingImplicitAssignmentException : ScheduleBuildException
{
    private ConflictingImplicitAssignmentException(string message)
        : base(message)
    {
    }

    public static ConflictingImplicitAssignmentException ForExplicitSpecializationConflict(
        string courseName,
        string? explicitValue,
        string? assignedValue)
    {
        return new(
            $"The lesson for course '{courseName}' has the explicit specialization "
            + $"'{explicitValue}', but the implicit split configuration "
            + $"assigns '{assignedValue}'.");
    }

    public static ConflictingImplicitAssignmentException ForExplicitAlternativeConflict(
        string courseName,
        string? explicitValue,
        string? assignedValue)
    {
        return new(
            $"The lesson for course '{courseName}' has the explicit alternative "
            + $"'{explicitValue}', but the implicit split configuration "
            + $"assigns '{assignedValue}'.");
    }

    /// <summary>
    /// Builds the shared "conflicting assignment" error for the implicit split pass.
    /// One factory keeps the four resolution sites (per-group and per-name, for both
    /// dimensions) worded identically.
    /// </summary>
    public static ConflictingImplicitAssignmentException ForConflictingAssignment(
        string dimension,
        string? prev,
        string? next,
        string courseName)
    {
        return new(
            $"The implicit split configuration assigns conflicting {dimension}s "
            + $"'{prev}' and '{next}' to course '{courseName}'.");
    }
}

/// <summary>
/// The implicit split configuration pins scopes to a study year, but the build
/// carries no usable study year to resolve them against: either there is no
/// group parse context at all, or the context was defaulted from the wall clock
/// and the clock has moved past every pinned year (which would silently skip
/// those assignments).
/// </summary>
public sealed class MissingImplicitSplitStudyYearException : ScheduleBuildException
{
    private MissingImplicitSplitStudyYearException(string message)
        : base(message)
    {
    }

    public static MissingImplicitSplitStudyYearException ForNoGroupParseContext()
    {
        return new(
            "The implicit split configuration pins scopes to a study year, but the "
            + "schedule builder has no group parse context. A null study year matches "
            + "no year-pinned scope, so building would silently skip those assignments; "
            + "set ScheduleBuilder.GroupParseContext or remove the StudyYear pins.");
    }

    public static MissingImplicitSplitStudyYearException ForNoMatchingWallClockYear(StudyYear currentYear)
    {
        return new(
            "The implicit split configuration pins scopes to a study year, but no scope "
            + $"matches the wall-clock-derived study year {currentYear}. The assignments "
            + "would be silently skipped; set ScheduleBuilder.GroupParseContext explicitly "
            + "(before adding groups) to pin the study year, or remove the StudyYear pins.");
    }
}
