// PROTOTYPE for ScheduleLib #282. Throwaway. Run: dotnet run
// Compares three ways to deliver a context value (Schedule) deep into a graph:
//   1. Seeded scope (Microsoft DI): one root, a scoped holder seeded once when the scope opens.
//   2. Keyed services with an inherited key (Microsoft DI): the context is the service key.
//   3. Autofac child lifetime scopes with per-scope registrations.
//   4. Section 1 extended: modular seeds, factory classes, async initializers and a plan
//      derived from the registrations (ScopePlan.cs, ScopePlanDemo.cs).
// Each variant runs the same scenarios and prints whether it works, fails at build time,
// or fails only at run time.
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var a = new ScheduleContext("2025-sem1");
var b = new ScheduleContext("2026-sem2");

Section("1. Seeded scope (Microsoft DI)");
{
    var root = Ms.Build(s => s.AddSeededGraph());
    if (root is not null)
    {
        await using var sa = root.OpenContextScope(a);
        await using var sb = root.OpenContextScope(b);
        await using var sa2 = root.OpenContextScope(a);
        Try("context A", () => sa.ServiceProvider.GetRequiredService<Handler>().Run());
        Try("context B", () => sb.ServiceProvider.GetRequiredService<Handler>().Run());
        Try("context A, second scope (schedule reused?)",
            () => sa2.ServiceProvider.GetRequiredService<Handler>().Run());
        Try("scope opened without seeding", () =>
        {
            using var raw = root.CreateScope();
            return raw.ServiceProvider.GetRequiredService<Handler>().Run();
        });
    }

    var globalFake = Ms.Build(s =>
    {
        s.AddSeededGraph();
        s.RemoveAll<ILessonSource>();
        s.AddTransient<ILessonSource, FakeLessonSource>();
    });
    if (globalFake is not null)
    {
        await using var sa = globalFake.OpenContextScope(a);
        Try("test replaces leaf for all contexts",
            () => sa.ServiceProvider.GetRequiredService<Handler>().Run());
    }

    // Not shown: swapping a leaf for ONE context only. Registrations belong to the root,
    // so every seeded scope shares them; only the seeded values differ.

    Ms.Build(s =>
    {
        s.AddSeededGraph();
        s.AddSingleton<CachingPlanner>();
    }, label: "captive singleton over Schedule");
}

Section("2. Keyed services, inherited key (Microsoft DI)");
{
    var root = Ms.Build(s => s.AddKeyedGraph());
    if (root is not null)
    {
        await using var sa = root.CreateAsyncScope();
        await using var sb = root.CreateAsyncScope();
        await using var sa2 = root.CreateAsyncScope();
        Try("context A", () => sa.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(a).Run());
        Try("context B", () => sb.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(b).Run());
        Try("context A, second scope (schedule reused?)",
            () => sa2.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(a).Run());
        Try("two contexts in ONE scope",
            () => sa.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(b).Run());
        Try("resolved without a key",
            () => sa.ServiceProvider.GetRequiredService<Keyed.Handler>().Run());
    }

    var globalFake = Ms.Build(s =>
    {
        s.AddKeyedGraph();
        s.RemoveAllKeyed<ILessonSource>(KeyedService.AnyKey);
        s.AddKeyedTransient<ILessonSource, FakeLessonSource>(KeyedService.AnyKey);
    });
    if (globalFake is not null)
    {
        await using var sa = globalFake.CreateAsyncScope();
        Try("test replaces leaf for all contexts",
            () => sa.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(a).Run());
    }

    var perContext = Ms.Build(s =>
    {
        s.AddKeyedGraph();
        s.AddKeyedTransient<ILessonSource, FakeLessonSource>(b); // a specific key beats AnyKey
    });
    if (perContext is not null)
    {
        await using var scope = perContext.CreateAsyncScope();
        Try("per-context override, A (specific key)",
            () => scope.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(a).Run());
        Try("per-context override, B (specific key)",
            () => scope.ServiceProvider.GetRequiredKeyedService<Keyed.Handler>(b).Run());
    }

    Ms.Build(s =>
    {
        s.AddKeyedGraph();
        s.AddKeyedTransient<Keyed.PlannerWithoutAnnotation>(KeyedService.AnyKey);
        s.AddKeyedTransient<Keyed.HandlerOverBrokenHop>(KeyedService.AnyKey);
    }, label: "intermediate hop missing [FromKeyedServices]");

    Ms.Build(s =>
    {
        s.AddKeyedGraph();
        s.AddKeyedSingleton<Keyed.CachingPlanner>(KeyedService.AnyKey);
    }, label: "captive keyed singleton over Schedule");
}

Section("3. Autofac child lifetime scopes");
{
    var root = Af.Build(_ => { });
    {
        // Three scopes open at the same time (not nested): two contexts plus a second A.
        await using var sa = root.OpenContextScope(a);
        await using var sb = root.OpenContextScope(b);
        await using var sa2 = root.OpenContextScope(a);
        Try("context A", () => sa.Resolve<Handler>().Run());
        Try("context B", () => sb.Resolve<Handler>().Run());
        Try("context A, second scope (schedule reused?)", () => sa2.Resolve<Handler>().Run());
    }
    Try("scope opened without context registrations", () =>
    {
        using var raw = root.BeginLifetimeScope();
        return raw.Resolve<Handler>().Run();
    });

    var globalFake = Af.Build(cb => cb.RegisterType<FakeLessonSource>().As<ILessonSource>());
    {
        await using var sa = globalFake.OpenContextScope(a);
        Try("test replaces leaf for all contexts", () => sa.Resolve<Handler>().Run());
    }

    {
        await using var sa = root.OpenContextScope(a);
        await using var sb = root.OpenContextScope(b,
            cb => cb.RegisterType<FakeLessonSource>().As<ILessonSource>());
        Try("per-context override, A (scope registration)", () => sa.Resolve<Handler>().Run());
        Try("per-context override, B (scope registration)", () => sb.Resolve<Handler>().Run());
    }

    var captive = Af.Build(cb => cb.RegisterType<CachingPlanner>().SingleInstance());
    Console.WriteLine("  (Autofac has no build-time validation; the container built fine)");
    {
        await using var sa = captive.OpenContextScope(a);
        Try("captive singleton over Schedule", () => sa.Resolve<CachingPlanner>().Plan());
    }
}

