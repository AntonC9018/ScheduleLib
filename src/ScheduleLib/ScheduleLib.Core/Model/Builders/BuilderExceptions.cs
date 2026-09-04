namespace ScheduleLib.Builders;

/// <summary>
/// Base for schedule-build domain errors. Derives from
/// <see cref="InvalidOperationException"/> so existing callers catching it keep
/// working; new code should catch (and tests should assert) the concrete type.
/// </summary>
public abstract class ScheduleBuildException : InvalidOperationException
{
    protected ScheduleBuildException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The implicit split configuration assigns conflicting values to one course:
/// either two configuration entries disagree, or an explicit lesson value
/// contradicts the configured one.
/// </summary>
public sealed class ConflictingImplicitAssignmentException : ScheduleBuildException
{
    public ConflictingImplicitAssignmentException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A lesson carries two different specializations: one stored directly and one
/// classified out of the subgroup field.
/// </summary>
public sealed class ConflictingSpecializationException : ScheduleBuildException
{
    public ConflictingSpecializationException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The beginner/non-beginner language proficiency split is inconsistent: a
/// lesson mixes groups with and without the split, or a beginner lesson has no
/// unannotated counterpart for the same group and course.
/// </summary>
public sealed class InconsistentLanguageSplitException : ScheduleBuildException
{
    public InconsistentLanguageSplitException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The observed subgroup partition of a group is malformed: numeric subgroups
/// do not form a contiguous prefix starting at I, or a group has exactly one
/// observed language subgroup instead of zero or at least two.
/// </summary>
public sealed class InvalidSubGroupPartitionException : ScheduleBuildException
{
    public InvalidSubGroupPartitionException(string message)
        : base(message)
    {
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
    public MissingImplicitSplitStudyYearException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Two weekly lessons occupy the same period, day and time slot for a shared
/// group without being separated on any split dimension.
/// </summary>
public sealed class OverlappingLessonsException : ScheduleBuildException
{
    public OverlappingLessonsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A required schedule-model value was never set: a group name or grade, a
/// lesson date, group or course reference, or a teacher last name.
/// </summary>
public sealed class UninitializedScheduleModelException : ScheduleBuildException
{
    public UninitializedScheduleModelException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A lesson's group set is malformed: a consultation carries groups, a group
/// repeats within one lesson, a group id refers to no known group, or the
/// groups mix attendance modes that may not combine.
/// </summary>
public sealed class InvalidLessonGroupsException : ScheduleBuildException
{
    public InvalidLessonGroupsException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A teaching period ends before it starts.
/// </summary>
public sealed class InvalidPeriodException : ScheduleBuildException
{
    public InvalidPeriodException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A lesson carries a subgroup value that is neither numeric, special, nor a
/// configured specialization.
/// </summary>
public sealed class UnknownSubGroupException : ScheduleBuildException
{
    public UnknownSubGroupException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Teacher name remaps recurse instead of mapping to the final version.
/// </summary>
public sealed class ConflictingTeacherNameRemapException : ScheduleBuildException
{
    public ConflictingTeacherNameRemapException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A teacher name carries more parts than the model can store.
/// </summary>
public sealed class InvalidTeacherNameException : ScheduleBuildException
{
    public InvalidTeacherNameException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A group name does not follow the expected label/year/number/language shape.
/// </summary>
public sealed class InvalidGroupNameException : ScheduleBuildException
{
    public InvalidGroupNameException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A schedule document header or cell does not follow the expected
/// day/date/time/semester shape.
/// </summary>
public sealed class InvalidScheduleDocumentException : ScheduleBuildException
{
    public InvalidScheduleDocumentException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A lesson carries two different subgroups: one stored directly and one
/// classified out of the partition hint.
/// </summary>
public sealed class ConflictingSubGroupException : ScheduleBuildException
{
    public ConflictingSubGroupException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The course-name parser configuration is malformed: ignored shortened words
/// must be given without the trailing dot.
/// </summary>
public sealed class InvalidCourseNameConfigException : ScheduleBuildException
{
    public InvalidCourseNameConfigException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A semester date-range configuration leaves a required value unspecified:
/// semester, attendance mode, qualification type, start/end date, or year.
/// </summary>
public sealed class IncompleteSemesterDateRangeException : ScheduleBuildException
{
    public IncompleteSemesterDateRangeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// A semester date-range configuration ends before it starts.
/// </summary>
public sealed class InvalidSemesterDateRangeException : ScheduleBuildException
{
    public InvalidSemesterDateRangeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Two semester date-range configurations cover the same key.
/// </summary>
public sealed class DuplicateSemesterDateRangeException : ScheduleBuildException
{
    public DuplicateSemesterDateRangeException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The study-week parity document has no usable document or body.
/// </summary>
public sealed class InvalidParityDocumentException : ScheduleBuildException
{
    public InvalidParityDocumentException(string message)
        : base(message)
    {
    }
}
