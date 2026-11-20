using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public static class ScopePlanServiceCollectionExtensions
{
    /// <summary>
    /// Registers the scope-plan infrastructure. Call it once from the composition root; repeated
    /// calls are harmless, and it may come before or after the scope values.
    /// </summary>
    public static IServiceCollection AddScopePlans(this IServiceCollection services)
    {
        // The graph reads the collection when the container first builds it; it keeps only its index.
        services.TryAddSingleton(sp => new ScopePlanGraph(
            services,
            sp.GetServices<ScopeValueRegistration>(),
            sp.GetServices<HiddenNeed>()));
        services.TryAddSingleton(typeof(ScopePlan<>));
        services.TryAddScoped(typeof(ScopeForkFactory<>));
        services.TryAddScoped<ScopeState>();
        services.TryAddScoped(static sp => sp.GetRequiredService<ScopeState>().Resources);
        return services;
    }

    /// <summary>
    /// A raw input supplied when a scope opens. The caller owns it; the scope never disposes it.
    /// Throws when <typeparamref name="T"/> is already a scope value.
    /// </summary>
    public static IServiceCollection AddSeed<T>(this IServiceCollection services)
        where T : class
    {
        ImmutableArray<ServiceDescriptor> descriptors =
        [
            ServiceDescriptor.Scoped<Slot<T>, Slot<T>>(),
            ServiceDescriptor.Scoped(static sp => sp.GetRequiredService<Slot<T>>().Value),
        ];
        return services.AddSeed<T>(ScopeNode.Unkeyed(typeof(T)), descriptors);
    }

    /// <summary>
    /// A keyed seed. Consumers take it through <see cref="FromKeyedServicesAttribute"/>
    /// and stay unkeyed themselves. Throws when a seed with the same key is already registered.
    /// </summary>
    public static IServiceCollection AddKeyedSeed<T>(this IServiceCollection services, object key)
        where T : class
    {
        ImmutableArray<ServiceDescriptor> descriptors =
        [
            ServiceDescriptor.KeyedScoped<Slot<T>, Slot<T>>(key),
            ServiceDescriptor.KeyedScoped(key, static (sp, k) => sp.GetRequiredKeyedService<Slot<T>>(k).Value),
        ];
        return services.AddSeed<T>(new ScopeNode(typeof(T), key), descriptors);
    }

    private static IServiceCollection AddSeed<T>(
        this IServiceCollection services,
        ScopeNode node,
        ImmutableArray<ServiceDescriptor> descriptors)
        where T : class
    {
        var registration = new ScopeValueRegistration(
            Node: node,
            Kind: ScopeValueKind.Seed,
            FactoryOrInitializer: null,
            SlotType: typeof(Slot<T>),
            OwnedDescriptors: descriptors);
        return services.AddScopeValue(registration);
    }

    /// <summary>
    /// A value built synchronously in each scope by <typeparamref name="TFactory"/>.
    /// Throws when <typeparamref name="T"/> is already a scope value.
    /// </summary>
    public static IServiceCollection AddDerived<T, TFactory>(this IServiceCollection services)
        where T : class
        where TFactory : class, IFactory<T>
    {
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
        var registration = new ScopeValueRegistration(
            Node: ScopeNode.Unkeyed(typeof(T)),
            Kind: ScopeValueKind.Derived,
            FactoryOrInitializer: typeof(TFactory),
            SlotType: null,
            OwnedDescriptors: descriptors);
        return services.AddScopeValue(registration);
    }

    /// <summary>
    /// A value built asynchronously by <typeparamref name="TInitializer"/> when the scope opens.
    /// Throws when <typeparamref name="T"/> is already a scope value.
    /// </summary>
    public static IServiceCollection AddInitialized<T, TInitializer>(this IServiceCollection services)
        where T : class
        where TInitializer : class, IInitializer<T>
    {
        ImmutableArray<ServiceDescriptor> descriptors =
        [
            ServiceDescriptor.Transient<TInitializer, TInitializer>(),
            ServiceDescriptor.Scoped<Slot<T>, Slot<T>>(),
            ServiceDescriptor.Scoped(static sp => sp.GetRequiredService<Slot<T>>().Value),
        ];
        var registration = new ScopeValueRegistration(
            Node: ScopeNode.Unkeyed(typeof(T)),
            Kind: ScopeValueKind.Initialized,
            FactoryOrInitializer: typeof(TInitializer),
            SlotType: typeof(Slot<T>),
            OwnedDescriptors: descriptors);
        return services.AddScopeValue(registration);
    }

    private static IServiceCollection AddScopeValue(this IServiceCollection services, ScopeValueRegistration registration)
    {
        if (services.FindScopeValue(registration.Node) is not null)
        {
            throw new DuplicateScopeValueException(registration.Node);
        }
        foreach (var descriptor in registration.OwnedDescriptors)
        {
            services.Add(descriptor);
        }
        services.AddSingleton(registration);
        return services;
    }

    /// <summary>
    /// Removes the seed, derived or initialized value <typeparamref name="T"/>, so it can be added
    /// again, possibly as another kind. Throws when it is not registered.
    /// </summary>
    public static IServiceCollection RemoveScopeValue<T>(this IServiceCollection services)
        where T : class
    {
        return services.RemoveScopeValue(ScopeNode.Unkeyed(typeof(T)));
    }

    /// <summary>Removes the keyed seed <typeparamref name="T"/>. Throws when it is not registered.</summary>
    public static IServiceCollection RemoveScopeValue<T>(this IServiceCollection services, object key)
        where T : class
    {
        return services.RemoveScopeValue(new ScopeNode(typeof(T), key));
    }

    /// <summary>
    /// Removes the value's record and exactly the registrations it added, so container validation
    /// no longer requires its dependencies and an unrelated registration of the same type stays.
    /// </summary>
    private static IServiceCollection RemoveScopeValue(this IServiceCollection services, ScopeNode node)
    {
        var descriptor = services.FindScopeValue(node) ?? throw new ScopeValueNotRegisteredException(node);
        services.Remove(descriptor);
        var registration = (ScopeValueRegistration) descriptor.ImplementationInstance!;
        foreach (var owned in registration.OwnedDescriptors)
        {
            services.Remove(owned);
        }
        return services;
    }

    private static ServiceDescriptor? FindScopeValue(this IServiceCollection services, ScopeNode node)
    {
        foreach (var descriptor in services)
        {
            if (descriptor.ImplementationInstance is ScopeValueRegistration registration && registration.Node == node)
            {
                return descriptor;
            }
        }
        return null;
    }

    /// <summary>
    /// Declares what an opaque service, factory or initializer resolves at run time,
    /// until it is converted to constructor injection. The plan warning stays.
    /// <typeparamref name="TService"/> is the implementation type, or the service type of a
    /// lambda registration.
    /// </summary>
    public static IServiceCollection AddHiddenNeeds<TService, TNeed>(this IServiceCollection services)
    {
        var need = new HiddenNeed(Service: typeof(TService), Need: ScopeNode.Unkeyed(typeof(TNeed)));
        services.AddSingleton(need);
        return services;
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
