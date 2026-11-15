// PROTOTYPE for ScheduleLib #282. Throwaway.
// Modular seeding with a plan derived from the registrations:
// - Seeds: raw inputs supplied when a scope opens.
// - Derived values: built synchronously by a factory class whose constructor lists its inputs.
// - Initialized values: built asynchronously by an initializer class, before the scope is handed out.
// ScopePlan walks constructors from a workflow's root type and reports which seeds it needs,
// which initializers run and in what order, plus dependencies it cannot see through (opaque).
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Plans;

public interface IFactory<out T>
{
    T Create();
}

public interface IInitializer<T>
{
    ValueTask<T> InitializeAsync(CancellationToken cancellationToken);
}

// Write-once slot, filled only by ScopeOpener.
public sealed class Slot<T> where T : class
{
    private T? _value;

    public T Value => _value ?? throw new InvalidOperationException(
        $"{typeof(T).Name} is not available: open the scope through its ScopePlan.");

    internal void Fill(T value)
    {
        if (_value is not null)
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already filled.");
        }
        _value = value;
    }
}

// Metadata kept next to the IServiceCollection so the plan can see through factories.
public sealed class ScopeGraph
{
    internal HashSet<Type> Seeds { get; } = [];
    internal Dictionary<Type, Type> Derived { get; } = [];      // value -> factory class
    internal Dictionary<Type, Type> Initialized { get; } = [];  // value -> initializer class
}

public static class ScopeGraphRegistration
{
    public static ScopeGraph GetScopeGraph(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ScopeGraph));
        if (descriptor?.ImplementationInstance is ScopeGraph existing)
        {
            return existing;
        }
        var graph = new ScopeGraph();
        services.AddSingleton(graph);
        return graph;
    }

    public static IServiceCollection AddSeed<T>(this IServiceCollection services) where T : class
    {
        services.GetScopeGraph().Seeds.Add(typeof(T));
        services.AddScoped<Slot<T>>();
        services.AddScoped(sp => sp.GetRequiredService<Slot<T>>().Value);
        return services;
    }

    public static IServiceCollection AddDerived<T, TFactory>(this IServiceCollection services)
        where T : class
        where TFactory : class, IFactory<T>
    {
        services.GetScopeGraph().Derived[typeof(T)] = typeof(TFactory);
        services.AddTransient<TFactory>();
        services.AddScoped(sp => sp.GetRequiredService<TFactory>().Create());
        return services;
    }

    public static IServiceCollection AddInitialized<T, TInitializer>(this IServiceCollection services)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        services.GetScopeGraph().Initialized[typeof(T)] = typeof(TInitializer);
        services.AddTransient<TInitializer>();
        services.AddScoped<Slot<T>>();
        services.AddScoped(sp => sp.GetRequiredService<Slot<T>>().Value);
        return services;
    }
}

public sealed record PlanStep(Type Value, Type Initializer);

public sealed class ScopePlan
{
    public required Type Root { get; init; }
    public required IReadOnlyList<Type> Seeds { get; init; }
    public required IReadOnlyList<PlanStep> Initializers { get; init; } // dependency order
    public required IReadOnlyList<string> Opaque { get; init; }        // warnings: runtime-only
    public required IReadOnlyList<string> Errors { get; init; }        // missing registrations, cycles

    public static ScopePlan For<TRoot>(IServiceCollection services) => For(typeof(TRoot), services);

