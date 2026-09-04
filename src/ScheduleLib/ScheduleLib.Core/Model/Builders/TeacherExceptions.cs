namespace ScheduleLib.Builders;

/// <summary>
/// Teacher name remaps recurse instead of mapping to the final version.
/// </summary>
public sealed class ConflictingTeacherNameRemapException : ScheduleBuildException
{
    private ConflictingTeacherNameRemapException(string message)
        : base(message)
    {
    }

    public static ConflictingTeacherNameRemapException ForRecursive()
    {
        return new(
            "Recursive teacher name remaps are not supported to prevent errors. Ensure the remap maps to the final version.");
    }
}

/// <summary>
/// A teacher name carries more parts than the model can store.
/// </summary>
public sealed class InvalidTeacherNameException : ScheduleBuildException
{
    private InvalidTeacherNameException(string message)
        : base(message)
    {
    }

    public static InvalidTeacherNameException ForTooManyParts()
    {
        return new("Too many name parts.");
    }
}
