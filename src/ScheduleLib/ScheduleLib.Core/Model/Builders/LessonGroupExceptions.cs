using ScheduleLib.Helper;

namespace ScheduleLib.Builders;

/// <summary>
/// A lesson carries two different specializations: one stored directly and one
/// classified out of the subgroup field.
/// </summary>
public sealed class ConflictingSpecializationException : ScheduleBuildException
{
    private ConflictingSpecializationException(string? first, string? second)
        : base($"A lesson may not have two specializations: '{first}' and '{second}'.")
    {
        First = first;
        Second = second;
    }

    public string? First { get; }
    public string? Second { get; }

    public static ConflictingSpecializationException ForConflictingValues(string? first, string? second)
    {
        return new(first: first, second: second);
    }
}

/// <summary>
/// The beginner/non-beginner language proficiency split is inconsistent: a
/// lesson mixes groups with and without the split, or a beginner lesson has no
/// unannotated counterpart for the same group and course.
/// </summary>
public abstract class InconsistentLanguageSplitException : ScheduleBuildException
{
    protected InconsistentLanguageSplitException(string message)
        : base(message)
    {
    }

    public static InconsistentLanguageSplitException ForMixedSplit(string groups, string course)
    {
        return new MixedSplitException(
            groups: groups,
            course: course);
    }

    public static InconsistentLanguageSplitException ForMissingCounterpart(string group, string course)
    {
        return new MissingCounterpartException(
            @group: group,
            course: course);
    }

    public sealed class MixedSplitException : InconsistentLanguageSplitException
    {
        internal MixedSplitException(string groups, string course)
            : base(
                $"The lesson for groups '{groups}' and course '{course}' mixes groups with and without the language proficiency split for that course.  One stored subgroup value could not represent both meanings.")
        {
            Groups = groups;
            Course = course;
        }

        public string Groups { get; }
        public string Course { get; }
    }

    public sealed class MissingCounterpartException : InconsistentLanguageSplitException
    {
        internal MissingCounterpartException(string group, string course)
            : base(
                $"The beginner subgroup lesson for group '{group}' and course '{course}' has no unannotated counterpart lesson for the same group and course.")
        {
            Group = group;
            Course = course;
        }

        public string Group { get; }
        public string Course { get; }
    }
}

/// <summary>
/// The observed subgroup partition of a group is malformed: numeric subgroups
/// do not form a contiguous prefix starting at I, or a group has exactly one
/// observed language subgroup instead of zero or at least two.
/// </summary>
public abstract class InvalidSubGroupPartitionException : ScheduleBuildException
{
    protected InvalidSubGroupPartitionException(string message)
        : base(message)
    {
    }

    public static InvalidSubGroupPartitionException ForNonContiguousPrefix(
        string groupName,
        int missing,
        int occurring)
    {
        return new NonContiguousPrefixException(
            groupName,
            missing: missing,
            occurring: occurring);
    }

    public static InvalidSubGroupPartitionException ForSingleLanguageSubgroup(string groupName)
    {
        return new SingleLanguageSubgroupException(
            groupName);
    }

    public sealed class NonContiguousPrefixException : InvalidSubGroupPartitionException
    {
        internal NonContiguousPrefixException(
            string groupName,
            int missing,
            int occurring)
            : base(
                $"The numeric subgroups of group '{groupName}' must form a contiguous prefix starting at I. Missing '{NumberHelper.ToRoman(missing)}' while '{NumberHelper.ToRoman(occurring)}' occurs.")
        {
            GroupName = groupName;
            Missing = missing;
            Occurring = occurring;
        }

        public string GroupName { get; }
        public int Missing { get; }
        public int Occurring { get; }
    }

    public sealed class SingleLanguageSubgroupException : InvalidSubGroupPartitionException
    {
        internal SingleLanguageSubgroupException(string groupName)
            : base(
                $"The group '{groupName}' has a single language subgroup, but a group must have either zero observed language subgroups or at least two.")
        {
            GroupName = groupName;
        }

        public string GroupName { get; }
    }
}

/// <summary>
/// A lesson's group set is malformed: a consultation carries groups, a group
/// repeats within one lesson, a group id refers to no known group, or the
/// groups mix attendance modes that may not combine.
/// </summary>
public abstract class InvalidLessonGroupsException : ScheduleBuildException
{
    protected InvalidLessonGroupsException(string message)
        : base(message)
    {
    }

