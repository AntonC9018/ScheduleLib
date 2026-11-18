using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <param name="Descriptors">The service registrations this value added, removed when it is replaced.</param>
internal sealed record DerivedRegistration(Type Factory, ImmutableArray<ServiceDescriptor> Descriptors);

/// <param name="Descriptors">The service registrations this value added, removed when it is replaced.</param>
internal sealed record InitializedRegistration(
    Type Initializer,
    Type SlotType,
    ImmutableArray<ServiceDescriptor> Descriptors);

/// <summary>Scope-plan metadata kept next to the service collection, so plans can see through factories.</summary>
internal sealed class ScopeRegistrations
{
    /// <summary>Seed node to its slot type.</summary>
    public Dictionary<ScopeNode, Type> Seeds { get; } = [];

    public Dictionary<Type, DerivedRegistration> Derived { get; } = [];

    public Dictionary<Type, InitializedRegistration> Initialized { get; } = [];

    /// <summary>Service, factory or initializer implementation type to what it resolves at run time.</summary>
    public Dictionary<Type, List<ScopeNode>> HiddenNeeds { get; } = [];

    /// <summary>
    /// Forgets the derived or initialized registration of <paramref name="value"/>, whichever kind
    /// it was, and returns the service registrations it added.
    /// </summary>
    public ImmutableArray<ServiceDescriptor> Unregister(Type value)
    {
        if (Derived.Remove(value, out var derived))
        {
            return derived.Descriptors;
        }
        if (Initialized.Remove(value, out var initialized))
        {
            return initialized.Descriptors;
        }
        return [];
    }

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

    /// <summary>
    /// A value built synchronously in each scope by <typeparamref name="TFactory"/>.
    /// A later derived or initialized registration for the same <typeparamref name="T"/>
    /// replaces the earlier one.
    /// </summary>
    public static IServiceCollection AddDerived<T, TFactory>(this IServiceCollection services)
        where T : class
        where TFactory : class, IFactory<T>
    {
        var registrations = services.GetScopeRegistrations();
        services.RemoveDescriptors(registrations.Unregister(typeof(T)));
        ImmutableArray<ServiceDescriptor> descriptors =
        [
            ServiceDescriptor.Transient<TFactory, TFactory>(),
            ServiceDescriptor.Scoped(static sp =>
            {
                var value = sp.GetRequiredService<TFactory>().Create();
                ScopeValueGuard.RejectDisposable(ScopeNode.Unkeyed(typeof(T)), value);
                return value;
            }),
        ];
        services.AddDescriptors(descriptors);
        registrations.Derived[typeof(T)] = new(Factory: typeof(TFactory), Descriptors: descriptors);
        return services;
    }

    /// <summary>
    /// A value built asynchronously by <typeparamref name="TInitializer"/> when the scope opens.
    /// A later derived or initialized registration for the same <typeparamref name="T"/>
    /// replaces the earlier one.
    /// </summary>
    public static IServiceCollection AddInitialized<T, TInitializer>(this IServiceCollection services)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        var registrations = services.GetScopeRegistrations();
        services.RemoveDescriptors(registrations.Unregister(typeof(T)));
        ImmutableArray<ServiceDescriptor> descriptors =
        [
            ServiceDescriptor.Transient<TInitializer, TInitializer>(),
            ServiceDescriptor.Scoped<Slot<T>, Slot<T>>(),
            ServiceDescriptor.Scoped(static sp => sp.GetRequiredService<Slot<T>>().Value),
        ];
        services.AddDescriptors(descriptors);
        registrations.Initialized[typeof(T)] = new(
            Initializer: typeof(TInitializer),
            SlotType: typeof(Slot<T>),
            Descriptors: descriptors);
        return services;
    }

    private static void AddDescriptors(this IServiceCollection services, ImmutableArray<ServiceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            services.Add(descriptor);
        }
    }

    /// <summary>
    /// Removes exactly the registrations a replaced value added, so container validation no longer
    /// requires its dependencies and an unrelated registration of the same type stays.
    /// </summary>
    private static void RemoveDescriptors(this IServiceCollection services, ImmutableArray<ServiceDescriptor> descriptors)
    {
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
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
