using System.Diagnostics;

namespace ScheduleLib.OnlineRegistry;

public interface ILessonFilter
{
    public LessonValidity Filter(LessonFilterContext c);
}

public enum LessonValidityDecision
{
    None,
    Process,
    Skip,
    Error,
}

public readonly record struct LessonValidity
{
    public LessonValidityDecision Decision { get; }
    public object? ErrorContext { get; }

    private LessonValidity(LessonValidityDecision decision, object? errorContext)
    {
        if (errorContext != null)
        {
            Debug.Assert(decision == LessonValidityDecision.Error);
        }
        else
        {
            Debug.Assert(decision != LessonValidityDecision.Error);
        }
        ErrorContext = errorContext;
        Decision = decision;
    }

    public static LessonValidity Process => new(LessonValidityDecision.Process, errorContext: null);
    public static LessonValidity Skip => new(LessonValidityDecision.Skip, errorContext: null);
    public static LessonValidity None => new(LessonValidityDecision.None, errorContext: null);
    public static LessonValidity Error(object? errorContext = null) => new(LessonValidityDecision.Error, errorContext);
}

public readonly ref struct LessonFilterContext
{
    public readonly ref readonly LessonSearchFilter Filter;
    public readonly Schedule Schedule;
    public readonly List<AnyLessonId> Lessons;

    public LessonFilterContext(
        in LessonSearchFilter filter,
        Schedule schedule,
        List<AnyLessonId> lessons)
    {
        Schedule = schedule;
        Lessons = lessons;
        Filter = ref filter;
    }
}

public sealed class ShouldAlwaysProcessLessonFilter : ILessonFilter
{
    public static ShouldAlwaysProcessLessonFilter Instance { get; } = new();

    public LessonValidity Filter(LessonFilterContext c)
    {
        _ = c;
        return LessonValidity.Process;
    }
}
