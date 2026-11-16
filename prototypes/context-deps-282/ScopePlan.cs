// PROTOTYPE for ScheduleLib #282. Throwaway.
// Modular seeding with a plan derived from the registrations:
// - Seeds: raw inputs supplied when a scope opens, optionally keyed. Consumers receive them
//   through constructor injection, like any other dependency.
// - Derived values: built synchronously by a factory class whose constructor lists its inputs.
// - Initialized values: built asynchronously by an initializer class. The registration picks
//   WHEN that happens (InitMode):
//     Eager    - OpenAsync runs it before handing out the scope; the caller says nothing.
//     Explicit - OpenAsync runs it too, but only if the caller names it with .Init<T>().
//     Lazy     - nothing runs in OpenAsync; consumers depend on Deferred<T> and await it.
// ScopePlan walks constructors from a workflow's root type and reports which seeds it needs,
// which initializers run and in what order, plus dependencies it cannot see through (opaque).
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Plans;

public interface IFactory<out T>
{
    T Create();
}

// Untyped view so OpenAsync can run any initializer without reflection.
public interface IInitializer
{
    ValueTask<object> InitializeUntypedAsync(CancellationToken cancellationToken);
}

public interface IInitializer<T> : IInitializer where T : class
{
    ValueTask<T> InitializeAsync(CancellationToken cancellationToken);

    async ValueTask<object> IInitializer.InitializeUntypedAsync(CancellationToken cancellationToken) =>
        await InitializeAsync(cancellationToken);
}

public enum InitMode
{
    Eager,
    Explicit,
    Lazy,
}

// Untyped view so OpenAsync can fill any slot without reflection.
internal interface ISlot
{
    void Fill(object value);
}

// Write-once slot, filled only by ScopeOpener.
public sealed class Slot<T> : ISlot where T : class
{
    private T? _value;

    public T Value => _value ?? throw new InvalidOperationException(
        $"{typeof(T).Name} is not available: open the scope through its ScopePlan.");

    void ISlot.Fill(object value)
    {
        if (_value is not null)
        {
            throw new InvalidOperationException($"{typeof(T).Name} is already filled.");
        }
        _value = (T)value;
    }
}

// Lazy async value, one per scope. Consumers see in their constructor that the value is
// not ready yet, instead of a sync property that blocks on async work.
// Prototype simplification: a failed or cancelled first attempt stays cached.
public sealed class Deferred<T>(Func<CancellationToken, ValueTask<T>> initialize) where T : class
{
    private readonly Lock _lock = new();
    private Task<T>? _task;

    public ValueTask<T> GetAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            return new(_task ??= initialize(cancellationToken).AsTask());
        }
    }
}

// A node of the graph: a service type plus its DI key (null = unkeyed).
public sealed record Node(Type Type, object? Key)
{
    public override string ToString() => Key is null ? Type.Name : $"{Type.Name}[\"{Key}\"]";
}

public sealed record InitializedEntry(Type Initializer, InitMode Mode, Type SlotType);