    public static InvalidLessonGroupsException ForConsultationHasGroups()
    {
        return new ConsultationHasGroupsException("Consultation lessons must have no groups attached");
    }

    public static InvalidLessonGroupsException ForDuplicateGroup()
    {
        return new DuplicateGroupException("Duplicate group in the same lesson");
    }

    public static InvalidLessonGroupsException ForInvalidGroupId()
    {
        return new InvalidGroupIdException("Invalid group id in lesson");
    }

    public static InvalidLessonGroupsException ForMixedAttendanceModes(
        AttendanceMode firstMode,
        AttendanceMode secondMode,
        string firstGroup,
        string secondGroup)
    {
        return new MixedAttendanceModesException(
            firstMode: firstMode,
            secondMode: secondMode,
            firstGroup: firstGroup,
            secondGroup: secondGroup);
    }

    public sealed class ConsultationHasGroupsException : InvalidLessonGroupsException
    {
        internal ConsultationHasGroupsException(string message)
            : base(message)
        {
        }
    }

    public sealed class DuplicateGroupException : InvalidLessonGroupsException
    {
        internal DuplicateGroupException(string message)
            : base(message)
        {
        }
    }

    public sealed class InvalidGroupIdException : InvalidLessonGroupsException
    {
        internal InvalidGroupIdException(string message)
            : base(message)
        {
        }
    }

    public sealed class MixedAttendanceModesException : InvalidLessonGroupsException
    {
        internal MixedAttendanceModesException(
            AttendanceMode firstMode,
            AttendanceMode secondMode,
            string firstGroup,
            string secondGroup)
            : base(
                $"Mixed attendance modes for a lesson are not allowed, attendances '{firstMode}' and '{secondMode}', groups '{firstGroup}' and '{secondGroup}'!")
        {
            FirstMode = firstMode;
            SecondMode = secondMode;
            FirstGroup = firstGroup;
            SecondGroup = secondGroup;
        }

        public AttendanceMode FirstMode { get; }
        public AttendanceMode SecondMode { get; }
        public string FirstGroup { get; }
        public string SecondGroup { get; }
    }
}

/// <summary>
/// A lesson carries a subgroup value that is neither numeric, special, nor a
/// configured specialization.
/// </summary>
public sealed class UnknownSubGroupException : ScheduleBuildException
{
    private UnknownSubGroupException(
        string? subGroupValue,
        string groupContext,
        string registryContext)
        : base(
            $"Invalid subgroup '{subGroupValue}' for group '{groupContext}'. Specialization registry context: {registryContext}.  The configured subgroup selector accepts numeric values and configured special subgroups.")
    {
        SubGroupValue = subGroupValue;
        GroupContext = groupContext;
        RegistryContext = registryContext;
    }

    public string? SubGroupValue { get; }
    public string GroupContext { get; }
    public string RegistryContext { get; }

    public static UnknownSubGroupException ForInvalid(
        string? subGroupValue,
        string groupContext,
        string registryContext)
    {
        return new(
            subGroupValue: subGroupValue,
            groupContext: groupContext,
            registryContext: registryContext);
    }
}

/// <summary>
/// A lesson carries two different subgroups: one stored directly and one
/// classified out of the partition hint.
/// </summary>
public sealed class ConflictingSubGroupException : ScheduleBuildException
{
    private ConflictingSubGroupException(string? first, string? second)
        : base($"A lesson may not have two subgroups: '{first}' and '{second}'.")
    {
        First = first;
        Second = second;
    }

    public string? First { get; }
    public string? Second { get; }

    public static ConflictingSubGroupException ForConflictingValues(string? first, string? second)
    {
        return new(first: first, second: second);
    }
}

/// <summary>
/// Two weekly lessons occupy the same period, day and time slot for a shared
/// group without being separated on any split dimension.
/// </summary>
public sealed class OverlappingLessonsException : ScheduleBuildException
{
    private OverlappingLessonsException(int errorCount, string[] errors)
        : base($"The schedule has {errorCount} overlapping lesson pairs:\n{string.Join("\n", errors)}")
    {
        ErrorCount = errorCount;
        Errors = Array.AsReadOnly(errors);
    }

    public int ErrorCount { get; }
    public IReadOnlyList<string> Errors { get; }

    public static OverlappingLessonsException ForOverlaps(IReadOnlyCollection<string> errors)
    {
        int errorCount = errors.Count;
        string[] errorSnapshot = errors.ToArray();
        return new(errorCount, errorSnapshot);
    }
}
