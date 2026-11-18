using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>How Microsoft DI would build a service, as far as the plan can tell.</summary>
internal enum ServiceSource
{
    Missing,

    /// <summary>Singletons cannot depend on scoped values, so the plan stops there.</summary>
    Singleton,

    Type,
    Lambda,
}

internal readonly record struct ServiceRecipe(ServiceSource Source, Type? Implementation);

/// <summary>Registrations by service type and key, built once from the read-only service collection.</summary>
internal sealed class ServiceIndex
{
    private readonly Dictionary<ScopeNode, List<ServiceDescriptor>> _descriptors = [];

    public ServiceIndex(IServiceCollection services)
    {
        foreach (var descriptor in services)
        {
            var node = new ScopeNode(descriptor.ServiceType, descriptor.ServiceKey);
            if (!_descriptors.TryGetValue(node, out var list))
            {
                list = [];
                _descriptors[node] = list;
            }
            list.Add(descriptor);
        }
    }

    /// <summary>The registration Microsoft DI resolves for a single service: the last one.</summary>
    public ServiceRecipe Find(ScopeNode node)
    {
        var descriptors = FindAll(node);
        if (descriptors.Count == 0)
        {
            return new(ServiceSource.Missing, Implementation: null);
        }
        return RecipeOf(descriptors[^1], node.Type);
    }

    /// <summary>Every registration, in order, as <see cref="IEnumerable{T}"/> resolves them.</summary>
    public IEnumerable<ServiceRecipe> FindEach(ScopeNode node)
    {
        return FindAll(node).Select(d => RecipeOf(d, node.Type));
    }

    private List<ServiceDescriptor> FindAll(ScopeNode node)
    {
        if (_descriptors.TryGetValue(node, out var exact))
        {
            return exact;
        }
        if (!node.Type.IsConstructedGenericType)
        {
            return [];
        }
        var genericDefinition = node.Type.GetGenericTypeDefinition();
        var open = new ScopeNode(genericDefinition, node.Key);
        if (_descriptors.TryGetValue(open, out var openDescriptors))
        {
            return openDescriptors;
        }
        return [];
    }

    private static ServiceRecipe RecipeOf(ServiceDescriptor descriptor, Type serviceType)
    {
        if (descriptor.Lifetime == ServiceLifetime.Singleton)
        {
            return new(ServiceSource.Singleton, Implementation: null);
        }
        var implementation = descriptor.IsKeyedService
            ? descriptor.KeyedImplementationType
            : descriptor.ImplementationType;
        if (implementation is null)
        {
            return new(ServiceSource.Lambda, Implementation: null);
        }
        if (implementation.IsGenericTypeDefinition)
        {
            implementation = implementation.MakeGenericType(serviceType.GenericTypeArguments);
        }
        return new(ServiceSource.Type, implementation);
    }
}
