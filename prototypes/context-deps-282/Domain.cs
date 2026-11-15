// PROTOTYPE for ScheduleLib #282. Throwaway.
// Toy graph: Handler -> Planner -> ILessonSource -> Schedule (the context value X).
// Shared is context-independent and must be built once for all contexts.
using System.Collections.Concurrent;

public sealed record ScheduleContext(string Name);

public static class Counters
{
    public static int Schedules;
    public static int Shared;
}

public sealed class Schedule(ScheduleContext context)
{
    public ScheduleContext Context { get; } = context;
    public int Id { get; } = Interlocked.Increment(ref Counters.Schedules);
}

// Root singleton: one Schedule per context, reused by every scope of that context.
// This is what a long-lived server would keep between requests.
public sealed class ScheduleCache
{
    private readonly ConcurrentDictionary<ScheduleContext, Schedule> _schedules = new();
    public Schedule Get(ScheduleContext context) => _schedules.GetOrAdd(context, c => new Schedule(c));
}

public sealed class Shared
{
    public int Id { get; } = Interlocked.Increment(ref Counters.Shared);
}

public interface ILessonSource
{
    string Describe();
}

public sealed class FakeLessonSource : ILessonSource
{
    public string Describe() => "FAKE lessons";
}

// ---- Plain classes, used by the seeded-scope and Autofac variants ----

public sealed class LessonSource(Schedule schedule) : ILessonSource
{
    public string Describe() => $"lessons of {schedule.Context.Name} (schedule #{schedule.Id})";
}

public sealed class Planner(ILessonSource source, Shared shared)
{
    public string Plan() => $"{source.Describe()}, shared #{shared.Id}";
}

public sealed class Handler(Planner planner)
{
    public string Run() => planner.Plan();
}

// A singleton that (wrongly) depends on context-dependent data: a captive dependency.
public sealed class CachingPlanner(ILessonSource source)
{
    public string Plan() => source.Describe();
}
