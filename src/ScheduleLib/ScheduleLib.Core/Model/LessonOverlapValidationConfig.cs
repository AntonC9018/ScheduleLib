namespace ScheduleLib;

/// <summary>
/// Configuration of the lesson overlap validation. The validation only runs when the
/// configuration is present, so builders that never opted in are not checked.
/// </summary>
public sealed class LessonOverlapValidationConfig
{
    /// <summary>
    /// Validates only the schedules built for this study year. Historical rebuilds stay
    /// unchecked: their data is frozen and their overlaps were never going to be fixed.
    /// </summary>
    public StudyYear? StudyYear { get; init; }

    /// <summary>
    /// Pairs the validation tolerates, each with the reason. Entries are temporary:
    /// they are removed as the data or the configuration gets fixed.
    /// </summary>
    public List<LessonOverlapAllowlistEntry> Allowlist { get; init; } = [];
}

/// <summary>
/// Accepts a conflict of two courses. The entry matches when the two canonical course
/// names are the given ones (in either order) and every set field also matches. The
/// more fields are set, the narrower the acceptance.
/// </summary>
public sealed class LessonOverlapAllowlistEntry
{
    public required string CourseA { get; init; }
    public required string CourseB { get; init; }

    /// <summary>Constrains the entry to conflicts whose shared group is this one.</summary>
    public string? GroupName { get; init; }
    public DayOfWeek? Day { get; init; }
    public TimeSlot? TimeSlot { get; init; }

    /// <summary>Why the pair is tolerated. Shows up in this entry's documentation only.</summary>
    public required string Reason { get; init; }
}
