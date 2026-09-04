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