    public static ScopePlan For(Type root, IServiceCollection services)
    {
        var graph = services.GetScopeGraph();
        var seeds = new List<Type>();
        var steps = new List<PlanStep>();
        var opaque = new List<string>();
        var errors = new List<string>();
        var done = new HashSet<Type>();
        var path = new Stack<Type>();

        Visit(root);
        return new()
        {
            Root = root,
            Seeds = seeds,
            Initializers = steps,
            Opaque = opaque,
            Errors = errors,
        };

        void Visit(Type type)
        {
            if (done.Contains(type))
            {
                return;
            }
            if (path.Contains(type))
            {
                errors.Add($"cycle: {string.Join(" -> ", path.Reverse().Append(type).Select(t => t.Name))}");
                return;
            }
            path.Push(type);
            try
            {
                VisitCore(type);
            }
            finally
            {
                path.Pop();
                done.Add(type);
            }
        }

        void VisitCore(Type type)
        {
            if (graph.Seeds.Contains(type))
            {
                seeds.Add(type);
                return;
            }
            if (graph.Derived.TryGetValue(type, out var factory))
            {
                VisitConstructor(factory);
                return;
            }
            if (graph.Initialized.TryGetValue(type, out var initializer))
            {
                VisitConstructor(initializer);
                steps.Add(new(type, initializer)); // post-order = inputs initialized first
                return;
            }
            if (type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory))
            {
                opaque.Add($"{Where()} injects {type.Name}: whatever it resolves is invisible to the plan");
                return;
            }

            var descriptor = services.LastOrDefault(d => d.ServiceType == type && !d.IsKeyedService);
            if (descriptor is null && type == root)
            {
                VisitConstructor(type); // allow unregistered concrete roots
                return;
            }
            switch (descriptor)
            {
                case null:
                    errors.Add($"{Where()} needs {type.Name}, which is not registered");
                    return;
                case { ImplementationInstance: not null }:
                    return;
                case { ImplementationType: { } implementation }:
                    VisitConstructor(implementation);
                    return;
                default:
                    opaque.Add($"{type.Name} is built by an untyped lambda factory");
                    return;
            }
        }

        void VisitConstructor(Type implementation)
        {
            // Microsoft DI picks the satisfiable constructor with the most parameters;
            // the prototype simply takes the longest public one.
            var constructor = implementation
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .MaxBy(c => c.GetParameters().Length);
            foreach (var parameter in constructor?.GetParameters() ?? [])
            {
                Visit(parameter.ParameterType);
            }
        }

        string Where() => path.Count > 1 ? path.ElementAt(1).Name : root.Name;
    }

    public void Print()
    {
        Console.WriteLine($"  plan for {Root.Name}:");
        Console.WriteLine($"    seeds:        [{string.Join(", ", Seeds.Select(t => t.Name))}]");
        Console.WriteLine($"    initializers: [{string.Join(" -> ", Initializers.Select(s => s.Value.Name))}]");
        foreach (var o in Opaque)
        {
            Console.WriteLine($"    OPAQUE (runtime only): {o}");
        }
        foreach (var e in Errors)
        {
            Console.WriteLine($"    ERROR: {e}");
        }
    }
}

public sealed class SeedSet
{
    internal Dictionary<Type, object> Values { get; } = [];

    public SeedSet Add<T>(T value) where T : class
    {
        Values[typeof(T)] = value;
        return this;
    }
}

public static class ScopeOpener
{
    private static readonly MethodInfo FillMethod =
        typeof(ScopeOpener).GetMethod(nameof(Fill), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo InitializeMethod =
        typeof(ScopeOpener).GetMethod(nameof(Initialize), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Verifies the seeds against the plan BEFORE any work, fills them, runs initializers in order,
    // and only then hands out the scope.
    public static async Task<AsyncServiceScope> OpenAsync(
        this IServiceProvider root,
        ScopePlan plan,
        Action<SeedSet> seed,
        CancellationToken cancellationToken)
    {
        if (plan.Errors.Count > 0)
        {
            throw new InvalidOperationException($"plan for {plan.Root.Name} has errors");
        }
        var seeds = new SeedSet();
        seed(seeds);
        var missing = plan.Seeds.Where(t => !seeds.Values.ContainsKey(t)).Select(t => t.Name).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"{plan.Root.Name} needs seeds [{string.Join(", ", missing)}]; nothing was initialized");
        }

        var scope = root.CreateAsyncScope();
        try
        {
            foreach (var type in plan.Seeds)
            {
                FillMethod.MakeGenericMethod(type).Invoke(null, [scope.ServiceProvider, seeds.Values[type]]);
            }
            foreach (var step in plan.Initializers)
            {
                var task = (Task)InitializeMethod.MakeGenericMethod(step.Value, step.Initializer)
                    .Invoke(null, [scope.ServiceProvider, cancellationToken])!;
                await task;
            }
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private static void Fill<T>(IServiceProvider services, object value) where T : class =>
        services.GetRequiredService<Slot<T>>().Fill((T)value);

    private static async Task Initialize<T, TInitializer>(IServiceProvider services, CancellationToken token)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        var value = await services.GetRequiredService<TInitializer>().InitializeAsync(token);
        services.GetRequiredService<Slot<T>>().Fill(value);
    }
}
