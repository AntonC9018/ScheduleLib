// PROTOTYPE for ScheduleLib #282. Throwaway.
// Toy domain for the ScopePlan section. Three workflows need different context:
//   QueryLessons:  Source + Output seeds, async Schedule.
//   CalendarSync:  Source + Settings + Output seeds, async Schedule, derived ResolvedSettings.
//   ShowSettings:  Settings seed only; the schedule is never loaded.
using Microsoft.Extensions.DependencyInjection;

namespace Plans;

// ---- seeds (raw inputs) ----
public sealed record SourceArgs(string DataDir);
public sealed record SettingsArgs(string Profile);
public sealed record OutputArgs(bool Jsonl);

// ---- derived value and its factory class ----
public sealed record ResolvedSettings(string Profile, string Teacher);

public sealed class ResolvedSettingsFactory(SettingsArgs args) : IFactory<ResolvedSettings>
{
    public ResolvedSettings Create() => new(args.Profile, Teacher: $"teacher-of-{args.Profile}");
}

// ---- async initialized value and its initializer class ----
public sealed record PlanSchedule(string DataDir, int Id);

public sealed class PlanScheduleCache
{
    private readonly Dictionary<string, PlanSchedule> _schedules = [];
    public int Loads { get; private set; }

    public async ValueTask<PlanSchedule> GetAsync(string dataDir, CancellationToken token)
    {
        if (!_schedules.TryGetValue(dataDir, out var schedule))
        {
            await Task.Delay(10, token); // stands in for parsing and InitializeSchedule
            Loads++;
            schedule = new(dataDir, Loads);
            _schedules[dataDir] = schedule;
        }
        return schedule;
    }
}

public sealed class ScheduleInitializer(PlanScheduleCache cache, SourceArgs source) : IInitializer<PlanSchedule>
{
    public ValueTask<PlanSchedule> InitializeAsync(CancellationToken token) => cache.GetAsync(source.DataDir, token);
}

// ---- ordinary services: none of them mention seeds or initialization ----
public sealed class LessonQuery(PlanSchedule schedule)
{
    public string Run() => $"lessons from {schedule.DataDir} (schedule #{schedule.Id})";
}

public sealed class TeacherFilter(ResolvedSettings settings, PlanSchedule schedule)
{
    public string Run() => $"{settings.Teacher}'s events from schedule #{schedule.Id}";
}

public sealed class Reporter(OutputArgs output)
{
    public string Format(string text) => output.Jsonl ? $"{{\"result\":\"{text}\"}}" : text;
}

public sealed class QueryLessonsHandler(LessonQuery query, Reporter reporter)
{
    public string Run() => reporter.Format(query.Run());
}

public sealed class CalendarSyncHandler(TeacherFilter filter, Reporter reporter)
{
    public string Run() => reporter.Format(filter.Run());
}

public sealed class ShowSettingsHandler(ResolvedSettings settings)
{
    public string Run() => $"profile {settings.Profile}, teacher {settings.Teacher}";
}

// ---- deliberately hidden dependency (service location) ----
public sealed class LegacyGradePlanner(IServiceProvider services)
{
    public string Run() => services.GetRequiredService<LessonQuery>().Run();
}

public sealed class LegacyGradeHandler(LegacyGradePlanner planner)
{
    public string Run() => planner.Run();
}

// ---- deliberately broken graph ----
public sealed class Unregistered;

public sealed class BrokenHandler(Unregistered missing, LessonQuery query);

public static class ScopePlanDemo
{
    public static IServiceCollection AddPlanGraph(this IServiceCollection s)
    {
        s.AddSeed<SourceArgs>();
        s.AddSeed<SettingsArgs>();
        s.AddSeed<OutputArgs>();
        s.AddDerived<ResolvedSettings, ResolvedSettingsFactory>();
        s.AddInitialized<PlanSchedule, ScheduleInitializer>();
        s.AddSingleton<PlanScheduleCache>();
        s.AddTransient<LessonQuery>();
        s.AddTransient<TeacherFilter>();
        s.AddTransient<Reporter>();
        s.AddTransient<QueryLessonsHandler>();
        s.AddTransient<CalendarSyncHandler>();
        s.AddTransient<ShowSettingsHandler>();
        s.AddTransient<LegacyGradePlanner>();
        s.AddTransient<LegacyGradeHandler>();
        s.AddTransient<BrokenHandler>();
        return s;
    }

    public static async Task Run(Action<string, Func<string>> tryRun)
    {
        var services = new ServiceCollection().AddPlanGraph();

        // Startup check: build every workflow's plan once. This is the "verifiable" step.
        var query = ScopePlan.For<QueryLessonsHandler>(services);
        var sync = ScopePlan.For<CalendarSyncHandler>(services);
        var show = ScopePlan.For<ShowSettingsHandler>(services);
        var legacy = ScopePlan.For<LegacyGradeHandler>(services);
        var broken = ScopePlan.For<BrokenHandler>(services);
        foreach (var plan in new[] { query, sync, show, legacy, broken })
        {
            plan.Print();
        }

        await using var root = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false, // BrokenHandler is registered on purpose; the plan reports it
            ValidateScopes = true,
        });
        var cache = root.GetRequiredService<PlanScheduleCache>();
        var source = new SourceArgs("data/2025-sem1");
        var output = new OutputArgs(Jsonl: false);
        var settings = new SettingsArgs("default");
        var ct = CancellationToken.None;

        Console.WriteLine();
        await using (var scope = await root.OpenAsync(query, s => s.Add(source).Add(output), ct))
        {
            tryRun("query lessons (Source + Output)",
                () => scope.ServiceProvider.GetRequiredService<QueryLessonsHandler>().Run());
        }
        await using (var scope = await root.OpenAsync(sync, s => s.Add(source).Add(settings).Add(output), ct))
        {
            tryRun("calendar sync (Source + Settings + Output)",
                () => scope.ServiceProvider.GetRequiredService<CalendarSyncHandler>().Run());
        }
        var loadsBefore = cache.Loads;
        await using (var scope = await root.OpenAsync(show, s => s.Add(settings), ct))
        {
            tryRun("show settings (Settings only)",
                () => scope.ServiceProvider.GetRequiredService<ShowSettingsHandler>().Run()
                    + $"; schedule loads during this scope: {cache.Loads - loadsBefore}");
        }
        await using (var scope = await root.OpenAsync(legacy, s => s.Add(source), ct))
        {
            tryRun("legacy grade (opaque: the plan cannot see it needs PlanSchedule)",
                () => scope.ServiceProvider.GetRequiredService<LegacyGradeHandler>().Run());
        }

        tryRun("calendar sync opened without Settings", () =>
        {
            var before = cache.Loads;
            try
            {
                root.OpenAsync(sync, s => s.Add(source).Add(output), ct).GetAwaiter().GetResult();
                return "opened?!";
            }
            finally
            {
                Console.WriteLine($"                (schedule loads during the failed open: {cache.Loads - before})");
            }
        });
        tryRun("broken plan opened", () =>
        {
            root.OpenAsync(broken, s => s.Add(source).Add(output), ct).GetAwaiter().GetResult();
            return "opened?!";
        });
        Console.WriteLine($"  schedule loads in total: {cache.Loads} (cached per data dir)");
    }
}
