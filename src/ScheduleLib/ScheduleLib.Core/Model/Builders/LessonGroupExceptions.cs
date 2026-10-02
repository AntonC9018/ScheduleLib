using ScheduleLib.Helper;

namespace ScheduleLib.Builders;

/// <summary>
/// A lesson carries two different specializations: one stored directly and one
/// classified out of the subgroup field.
/// </summary>
public sealed class ConflictingSpecializationException : ScheduleBuildException
{
    private ConflictingSpecializationException(string message)
        : base(message)
    {
    }

    public static ConflictingSpecializationException ForConflictingValues(string? first, string? second)
    {
        return new($"A lesson may not have two specializations: '{first}' and '{second}'.");
    }
}

/// <summary>
/// The beginner/non-beginner language proficiency split is inconsistent: a
/// lesson mixes groups with and without the split, or a beginner lesson has no
/// unannotated counterpart for the same group and course.
/// </summary>
public sealed class InconsistentLanguageSplitException : ScheduleBuildException
{
    private InconsistentLanguageSplitException(string message)
        : base(message)
    {
    }

    public static InconsistentLanguageSplitException ForMixedSplit(string groups, string course)
    {
        return new(
            $"The lesson for groups '{groups}' and course '{course}' "
            + "mixes groups with and without the language proficiency split for that course. "
            + "One stored subgroup value could not represent both meanings.");
    }

    public static InconsistentLanguageSplitException ForMissingCounterpart(string group, string course)
    {
        return new(
            $"The beginner subgroup lesson for group '{group}' and course '{course}' "
            + "has no unannotated counterpart lesson for the same group and course.");
    }
}

/// <summary>
/// The observed subgroup partition of a group is malformed: numeric subgroups
/// do not form a contiguous prefix starting at I, or a group has exactly one
/// observed language subgroup instead of zero or at least two.
/// </summary>
public sealed class InvalidSubGroupPartitionException : ScheduleBuildException
{
    private InvalidSubGroupPartitionException(string message)
        : base(message)
    {
    }

    public static InvalidSubGroupPartitionException ForNonContiguousPrefix(string groupName, int missing, int occurring)
    {
        return new(
            $"The numeric subgroups of group '{groupName}' must form a contiguous prefix starting at I. "
            + $"Missing '{NumberHelper.ToRoman(missing)}' while '{NumberHelper.ToRoman(occurring)}' occurs.");
    }

    public static InvalidSubGroupPartitionException ForSingleLanguageSubgroup(string groupName)
    {
        return new(
            $"The group '{groupName}' has a single language subgroup, but a group "
            + "must have either zero observed language subgroups or at least two.");
    }
}

/// <summary>
/// A lesson's group set is malformed: a consultation carries groups, a group
/// repeats within one lesson, a group id refers to no known group, or the
/// groups mix attendance modes that may not combine.
/// </summary>
public sealed class InvalidLessonGroupsException : ScheduleBuildException
{
    private InvalidLessonGroupsException(string message)
        : base(message)
    {
    }

    public static InvalidLessonGroupsException ForConsultationHasGroups()
    {
        return new("Consultation lessons must have no groups attached");
    }

    public static InvalidLessonGroupsException ForDuplicateGroup()
    {
        return new("Duplicate group in the same lesson");
    }

    public static InvalidLessonGroupsException ForInvalidGroupId()
    {
        return new("Invalid group id in lesson");
    }

    public static InvalidLessonGroupsException ForMixedAttendanceModes(
        AttendanceMode firstMode,
        AttendanceMode secondMode,
        string firstGroup,
        string secondGroup)
    {
        return new(
            $"Mixed attendance modes for a lesson are not allowed, attendances '{firstMode}' and '{secondMode}', groups '{firstGroup}' and '{secondGroup}'!");
    }
}

/// <summary>
/// A lesson carries a subgroup value that is neither numeric, special, nor a
/// configured specialization.
/// </summary>
public sealed class UnknownSubGroupException : ScheduleBuildException
{
    private UnknownSubGroupException(string message)
        : base(message)
    {
    }

    public static UnknownSubGroupException ForInvalid(
        string? subGroupValue,
        string groupContext,
        string registryContext)
    {
        return new(
            $"Invalid subgroup '{subGroupValue}' for group '{groupContext}'. "
            + "Specialization registry context: "
            + registryContext
            + ". "
            + "The configured subgroup selector accepts numeric values and configured special subgroups.");
    }
}

/// <summary>
/// A lesson carries two different subgroups: one stored directly and one
/// classified out of the partition hint.
/// </summary>
public sealed class ConflictingSubGroupException : ScheduleBuildException
{
    private ConflictingSubGroupException(string message)
        : base(message)
    {
    }

    public static ConflictingSubGroupException ForConflictingValues(string? first, string? second)
    {
        return new($"A lesson may not have two subgroups: '{first}' and '{second}'.");
    }
}

/// <summary>
/// Two weekly lessons occupy the same period, day and time slot for a shared
/// group without being separated on any split dimension.
/// </summary>
public sealed class OverlappingLessonsException : ScheduleBuildException
{
    private OverlappingLessonsException(string message)
        : base(message)
    {
    }

    public static OverlappingLessonsException ForOverlaps(IReadOnlyCollection<string> errors)
    {
        return new(
            $"The schedule has {errors.Count} overlapping lesson pairs:\n"
            + string.Join("\n", errors));
    }
}
