using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Tests.ScopePlans;

internal sealed record SourceArgs(string DataDir);

internal sealed record SettingsArgs(string Profile);

internal sealed record QuizArgs(int Quiz);

/// <summary>Counts expensive loads and records their order.</summary>
internal sealed class LoadLog
{
    public List<string> Order { get; } = [];

    public int Count(string name)
    {
        return Order.Count(n => n == name);
    }

    public int Record(string name)
    {
        Order.Add(name);
        return Count(name);
    }
}

internal sealed record TeacherSettings(string Teacher);

internal sealed class TeacherSettingsFactory(SettingsArgs settings) : IFactory<TeacherSettings>
{
    public TeacherSettings Create()
    {
        return new($"teacher-of-{settings.Profile}");
    }
}

internal sealed record LoadedSchedule(string DataDir, int Load);

internal sealed class LoadedScheduleInitializer(SourceArgs source, LoadLog log) : IInitializer<LoadedSchedule>
{
    public ValueTask<LoadedSchedule> InitializeAsync(CancellationToken cancellationToken)
    {
        int load = log.Record("schedule");
        return new(new LoadedSchedule(source.DataDir, load));
    }
}

internal sealed class FakeScheduleInitializer : IInitializer<LoadedSchedule>
{
    public ValueTask<LoadedSchedule> InitializeAsync(CancellationToken cancellationToken)
    {
        return new(new LoadedSchedule("fake", Load: 0));
    }
}

internal sealed record RemoteCalendar(string Owner);

internal sealed class RemoteCalendarInitializer(TeacherSettings settings, LoadLog log) : IInitializer<RemoteCalendar>
{
    public ValueTask<RemoteCalendar> InitializeAsync(CancellationToken cancellationToken)
    {
        log.Record("calendar");
        return new(new RemoteCalendar(settings.Teacher));
    }
}

internal sealed class RegistryConnection(LoadLog log) : IAsyncDisposable
{
    public bool IsClosed { get; private set; }

    public ValueTask DisposeAsync()
    {
        IsClosed = true;
        log.Record("registry closed");
        return ValueTask.CompletedTask;
    }
}

internal sealed record RegistrySession(RegistryConnection Connection);

internal sealed class RegistrySessionInitializer(ScopeResources resources, LoadLog log) : IInitializer<RegistrySession>
{
    public ValueTask<RegistrySession> InitializeAsync(CancellationToken cancellationToken)
    {
        log.Record("registry opened");
#pragma warning disable CA2000 // ScopeResources closes the connection when the scope ends.
        var connection = resources.Add(new RegistryConnection(log));
#pragma warning restore CA2000
        return new(new RegistrySession(connection));
    }
}

internal sealed record QuizGrades(int Quiz, int ScheduleLoad);

internal sealed class QuizGradesInitializer(QuizArgs quiz, LoadedSchedule schedule) : IInitializer<QuizGrades>
{
    public ValueTask<QuizGrades> InitializeAsync(CancellationToken cancellationToken)
    {
        return new(new QuizGrades(Quiz: quiz.Quiz, ScheduleLoad: schedule.Load));
    }
}

internal sealed class LessonQuery(LoadedSchedule schedule)
{
    public LoadedSchedule Schedule => schedule;
}

internal sealed class QueryLessonsHandler(LessonQuery query)
{
    public LoadedSchedule Schedule => query.Schedule;
}

internal sealed class CalendarSyncHandler(RemoteCalendar calendar, LessonQuery query)
{
    public string Describe()
    {
        return $"{calendar.Owner}:{query.Schedule.Load}";
    }
}

internal sealed class ShowSettingsHandler(TeacherSettings settings)
{
    public TeacherSettings Settings => settings;
}

internal sealed class MoodleCopyHandler(QuizGrades grades, RegistrySession session)
{
    public QuizGrades Grades => grades;
    public RegistrySession Session => session;
}

internal sealed class GradeImportHandler(
    RegistrySession session,
    LoadedSchedule schedule,
    ScopeForkFactory<MoodleCopyHandler> copies)
{
    public LoadedSchedule Schedule => schedule;
    public RegistrySession Session => session;
    public ScopeForkFactory<MoodleCopyHandler> Copies => copies;
}

internal sealed class QueryForkingHandler(LoadedSchedule schedule, ScopeForkFactory<QueryLessonsHandler> queries)
{
    public LoadedSchedule Schedule => schedule;
    public ScopeForkFactory<QueryLessonsHandler> Queries => queries;
}

internal sealed class CompareSemestersHandler(SourceArgs current, [FromKeyedServices("previous")] SourceArgs previous)
{
    public string Describe()
    {
        return $"{previous.DataDir}->{current.DataDir}";
    }
}

/// <summary>Service location; its need is declared with AddHiddenNeeds.</summary>
internal sealed class LegacyPlanner(IServiceProvider services)
{
    public LessonQuery Query => services.GetRequiredService<LessonQuery>();
}

