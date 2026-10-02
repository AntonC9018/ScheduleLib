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

/// <summary>
/// A teaching period ends before it starts.
/// </summary>
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
