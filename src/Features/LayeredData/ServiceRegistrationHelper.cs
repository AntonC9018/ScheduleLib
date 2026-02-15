using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData;

internal static class ServiceRegistrationHelper
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
                firstImpl,
                implementationType,
                lifetime);
            services.Add(descriptor);
            return true;
        }

        // Multiple implementations in one class.
        {
            var descriptor = new ServiceDescriptor(
                implementationType,
                implementationType,
                lifetime);
            services.Add(descriptor);
        }

        void AddImpl(Type implementedInterface)
        {
            var descriptor = new ServiceDescriptor(
                implementedInterface,
                static sp => sp.GetRequiredService<TImplementation>(),
                lifetime);
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
            unboundServiceType, lifetime);
        if (!registered)
        {
            throw new InvalidOperationException(
                $"{markerServiceType.Name} should only be inherited via {unboundServiceType.Name}");
        }
        return services;
    }
}