internal sealed class LegacyHandler(LegacyPlanner planner)
{
    public LegacyPlanner Planner => planner;
}

internal sealed record LegacyReport(int Load);

internal sealed class LegacyReportInitializer(IServiceProvider services, LoadLog log) : IInitializer<LegacyReport>
{
    public ValueTask<LegacyReport> InitializeAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<LessonQuery>();
        int load = log.Record("legacy report");
        return new(new LegacyReport(load));
    }
}

internal sealed class LegacyReportHandler(LegacyReport report, ScopeForkFactory<LegacyReportChild> children)
{
    public LegacyReport Report => report;
    public ScopeForkFactory<LegacyReportChild> Children => children;
}

internal sealed class LegacyReportChild(LegacyReport report)
{
    public LegacyReport Report => report;
}

/// <summary>A singleton may inject the provider: it cannot reach scoped values anyway.</summary>
internal sealed class SingletonLocator(IServiceProvider services)
{
    public IServiceProvider Services => services;
}

internal sealed class SingletonUserHandler(SingletonLocator locator, LessonQuery query)
{
    public SingletonLocator Locator => locator;
    public LessonQuery Query => query;
}

internal sealed class Wrapper<T>(T value)
    where T : notnull
{
    public T Value => value;
}

internal sealed class WrapperHandler(Wrapper<LessonQuery> wrapper)
{
    public Wrapper<LessonQuery> Wrapper => wrapper;
}

internal sealed class Unregistered;

internal sealed class BrokenHandler(Unregistered missing, LessonQuery query)
{
    public Unregistered Missing => missing;
    public LessonQuery Query => query;
}

internal sealed class DisposableValue : IDisposable
{
    public void Dispose()
    {
    }
}

internal sealed class DisposableValueInitializer : IInitializer<DisposableValue>
{
    public ValueTask<DisposableValue> InitializeAsync(CancellationToken cancellationToken)
    {
#pragma warning disable CA2000 // The plan rejects this value before the initializer runs.
        return new(new DisposableValue());
#pragma warning restore CA2000
    }
}

internal sealed class DisposableValueHandler(DisposableValue value)
{
    public DisposableValue Value => value;
}

/// <summary>A seed's declared type need not be disposable for its runtime value to be.</summary>
internal abstract class SourceBase;

internal sealed class DisposableSource : SourceBase, IDisposable
{
    public void Dispose()
    {
    }
}

internal sealed class SourceBaseHandler(SourceBase source)
{
    public SourceBase Source => source;
}

internal static class ScopePlanFixture
{
    public static readonly SourceArgs Source = new("data/2025-sem1");
    public static readonly SettingsArgs Settings = new("default");

    public static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();
        services.AddSeed<SourceArgs>();
        services.AddSeed<SettingsArgs>();
        services.AddSeed<QuizArgs>();
        services.AddSeed<SourceBase>();
        services.AddKeyedSeed<SourceArgs>("previous");
        services.AddDerived<TeacherSettings, TeacherSettingsFactory>();
        services.AddInitialized<LoadedSchedule, LoadedScheduleInitializer>();
        services.AddInitialized<RemoteCalendar, RemoteCalendarInitializer>();
        services.AddInitialized<RegistrySession, RegistrySessionInitializer>();
        services.AddInitialized<QuizGrades, QuizGradesInitializer>();
        services.AddInitialized<LegacyReport, LegacyReportInitializer>();
        services.AddInitialized<DisposableValue, DisposableValueInitializer>();
        services.AddHiddenNeeds<LegacyPlanner, LessonQuery>();
        services.AddHiddenNeeds<LegacyReportInitializer, LessonQuery>();
        services.AddSingleton<LoadLog>();
        services.AddSingleton<SingletonLocator>();
        services.AddTransient<LessonQuery>();
        services.AddTransient<LegacyPlanner>();
        services.AddScoped(typeof(Wrapper<>));
        return services;
    }

    public static ServiceProvider Build(this IServiceCollection services)
    {
        var options = new ServiceProviderOptions
        {
            ValidateScopes = true,
        };
        return services.BuildServiceProvider(options);
    }

    public static ScopeForkFactory<THandler> Forks<THandler>(this IServiceProvider services)
        where THandler : notnull
    {
        return services.GetRequiredService<ScopeForkFactory<THandler>>();
    }

    public static ValueTask<PlannedScope<THandler>> OpenAsync<THandler>(
        this IServiceProvider services,
        Action<ScopeInputs>? supply)
        where THandler : notnull
    {
        var forks = services.Forks<THandler>();
        return forks.OpenAsync(supply, CancellationToken.None);
    }

    public static void SupplySource(ScopeInputs inputs)
    {
        inputs.Add(Source);
    }

    public static ScopePlan PlanOf<THandler>(this IServiceProvider root)
    {
        var graph = root.GetRequiredService<ScopePlanGraph>();
        return graph.GetPlan(typeof(THandler));
    }
}
