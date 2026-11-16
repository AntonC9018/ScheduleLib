// PROTOTYPE for ScheduleLib #282. Throwaway.
// Toy domain for the ScopePlan section. Workflows need different context:
//   QueryLessons:  Source + Output seeds, eager Schedule.
//   CalendarSync:  Source + Settings + Output seeds, eager Schedule, explicit RemoteCalendar,
//                  derived ResolvedSettings.
//   ShowSettings:  Settings seed only; the schedule is never loaded.
//   HolidayReport: lazy Holidays, loaded only if the report actually asks for them.
//   CompareSemesters: two SourceArgs seeds, the unkeyed one and one keyed "previous".
//   ExamReminder:  lazy ExamDates whose initializer needs the schedule (known gap).
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

// ---- counts how often each expensive load ran ----
public sealed class LoadCounter
{
    private readonly Dictionary<string, int> _loads = [];

    public int this[string name] => _loads.GetValueOrDefault(name);

    public int Count(string name) => _loads[name] = this[name] + 1;
}

// ---- eager: the schedule, cached per data dir in a root singleton ----
public sealed record PlanSchedule(string DataDir, int Id);

public sealed class PlanScheduleCache(LoadCounter loads)
{
    private readonly Dictionary<string, PlanSchedule> _schedules = [];

    public async ValueTask<PlanSchedule> GetAsync(string dataDir, CancellationToken token)
    {
        if (!_schedules.TryGetValue(dataDir, out var schedule))
        {
            await Task.Delay(10, token); // stands in for parsing and InitializeSchedule
            schedule = new(dataDir, loads.Count("schedule"));
            _schedules[dataDir] = schedule;
        }
        return schedule;
    }
}

public sealed class ScheduleInitializer(PlanScheduleCache cache, SourceArgs source) : IInitializer<PlanSchedule>
{
    public ValueTask<PlanSchedule> InitializeAsync(CancellationToken token) => cache.GetAsync(source.DataDir, token);
}

// ---- explicit: a remote calendar, a network call the caller must agree to ----
public sealed record RemoteCalendar(string Owner, int EventCount);

public sealed class RemoteCalendarInitializer(ResolvedSettings settings, LoadCounter loads) : IInitializer<RemoteCalendar>
{
    public async ValueTask<RemoteCalendar> InitializeAsync(CancellationToken token)
    {
        await Task.Delay(10, token); // stands in for the Google Calendar API
        loads.Count("calendar");
        return new(settings.Teacher, EventCount: 42);
    }
}

// ---- lazy: holidays, fetched only if someone awaits them ----
public sealed record Holidays(IReadOnlyList<string> Days);

