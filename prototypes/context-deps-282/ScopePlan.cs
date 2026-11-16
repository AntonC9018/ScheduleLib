// PROTOTYPE for ScheduleLib #282. Throwaway.
// Modular seeding with a plan derived from the registrations:
// - Seeds: raw inputs supplied when a scope opens.
// - Derived values: built synchronously by a factory class whose constructor lists its inputs.
// - Initialized values: built asynchronously by an initializer class. The registration picks
//   WHEN that happens (InitMode):
//     Eager    - the opener runs it before handing out the scope; the caller says nothing.
//     Explicit - the opener runs it too, but only if the caller names it when opening the scope.
//     Lazy     - nothing runs at open; consumers depend on Deferred<T> and await it on first use.
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

public enum InitMode
{
    Eager,
    Explicit,
    Lazy,
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

// Lazy async value, one per scope. Consumers see in their constructor that the value is
// not ready yet, instead of a sync property that blocks on async work.
// Prototype simplification: a failed or cancelled first attempt stays cached.
public sealed class Deferred<T>(Func<CancellationToken, ValueTask<T>> initialize) where T : class
{
    private readonly Lock _lock = new();
    private Task<T>? _task;

    public Task<T> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return _task ??= initialize(cancellationToken).AsTask();
        }
    }
}

public sealed record InitializedEntry(Type Initializer, InitMode Mode);

// Metadata kept next to the IServiceCollection so the plan can see through factories.
public sealed class ScopeGraph
{
    internal HashSet<Type> Seeds { get; } = [];
    internal Dictionary<Type, Type> Derived { get; } = [];                  // value -> factory class
    internal Dictionary<Type, InitializedEntry> Initialized { get; } = [];  // value -> initializer
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

    public static IServiceCollection AddInitialized<T, TInitializer>(
        this IServiceCollection services,
        InitMode mode = InitMode.Eager)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        services.GetScopeGraph().Initialized[typeof(T)] = new(typeof(TInitializer), mode);
        services.AddTransient<TInitializer>();
        if (mode == InitMode.Lazy)
        {
            // Only Deferred<T> is resolvable; asking for T itself is a plan error.
            services.AddScoped(sp => new Deferred<T>(
                token => sp.GetRequiredService<TInitializer>().InitializeAsync(token)));
        }
        else
        {
            services.AddScoped<Slot<T>>();
            services.AddScoped(sp => sp.GetRequiredService<Slot<T>>().Value);
        }
        return services;
    }
}

public sealed record PlanStep(Type Value, Type Initializer, InitMode Mode);

public sealed class ScopePlan
{
    public required Type Root { get; init; }
    public required IReadOnlyList<Type> Seeds { get; init; }
    public required IReadOnlyList<PlanStep> Initializers { get; init; } // run at open, dependency order
    public required IReadOnlyList<PlanStep> Lazy { get; init; }         // run on first await
    public required IReadOnlyList<string> Opaque { get; init; }        // warnings: runtime-only
    public required IReadOnlyList<string> Errors { get; init; }        // missing registrations, cycles

    public IEnumerable<Type> ExplicitInitializers =>
        Initializers.Where(s => s.Mode == InitMode.Explicit).Select(s => s.Value);

    public static ScopePlan For<TRoot>(IServiceCollection services, bool allowImplicitInit = true) =>
        For(typeof(TRoot), services, allowImplicitInit);

