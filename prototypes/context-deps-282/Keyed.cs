// PROTOTYPE for ScheduleLib #282. Throwaway.
// The same graph for the keyed-services variant: every hop that must carry the context key
// is annotated with [FromKeyedServices] without an argument (= inherit the parent's key).
using Microsoft.Extensions.DependencyInjection;

namespace Keyed;

public sealed class LessonSource([FromKeyedServices] Schedule schedule) : ILessonSource
{
    public string Describe() => $"lessons of {schedule.Context.Name} (schedule #{schedule.Id})";
}

public sealed class Planner([FromKeyedServices] ILessonSource source, Shared shared)
{
    public string Plan() => $"{source.Describe()}, shared #{shared.Id}";
}

public sealed class Handler([FromKeyedServices] Planner planner)
{
    public string Run() => planner.Plan();
}

// Someone forgot the annotation on one intermediate hop.
public sealed class PlannerWithoutAnnotation(ILessonSource source, Shared shared)
{
    public string Plan() => $"{source.Describe()}, shared #{shared.Id}";
}

public sealed class HandlerOverBrokenHop([FromKeyedServices] PlannerWithoutAnnotation planner)
{
    public string Run() => planner.Plan();
}

public sealed class CachingPlanner([FromKeyedServices] ILessonSource source)
{
    public string Plan() => source.Describe();
}