public sealed class HolidaysInitializer(SourceArgs source, LoadCounter loads) : IInitializer<Holidays>
{
    public async ValueTask<Holidays> InitializeAsync(CancellationToken token)
    {
        await Task.Delay(10, token); // stands in for reading a holiday file next to the schedule
        loads.Count("holidays");
        return new([$"{source.DataDir}: 2025-12-25", $"{source.DataDir}: 2026-01-01"]);
    }
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

public sealed class CalendarSyncHandler(TeacherFilter filter, RemoteCalendar calendar, Reporter reporter)
{
    public string Run() => reporter.Format($"{filter.Run()} vs {calendar.EventCount} remote events");
}

public sealed class ShowSettingsHandler(ResolvedSettings settings)
{
    public string Run() => $"profile {settings.Profile}, teacher {settings.Teacher}";
}

// The type says "this may not be loaded yet"; the handler decides whether to pay for it.
public sealed class HolidayReportHandler(LessonQuery query, Deferred<Holidays> holidays)
{
    public async Task<string> RunAsync(bool includeHolidays, CancellationToken token)
    {
        if (!includeHolidays)
        {
            return query.Run();
        }
        var loaded = await holidays.GetAsync(token);
        return $"{query.Run()}, holidays [{string.Join(", ", loaded.Days)}]";
    }
}

// ---- keyed seeds: same type, two values ----
public sealed class CompareSemestersHandler(
    SourceArgs current,
    [FromKeyedServices("previous")] SourceArgs previous)
{
    public string Run() => $"compare {previous.DataDir} -> {current.DataDir}";
}

// ---- known gap: a lazy value whose initializer needs an eager one ----
public sealed record ExamDates(int Count);

public sealed class ExamDatesInitializer(PlanSchedule schedule) : IInitializer<ExamDates>
{
    public ValueTask<ExamDates> InitializeAsync(CancellationToken token) => new(new ExamDates(schedule.Id * 3));
}

public sealed class ExamReminderHandler(Deferred<ExamDates> exams)
{
    public async Task<string> RunAsync(bool remind, CancellationToken token) =>
        remind ? $"{(await exams.GetAsync(token)).Count} exams" : "no reminder requested";
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

// ---- deliberately broken graphs ----
public sealed class Unregistered;

public sealed class BrokenHandler(Unregistered missing, LessonQuery query);

public sealed class EagerHolidayHandler(Holidays holidays); // Holidays is lazy

public static class ScopePlanDemo
{
    public static IServiceCollection AddPlanGraph(this IServiceCollection s)
    {
        s.AddSeed<SourceArgs>();
        s.AddSeed<SettingsArgs>();
        s.AddSeed<OutputArgs>();
        s.AddKeyedSeed<SourceArgs>("previous");
        s.AddDerived<ResolvedSettings, ResolvedSettingsFactory>();
        s.AddInitialized<PlanSchedule, ScheduleInitializer>(); // InitMode.Eager by default
        s.AddInitialized<RemoteCalendar, RemoteCalendarInitializer>(InitMode.Explicit);
        s.AddInitialized<Holidays, HolidaysInitializer>(InitMode.Lazy);
        s.AddInitialized<ExamDates, ExamDatesInitializer>(InitMode.Lazy);
        s.AddSingleton<LoadCounter>();
        s.AddSingleton<PlanScheduleCache>();
        s.AddTransient<LessonQuery>();
        s.AddTransient<TeacherFilter>();
        s.AddTransient<Reporter>();
        s.AddTransient<QueryLessonsHandler>();
        s.AddTransient<CalendarSyncHandler>();
        s.AddTransient<ShowSettingsHandler>();
        s.AddTransient<HolidayReportHandler>();
        s.AddTransient<CompareSemestersHandler>();
        s.AddTransient<ExamReminderHandler>();
        s.AddTransient<LegacyGradePlanner>();
        s.AddTransient<LegacyGradeHandler>();
        s.AddTransient<BrokenHandler>();
        s.AddTransient<EagerHolidayHandler>();
        return s;
    }

    public static async Task Run(Action<string, Func<string>> tryRun)
    {
        var services = new ServiceCollection().AddPlanGraph();

        // Build every workflow's plan up front. This is the "verifiable" step a test would run.
        var query = ScopePlan.For<QueryLessonsHandler>(services);
        var queryStrict = ScopePlan.For<QueryLessonsHandler>(services, allowImplicitInit: false);
        var sync = ScopePlan.For<CalendarSyncHandler>(services);
        var show = ScopePlan.For<ShowSettingsHandler>(services);
        var holidays = ScopePlan.For<HolidayReportHandler>(services);
        var compare = ScopePlan.For<CompareSemestersHandler>(services);
        var exams = ScopePlan.For<ExamReminderHandler>(services);
        var legacy = ScopePlan.For<LegacyGradeHandler>(services);
        var broken = ScopePlan.For<BrokenHandler>(services);
        var eagerHoliday = ScopePlan.For<EagerHolidayHandler>(services);
        Console.WriteLine("  (QueryLessonsHandler is planned twice; the second plan disallows implicit init)");
        foreach (var plan in new[] { query, queryStrict, sync, show, holidays, compare, exams, legacy, broken, eagerHoliday })
        {
            plan.Print();
        }

        await using var root = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false, // the broken handlers are registered on purpose; the plan reports them
            ValidateScopes = true,
        });
        var loads = root.GetRequiredService<LoadCounter>();
        var source = new SourceArgs("data/2025-sem1");
        var output = new OutputArgs(Jsonl: false);
        var settings = new SettingsArgs("default");
        var ct = CancellationToken.None;

        Console.WriteLine();
        Console.WriteLine("  -- eager (implicit) --");
        await using (var scope = await root.OpenAsync(query, s => s.Add(source).Add(output), ct))
        {
            tryRun("query lessons", () => scope.ServiceProvider.GetRequiredService<QueryLessonsHandler>().Run());
        }
        var before = loads["schedule"];
        await using (var scope = await root.OpenAsync(show, s => s.Add(settings), ct))
        {
            tryRun("show settings", () => scope.ServiceProvider.GetRequiredService<ShowSettingsHandler>().Run()
                + $"; schedule loads: {loads["schedule"] - before}");
        }

