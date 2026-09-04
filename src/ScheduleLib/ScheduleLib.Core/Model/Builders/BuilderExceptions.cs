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
