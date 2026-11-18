using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

internal sealed record InitializedRegistration(Type Initializer, Type SlotType);

/// <summary>Scope-plan metadata kept next to the service collection, so plans can see through factories.</summary>
internal sealed class ScopeRegistrations
{
    /// <summary>Seed node to its slot type.</summary>
    public Dictionary<ScopeNode, Type> Seeds { get; } = [];

    /// <summary>Derived value type to its factory class.</summary>
    public Dictionary<Type, Type> Derived { get; } = [];

    public Dictionary<Type, InitializedRegistration> Initialized { get; } = [];

    /// <summary>Service, factory or initializer implementation type to what it resolves at run time.</summary>
    public Dictionary<Type, List<ScopeNode>> HiddenNeeds { get; } = [];

    public Type SlotTypeOf(ScopeNode node)
    {
        if (Seeds.TryGetValue(node, out var seedSlot))
        {
            return seedSlot;
        }
        return Initialized[node.Type].SlotType;
    }
}

public static class ScopePlanServiceCollectionExtensions
{
    /// <summary>A raw input supplied when a scope opens. The caller owns it; the scope never disposes it.</summary>
    public static IServiceCollection AddSeed<T>(this IServiceCollection services)
        where T : class
    {
        services.GetScopeRegistrations().Seeds[ScopeNode.Unkeyed(typeof(T))] = typeof(Slot<T>);
        services.AddScoped<Slot<T>>();
        services.AddScoped(static sp => sp.GetRequiredService<Slot<T>>().Value);
        return services;
    }

    /// <summary>
    /// A keyed seed. Consumers take it through <see cref="FromKeyedServicesAttribute"/>
    /// and stay unkeyed themselves.
    /// </summary>
    public static IServiceCollection AddKeyedSeed<T>(this IServiceCollection services, object key)
        where T : class
    {
        services.GetScopeRegistrations().Seeds[new(typeof(T), key)] = typeof(Slot<T>);
        services.AddKeyedScoped<Slot<T>>(key);
        services.AddKeyedScoped(key, static (sp, k) => sp.GetRequiredKeyedService<Slot<T>>(k).Value);
        return services;
    }

    /// <summary>A value built synchronously in each scope by <typeparamref name="TFactory"/>.</summary>
    public static IServiceCollection AddDerived<T, TFactory>(this IServiceCollection services)
        where T : class
        where TFactory : class, IFactory<T>
    {
        services.GetScopeRegistrations().Derived[typeof(T)] = typeof(TFactory);
        services.AddTransient<TFactory>();
        services.AddScoped(static sp =>
        {
            var value = sp.GetRequiredService<TFactory>().Create();
            ScopeValueGuard.RejectDisposable(ScopeNode.Unkeyed(typeof(T)), value);
            return value;
        });
        return services;
    }

    /// <summary>
    /// A value built asynchronously by <typeparamref name="TInitializer"/> when the scope opens.
    /// A later registration for the same <typeparamref name="T"/> replaces the earlier one.
    /// </summary>
    public static IServiceCollection AddInitialized<T, TInitializer>(this IServiceCollection services)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        var registration = new InitializedRegistration(Initializer: typeof(TInitializer), SlotType: typeof(Slot<T>));
        services.GetScopeRegistrations().Initialized[typeof(T)] = registration;
        services.AddTransient<TInitializer>();
        services.AddScoped<Slot<T>>();
        services.AddScoped(static sp => sp.GetRequiredService<Slot<T>>().Value);
        return services;
    }

    /// <summary>
    /// Declares what an opaque service, factory or initializer resolves at run time,
    /// until it is converted to constructor injection. The plan warning stays.
    /// </summary>
    public static IServiceCollection AddHiddenNeeds<TService, TNeed>(this IServiceCollection services)
    {
        var needs = services.GetScopeRegistrations().HiddenNeeds;
        if (!needs.TryGetValue(typeof(TService), out var list))
        {
            list = [];
            needs[typeof(TService)] = list;
        }
        list.Add(ScopeNode.Unkeyed(typeof(TNeed)));
        return services;
    }

    private static ScopeRegistrations GetScopeRegistrations(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(static d => d.ServiceType == typeof(ScopeRegistrations));
        if (descriptor?.ImplementationInstance is ScopeRegistrations existing)
        {
            return existing;
        }
        var registrations = new ScopeRegistrations();
        services.AddSingleton(registrations);
        services.AddSingleton(sp => new ScopePlanGraph(services, registrations));
        services.AddSingleton(typeof(ScopePlan<>));
        services.AddScoped(typeof(ScopeForkFactory<>));
        services.AddScoped<ScopeState>();
        services.AddScoped(static sp => sp.GetRequiredService<ScopeState>().Resources);
        return registrations;
    }
}

internal static class ScopeValueGuard
{
    public static void RejectDisposable(ScopeNode node, object value)
    {
        if (value is IDisposable or IAsyncDisposable)
        {
            throw new DisposableScopeValueException(node);
        }
    }
}