// Metadata kept next to the IServiceCollection so the plan can see through factories.
// Derived and initialized values are unkeyed for now; only seeds can be keyed.
public sealed class ScopeGraph
{
    internal Dictionary<Node, Type> Seeds { get; } = [];                    // seed -> slot type
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
        services.GetScopeGraph().Seeds[new(typeof(T), null)] = typeof(Slot<T>);
        services.AddScoped<Slot<T>>();
        services.AddScoped(sp => sp.GetRequiredService<Slot<T>>().Value);
        return services;
    }

    public static IServiceCollection AddKeyedSeed<T>(this IServiceCollection services, object key) where T : class
    {
        services.GetScopeGraph().Seeds[new(typeof(T), key)] = typeof(Slot<T>);
        services.AddKeyedScoped<Slot<T>>(key);
        services.AddKeyedScoped(key, (sp, k) => sp.GetRequiredKeyedService<Slot<T>>(k).Value);
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
        services.GetScopeGraph().Initialized[typeof(T)] = new(typeof(TInitializer), mode, typeof(Slot<T>));
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

public sealed record PlanStep(Type Value, InitializedEntry Entry, InitMode Mode);

public sealed record PlanSeed(Node Node, Type SlotType);

public sealed class ScopePlan
{
    public required Type Root { get; init; }
    public required IReadOnlyList<PlanSeed> Seeds { get; init; }
    public required IReadOnlyList<PlanStep> Initializers { get; init; } // run in OpenAsync, dependency order
    public required IReadOnlyList<PlanStep> Lazy { get; init; }         // run on first await
    public required IReadOnlyList<string> Opaque { get; init; }        // warnings: runtime-only
    public required IReadOnlyList<string> Errors { get; init; }        // missing registrations, cycles

    public IEnumerable<Type> ExplicitInitializers =>
        Initializers.Where(s => s.Mode == InitMode.Explicit).Select(s => s.Value);

    public static ScopePlan For<TRoot>(IServiceCollection services, bool allowImplicitInit = true) =>
        For(typeof(TRoot), services, allowImplicitInit);

    // allowImplicitInit: false turns every Eager initializer of this plan into an Explicit one,
    // so the caller must name everything that runs in OpenAsync.
    public static ScopePlan For(Type root, IServiceCollection services, bool allowImplicitInit = true)
    {
        var graph = services.GetScopeGraph();
        var seeds = new List<PlanSeed>();
        var steps = new List<PlanStep>();
        var lazy = new List<PlanStep>();
        var opaque = new List<string>();
        var errors = new List<string>();
        var done = new HashSet<Node>();
        var path = new Stack<Node>();

        Visit(new(root, null));
        return new()
        {
            Root = root,
            Seeds = seeds,
            Initializers = steps,
            Lazy = lazy,
            Opaque = opaque,
            Errors = errors,
        };

        void Visit(Node node)
        {
            if (done.Contains(node))
            {
                return;
            }
            if (path.Contains(node))
            {
                errors.Add($"cycle: {string.Join(" -> ", path.Reverse().Append(node))}");
                return;
            }
            path.Push(node);
            try
            {
                VisitCore(node);
            }
            finally
            {
                path.Pop();
                done.Add(node);
            }
        }

        void VisitCore(Node node)
        {
            var type = node.Type;
            if (graph.Seeds.TryGetValue(node, out var slotType))
            {
                seeds.Add(new(node, slotType));
                return;
            }
            if (node.Key is null && graph.Derived.TryGetValue(type, out var factory))
            {
                VisitConstructor(factory, key: null);
                return;
            }
            if (node.Key is null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Deferred<>))
            {
                VisitDeferred(type.GetGenericArguments()[0]);
                return;
            }
            if (node.Key is null && graph.Initialized.TryGetValue(type, out var entry))
            {
                if (entry.Mode == InitMode.Lazy)
                {
                    errors.Add($"{Where()} needs {type.Name}, which is lazy: depend on Deferred<{type.Name}>");
                    return;
                }
                VisitConstructor(entry.Initializer, key: null);
                var mode = entry.Mode == InitMode.Eager && !allowImplicitInit ? InitMode.Explicit : entry.Mode;
                steps.Add(new(type, entry, mode)); // post-order = inputs initialized first
                return;
            }
            if (type == typeof(IServiceProvider) || type == typeof(IServiceScopeFactory))
            {
                opaque.Add($"{Where()} injects {type.Name}: whatever it resolves is invisible to the plan");
                return;
            }

            var descriptor = services.LastOrDefault(d => d.ServiceType == type && Equals(d.ServiceKey, node.Key));
            if (descriptor is null && type == root)
            {
                VisitConstructor(type, key: null); // allow unregistered concrete roots
                return;
            }
            var implementation = descriptor switch
            {
                null => null,
                { IsKeyedService: true } => descriptor.KeyedImplementationType,
                _ => descriptor.ImplementationType,
            };
            var instance = descriptor is { IsKeyedService: true }
                ? descriptor.KeyedImplementationInstance
                : descriptor?.ImplementationInstance;
            if (descriptor is null)
            {
                errors.Add($"{Where()} needs {node}, which is not registered");
            }
            else if (implementation is not null)
            {
                VisitConstructor(implementation, node.Key);
            }
            else if (instance is null)
            {
                opaque.Add($"{node} is built by an untyped lambda factory");
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
            // The initializer runs later, but its own inputs are still planned now (see the demo's known gap).
            VisitConstructor(entry.Initializer, key: null);
            lazy.Add(new(value, entry, InitMode.Lazy));
        }

        void VisitConstructor(Type implementation, object? key)
        {
            // Microsoft DI picks the satisfiable constructor with the most parameters;
            // the prototype simply takes the longest public one.
            var constructor = implementation
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .MaxBy(c => c.GetParameters().Length);
            foreach (var parameter in constructor?.GetParameters() ?? [])
            {
                Visit(new(parameter.ParameterType, ParameterKey(parameter, key)));
            }
        }

        // Mirrors how Microsoft DI reads [FromKeyedServices] on a constructor parameter.
        static object? ParameterKey(ParameterInfo parameter, object? parentKey)
        {
            var attribute = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            return attribute?.LookupMode switch
            {
                null or ServiceKeyLookupMode.NullKey => null,
                ServiceKeyLookupMode.InheritKey => parentKey,
                _ => attribute.Key,
            };
        }

        string Where() => path.Count > 1 ? path.ElementAt(1).ToString() : root.Name;
    }

    public void Print()
    {
        Console.WriteLine($"  plan for {Root.Name}:");
        Console.WriteLine($"    seeds:        [{string.Join(", ", Seeds.Select(s => s.Node))}]");
        Console.WriteLine($"    in OpenAsync: [{string.Join(" -> ", Initializers.Select(Describe))}]");
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
    internal Dictionary<Node, object> Seeds { get; } = [];
    internal HashSet<Type> Initialize { get; } = [];

    public ScopeInputs Add<T>(T value) where T : class
    {
        Seeds[new(typeof(T), null)] = value;
        return this;
    }

    public ScopeInputs AddKeyed<T>(object key, T value) where T : class
    {
        Seeds[new(typeof(T), key)] = value;
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
    // Verifies the inputs against the plan BEFORE any work, fills the seeds, runs the
    // planned initializers in order, and only then hands out the scope.
    public static async ValueTask<AsyncServiceScope> OpenAsync(
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
        Reject("needs seeds", plan.Seeds.Select(s => s.Node).Where(n => !inputs.Seeds.ContainsKey(n)));
        Reject("needs explicit initialization of",
            plan.ExplicitInitializers.Where(t => !inputs.Initialize.Contains(t)).Select(t => new Node(t, null)));
        Reject("does not initialize explicitly",
            inputs.Initialize.Except(plan.ExplicitInitializers).Select(t => new Node(t, null)));

        var scope = root.CreateAsyncScope();
        try
        {
            var services = scope.ServiceProvider;
            foreach (var seed in plan.Seeds)
            {
                GetSlot(services, seed.SlotType, seed.Node.Key).Fill(inputs.Seeds[seed.Node]);
            }
            foreach (var step in plan.Initializers)
            {
                var initializer = (IInitializer)services.GetRequiredService(step.Entry.Initializer);
                var value = await initializer.InitializeUntypedAsync(cancellationToken);
                GetSlot(services, step.Entry.SlotType, key: null).Fill(value);
            }
            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }

        void Reject(string problem, IEnumerable<Node> nodes)
        {
            var names = nodes.Select(n => n.ToString()).ToList();
            if (names.Count > 0)
            {
                throw new InvalidOperationException(
                    $"{plan.Root.Name} {problem} [{string.Join(", ", names)}]; nothing was initialized");
            }
        }
    }

    private static ISlot GetSlot(IServiceProvider services, Type slotType, object? key) =>
        (ISlot)(key is null ? services.GetRequiredService(slotType) : services.GetRequiredKeyedService(slotType, key));
}
