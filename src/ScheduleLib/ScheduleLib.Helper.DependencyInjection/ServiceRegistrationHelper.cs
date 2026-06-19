using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection;

public static class ServiceRegistrationHelper
{
    /// <summary>
    /// Registers the given implementation type under all implementations of a generic service,
    /// implemented by the given implementation type.
    /// </summary>
    /// <returns>True if at least one implementation has been registered</returns>
    public static bool RegisterImplementationsOfGenericService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImplementation>(

        this IServiceCollection services,
        Type unboundServiceType,
        ServiceLifetime lifetime)

        where TImplementation : notnull
    {
        Type implementationType = typeof(TImplementation);
        using var implementedServices = implementationType
            .GetImplementationsOfGenericType(unboundServiceType)
            .GetEnumerator();

        if (!implementedServices.MoveNext())
        {
            return false;
        }

        var firstImpl = implementedServices.Current;

        // Register only a single implementation.
        if (!implementedServices.MoveNext())
        {
            var descriptor = new ServiceDescriptor(
                serviceType: firstImpl,
                implementationType: implementationType,
                lifetime: lifetime);
            services.Add(descriptor);
            return true;
        }

        // Multiple implementations in one class.
        {
            var descriptor = new ServiceDescriptor(
                serviceType: implementationType,
                implementationType: implementationType,
                lifetime: lifetime);
            services.Add(descriptor);
        }

        void AddImpl(Type implementedInterface)
        {
            var descriptor = new ServiceDescriptor(
                serviceType: implementedInterface,
                factory: static sp => sp.GetRequiredService<TImplementation>(),
                lifetime: lifetime);
            services.Add(descriptor);
        }

        AddImpl(firstImpl);
        do
        {
            AddImpl(implementedServices.Current);
        } while (implementedServices.MoveNext());

        return true;
    }

    public static IServiceCollection RegisterRequiredImplementationsOfGenericService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(

        this IServiceCollection services,
        Type markerServiceType,
        Type unboundServiceType,
        ServiceLifetime lifetime)

        where T : notnull
    {
        bool registered = services.RegisterImplementationsOfGenericService<T>(
            unboundServiceType: unboundServiceType,
            lifetime: lifetime);
        if (!registered)
        {
            throw new InvalidOperationException(
                $"{markerServiceType.Name} should only be inherited via {unboundServiceType.Name}");
        }
        return services;
    }

    public static ImplContext<TImpl> Register<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImpl>(

        this IServiceCollection services,
        ServiceLifetime lifetime)

        where TImpl : class
    {
        services.Add(new(typeof(TImpl), typeof(TImpl), lifetime));
        var builder = new ImplContext<TImpl>(services, lifetime);
        return builder;
    }

    public static ImplContext<TImpl> AsService<TImpl, TService>(
        this ImplContext<TImpl> builder,
        ServiceType<TService> service = default)

        where TImpl : class, TService
    {
        _ = service;
        builder._services.Add(new(
            typeof(TService),
            static sp => sp.GetRequiredService<TImpl>(),
            builder._lifetime));
        return builder;
    }
}

public readonly record struct ServiceType<T>();
public static class ServiceType
{
    public static ServiceType<T> Create<T>() => new();
}

public readonly struct ImplContext<TImpl>
    where TImpl : class
{
    internal readonly IServiceCollection _services;
    internal readonly ServiceLifetime _lifetime;

    internal ImplContext(IServiceCollection services, ServiceLifetime lifetime)
    {
        _services = services;
        _lifetime = lifetime;
    }
}
