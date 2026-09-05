namespace ScheduleLib;

/// <summary>
/// Configuration of the lesson overlap validation. The validation only runs when the
/// configuration is present, so builders that never opted in are not checked.
/// <para>
/// There is no tolerance list: an overlap is always an error. Elective lessons are
/// separated by their partition values, and anything else is a source-document
/// defect to fix there.
/// </para>
/// </summary>
public sealed class LessonOverlapValidationConfig
{
    /// <summary>
    /// Validates only the schedules built for this study year. Historical rebuilds stay
    /// unchecked: their data is frozen and their overlaps were never going to be fixed.
    /// </summary>
    public StudyYear? StudyYear { get; init; }
}
