namespace ScheduleLib.Builders;

/// <summary>
/// A required schedule-model value was never set: a group name or grade, a
/// lesson date, group or course reference, or a teacher last name.
/// </summary>
public sealed class UninitializedScheduleModelException : ScheduleBuildException
{
    private UninitializedScheduleModelException(string message)
        : base(message)
    {
    }

    public static UninitializedScheduleModelException ForGroupNameNotInitialized()
    {
        return new("The group name must be initialized.");
    }

    public static UninitializedScheduleModelException ForGroupGradeNotInitialized()
    {
        return new("The group grade must be initialized.");
    }

    public static UninitializedScheduleModelException ForLessonDateNotInitialized()
    {
        return new("The lesson date must be initialized.");
    }

    public static UninitializedScheduleModelException ForLessonGroupNotInitialized()
    {
        return new("The lesson group must be initialized.");
    }

    public static UninitializedScheduleModelException ForLessonCourseNotInitialized()
    {
        return new("The lesson course must be initialized.");
    }

    public static UninitializedScheduleModelException ForLessonCourseUnknown()
    {
        return new("The lesson course must refer to a known course.");
    }

    public static UninitializedScheduleModelException ForLessonCourseWithoutNames()
    {
        return new("The lesson course must have at least one name.");
    }

    public static UninitializedScheduleModelException ForTeacherLastNameNotInitialized()
    {
        return new("The teacher last name must be initialized.");
    }
}

public sealed class InvalidPeriodException : ScheduleBuildException
{
    private InvalidPeriodException(string message)
        : base(message)
    {
    }

    public static InvalidPeriodException ForEndBeforeStart()
    {
        return new("End date is before start date.");
    }
}

public sealed class MissingCourseLookupModuleException()
    : ScheduleBuildException("Lookup module must be enabled before refreshing CourseNameUnifierModule.");

public sealed class RemappingsConfiguredAfterTeachersAddedException(int teacherCount)
    : ScheduleBuildException(
        $"Remapping may only be configured before adding teachers (found {teacherCount} already added).")
{
    public int TeacherCount { get; } = teacherCount;
}

public sealed class EmptyScheduleCollectionException(Type accessorType)
    : ScheduleBuildException($"Schedule collection for {accessorType.Name} contains no elements.")
{
    public Type AccessorType { get; } = accessorType;
}

public sealed class LessonBuilderAlreadyAttachedException(WeeklyLessonId lessonId)
    : ScheduleBuildException($"Lesson builder is already attached with id {lessonId.Id}.")
{
    public WeeklyLessonId LessonId { get; } = lessonId;
}

public sealed class UnknownLessonModelKindException(Type lessonModelType)
    : ScheduleBuildException($"Unknown lesson model kind {lessonModelType.Name}: cannot split the lesson.")
{
    public Type LessonModelType { get; } = lessonModelType;
}

public sealed class NamePartsCapacityExceededException(int capacity)
    : InvalidOperationException($"NameParts has fixed capacity of {capacity}; no more space to add item.")
{
    public int Capacity { get; } = capacity;
}