    // allowImplicitInit: false turns every Eager initializer of this plan into an Explicit one,
    // so the caller must name everything that runs when the scope opens.
    public static ScopePlan For(Type root, IServiceCollection services, bool allowImplicitInit = true)
    {
        var graph = services.GetScopeGraph();
        var seeds = new List<Type>();
        var steps = new List<PlanStep>();
        var lazy = new List<PlanStep>();
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
            Lazy = lazy,
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
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Deferred<>))
            {
                VisitDeferred(type.GetGenericArguments()[0]);
                return;
            }
            if (graph.Initialized.TryGetValue(type, out var entry))
            {
                if (entry.Mode == InitMode.Lazy)
                {
                    errors.Add($"{Where()} needs {type.Name}, which is lazy: depend on Deferred<{type.Name}>");
                    return;
                }
                VisitConstructor(entry.Initializer);
                var mode = entry.Mode == InitMode.Eager && !allowImplicitInit ? InitMode.Explicit : entry.Mode;
                steps.Add(new(type, entry.Initializer, mode)); // post-order = inputs initialized first
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

        void VisitDeferred(Type value)
        {
            if (!graph.Initialized.TryGetValue(value, out var entry))
            {
                errors.Add($"{Where()} needs Deferred<{value.Name}>, but {value.Name} has no initializer");
                return;
            }
            if (entry.Mode != InitMode.Lazy)
            {
                errors.Add($"{Where()} needs Deferred<{value.Name}>, but {value.Name} is {entry.Mode}: depend on it directly");
                return;
            }
            // The initializer runs later, but its seeds must still be there when the scope opens.
            VisitConstructor(entry.Initializer);
            lazy.Add(new(value, entry.Initializer, InitMode.Lazy));
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
        Console.WriteLine($"    at open:      [{string.Join(" -> ", Initializers.Select(Describe))}]");
        if (Lazy.Count > 0)
        {
            Console.WriteLine($"    lazy:         [{string.Join(", ", Lazy.Select(s => s.Value.Name))}]");
        }
        foreach (var o in Opaque)
        {
            Console.WriteLine($"    OPAQUE (runtime only): {o}");
        }
        foreach (var e in Errors)
        {
            Console.WriteLine($"    ERROR: {e}");
        }

        static string Describe(PlanStep step) => $"{step.Value.Name} ({step.Mode.ToString().ToLowerInvariant()})";
    }
}

// What the caller provides when opening a scope: seed values, plus the names of
// Explicit initializers it agrees to run.
public sealed class ScopeInputs
{
    internal Dictionary<Type, object> Seeds { get; } = [];
    internal HashSet<Type> Initialize { get; } = [];

    public ScopeInputs Add<T>(T value) where T : class
    {
        Seeds[typeof(T)] = value;
        return this;
    }

    public ScopeInputs Init<T>() where T : class
    {
        Initialize.Add(typeof(T));
        return this;
    }
}

public static class ScopeOpener
{
    private static readonly MethodInfo FillMethod =
        typeof(ScopeOpener).GetMethod(nameof(Fill), BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly MethodInfo InitializeMethod =
        typeof(ScopeOpener).GetMethod(nameof(Initialize), BindingFlags.NonPublic | BindingFlags.Static)!;

    // Verifies the inputs against the plan BEFORE any work, fills the seeds, runs the
    // at-open initializers in order, and only then hands out the scope.
    public static async Task<AsyncServiceScope> OpenAsync(
        this IServiceProvider root,
        ScopePlan plan,
        Action<ScopeInputs> provide,
        CancellationToken cancellationToken)
    {
        if (plan.Errors.Count > 0)
        {
            throw new InvalidOperationException($"plan for {plan.Root.Name} has errors");
        }
        var inputs = new ScopeInputs();
        provide(inputs);
        Reject("needs seeds", plan.Seeds.Where(t => !inputs.Seeds.ContainsKey(t)));
        Reject("needs explicit initialization of", plan.ExplicitInitializers.Where(t => !inputs.Initialize.Contains(t)));
        Reject("does not initialize explicitly", inputs.Initialize.Except(plan.ExplicitInitializers));

        var scope = root.CreateAsyncScope();
        try
        {
            foreach (var type in plan.Seeds)
            {
                FillMethod.MakeGenericMethod(type).Invoke(null, [scope.ServiceProvider, inputs.Seeds[type]]);
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

        void Reject(string problem, IEnumerable<Type> types)
        {
            var names = types.Select(t => t.Name).ToList();
            if (names.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{plan.Root.Name} {problem} [{string.Join(", ", names)}]; nothing was initialized");
            }
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