Section("4. Modular seeding with a ScopePlan (Microsoft DI)");
await Plans.ScopePlanDemo.Run(Try);

Section("5. Disposal trap: a lambda registration owns what it hands out");
Disposal.DisposalCheck.Run(Try);

Console.WriteLine();
Console.WriteLine($"Schedules built in total: {Counters.Schedules}; Shared built in total: {Counters.Shared}");
Console.WriteLine("(Shared is built once per root container; each variant builds its own roots.)");
return;

static void Section(string title)
{
    Console.WriteLine();
    Console.WriteLine($"=== {title} ===");
}

static void Try(string label, Func<string> run)
{
    try
    {
        Console.WriteLine($"  OK            {label}: {run()}");
    }
    catch (Exception e)
    {
        Console.WriteLine($"  RUN-TIME ERR  {label}: {FirstLine(e)}");
    }
}

static string FirstLine(Exception e)
{
    var message = e.Message.Split('\n')[0].Trim();
    return message.Length > 150 ? message[..150] + "…" : message;
}

// ---------------- Seeded scope ----------------

// Set-once holder. Only OpenContextScope can seed it, and a second seed is an error.
public sealed class Seed<T> where T : class
{
    private T? _value;

    public T Value => _value ?? throw new InvalidOperationException(
        $"{typeof(T).Name} was not seeded; open the scope with OpenContextScope.");

    internal void Set(T value)
    {
        if (_value is not null)
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already seeded.");
        }
        _value = value;
    }
}

public static class Ms
{
    public static IServiceCollection AddSeededGraph(this IServiceCollection s)
    {
        s.AddSingleton<Shared>();
        s.AddSingleton<ScheduleCache>();
        s.AddScoped<Seed<ScheduleContext>>();
        s.AddScoped(sp => sp.GetRequiredService<Seed<ScheduleContext>>().Value);
        s.AddScoped(sp => sp.GetRequiredService<ScheduleCache>().Get(sp.GetRequiredService<ScheduleContext>()));
        s.AddTransient<ILessonSource, LessonSource>();
        s.AddTransient<Planner>();
        s.AddTransient<Handler>();
        return s;
    }

    // The single place that knows about seeding: no service ever calls Set.
    public static AsyncServiceScope OpenContextScope(this IServiceProvider root, ScheduleContext context)
    {
        var scope = root.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<Seed<ScheduleContext>>().Set(context);
        return scope;
    }

    public static IServiceCollection AddKeyedGraph(this IServiceCollection s)
    {
        s.AddSingleton<Shared>();
        s.AddSingleton<ScheduleCache>();
        s.AddKeyedScoped<Schedule>(KeyedService.AnyKey,
            (sp, key) => sp.GetRequiredService<ScheduleCache>().Get((ScheduleContext)key!));
        s.AddKeyedTransient<ILessonSource, Keyed.LessonSource>(KeyedService.AnyKey);
        s.AddKeyedTransient<Keyed.Planner>(KeyedService.AnyKey);
        s.AddKeyedTransient<Keyed.Handler>(KeyedService.AnyKey);
        return s;
    }

    public static ServiceProvider? Build(Action<IServiceCollection> configure, string? label = null)
    {
        var services = new ServiceCollection();
        configure(services);
        try
        {
            var sp = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
            if (label is not null)
            {
                Console.WriteLine($"  BUILT (no err) {label}");
            }
            return sp;
        }
        catch (Exception e)
        {
            Console.WriteLine($"  BUILD-TIME ERR {label ?? "container"}: {FirstLine(e)}");
            return null;
        }
    }

    private static string FirstLine(Exception e)
    {
        var inner = e is AggregateException { InnerExceptions.Count: > 0 } agg ? agg.InnerExceptions[0] : e;
        var message = inner.Message.Split('\n')[0].Trim();
        return message.Length > 150 ? message[..150] + "…" : message;
    }
}

// ---------------- Autofac ----------------

public static class Af
{
    public static IContainer Build(Action<ContainerBuilder> configure)
    {
        var builder = new ContainerBuilder();
        builder.RegisterType<Shared>().SingleInstance();
        builder.RegisterType<ScheduleCache>().SingleInstance();
        // RegisterType<T>() registers T as itself with a new instance per resolution
        // (Microsoft DI: AddTransient<T>()). .As<I>() exposes it under interface I instead.
        builder.RegisterType<LessonSource>().As<ILessonSource>();
        builder.RegisterType<Planner>();
        builder.RegisterType<Handler>();
        configure(builder);
        return builder.Build();
    }

    // Context values are registrations of the child scope itself, not values seeded later.
    public static ILifetimeScope OpenContextScope(
        this ILifetimeScope root,
        ScheduleContext context,
        Action<ContainerBuilder>? overrides = null)
    {
        return root.BeginLifetimeScope(cb =>
        {
            cb.RegisterInstance(context);
            cb.Register(c => c.Resolve<ScheduleCache>().Get(context)).InstancePerLifetimeScope();
            overrides?.Invoke(cb);
        });
    }
}