        Console.WriteLine("  -- implicit init disallowed for one plan --");
        Reject(tryRun, "strict query lessons, schedule not named",
            () => root.OpenAsync(queryStrict, s => s.Add(source).Add(output), ct));
        await using (var scope = await root.OpenAsync(queryStrict, s => s.Add(source).Add(output).Init<PlanSchedule>(), ct))
        {
            tryRun("strict query lessons, .Init<PlanSchedule>()",
                () => scope.ServiceProvider.GetRequiredService<QueryLessonsHandler>().Run());
        }

        Console.WriteLine("  -- explicit --");
        before = loads["calendar"];
        Reject(tryRun, "calendar sync, RemoteCalendar not named",
            () => root.OpenAsync(sync, s => s.Add(source).Add(settings).Add(output), ct));
        tryRun("  remote calendar loads during the rejected open", () => $"{loads["calendar"] - before}");
        Reject(tryRun, "query lessons, naming RemoteCalendar it does not use",
            () => root.OpenAsync(query, s => s.Add(source).Add(output).Init<RemoteCalendar>(), ct));
        await using (var scope = await root.OpenAsync(sync,
            s => s.Add(source).Add(settings).Add(output).Init<RemoteCalendar>(), ct))
        {
            tryRun("calendar sync, .Init<RemoteCalendar>()",
                () => scope.ServiceProvider.GetRequiredService<CalendarSyncHandler>().Run());
        }

        Console.WriteLine("  -- lazy --");
        await using (var scope = await root.OpenAsync(holidays, s => s.Add(source), ct))
        {
            var handler = scope.ServiceProvider.GetRequiredService<HolidayReportHandler>();
            var withoutHolidays = await handler.RunAsync(includeHolidays: false, ct);
            tryRun("holiday report, holidays not requested",
                () => $"{withoutHolidays}; holiday loads: {loads["holidays"]}");
            var withHolidays = await handler.RunAsync(includeHolidays: true, ct);
            await handler.RunAsync(includeHolidays: true, ct);
            tryRun("holiday report, requested twice",
                () => $"{withHolidays}; holiday loads: {loads["holidays"]}");
        }

        Console.WriteLine("  -- keyed seeds --");
        var previous = new SourceArgs("data/2024-sem2");
        await using (var scope = await root.OpenAsync(compare,
            s => s.Add(source).AddKeyed("previous", previous), ct))
        {
            tryRun("compare semesters, keyed \"previous\" seed",
                () => scope.ServiceProvider.GetRequiredService<CompareSemestersHandler>().Run());
        }
        Reject(tryRun, "compare semesters, keyed seed missing", () => root.OpenAsync(compare, s => s.Add(source), ct));

        Console.WriteLine("  -- KNOWN GAP: a lazy value's own inputs are not lazy --");
        before = loads["schedule"];
        await using (var scope = await root.OpenAsync(exams, s => s.Add(new SourceArgs("data/2026-sem2")), ct))
        {
            var text = await scope.ServiceProvider.GetRequiredService<ExamReminderHandler>().RunAsync(remind: false, ct);
            tryRun("exam reminder, exams never awaited",
                () => $"{text}; schedule loads anyway: {loads["schedule"] - before}");
        }

        Console.WriteLine("  -- opaque and broken --");
        await using (var scope = await root.OpenAsync(legacy, s => s.Add(source), ct))
        {
            tryRun("legacy grade (opaque: the plan cannot see it needs PlanSchedule)",
                () => scope.ServiceProvider.GetRequiredService<LegacyGradeHandler>().Run());
        }
        Reject(tryRun, "calendar sync without Settings",
            () => root.OpenAsync(sync, s => s.Add(source).Add(output).Init<RemoteCalendar>(), ct));
        Reject(tryRun, "broken plan", () => root.OpenAsync(broken, s => s.Add(source).Add(output), ct));
        Console.WriteLine($"  loads in total: schedule {loads["schedule"]}, calendar {loads["calendar"]}, "
            + $"holidays {loads["holidays"]}");
    }

    private static void Reject(Action<string, Func<string>> tryRun, string label, Func<ValueTask<AsyncServiceScope>> open)
    {
        tryRun(label, () =>
        {
            using var scope = open().AsTask().GetAwaiter().GetResult();
            return "opened?!";
        });
    }
}
