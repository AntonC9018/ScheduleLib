using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Anton.LayeredConfig.Retrieval;
using MainCli.BuilderNew;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public static class RegistrationHelper
{
    public static void RegisterBasicOperationsAndMergers<T>(this IServiceCollection services)
        where T : class
    {
        RegisterBasicOperationsAndMergersForType(services, typeof(T));
    }

    public static void RegisterBasicOperationsAndMergersForType(
        IServiceCollection services,
        Type type)
    {
        if (!ProcessSelf_CheckShouldProcessChildren())
        {
            return;
        }
        ProcessChildren();
        return;

        bool ProcessSelf_CheckShouldProcessChildren()
        {
            if (type == typeof(string)
                || type == typeof(Type)
                || type.IsInterface)
            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
                var implType = typeof(ImmutableClassBasicOperations<>).MakeGenericType(type);
                services.TryAddSingleton(serviceType, implType);
                return false;
            }

            // TODO: also check base types
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = type.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elementType);
                Debug.Assert(listType == type);
                {
                    var serviceType = typeof(IMerger<>).MakeGenericType(type);
                    var implType = typeof(ListMerger<>).MakeGenericType(elementType);
                    services.TryAddScoped(serviceType, implType);
                }
                {
                    var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
                    var implType = typeof(ListBasicOperations<>).MakeGenericType(elementType);
                    services.TryAddSingleton(serviceType, implType);
                }
                RegisterBasicOperationsAndMergersForType(services, elementType);
                return false;
            }

            if (type.IsClass)
            {
                bool added = false;
                {
                    var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
                    var implType = typeof(ReflectionBasicOperations<>).MakeGenericType(type);
                    if (services.TryAddScoped(serviceType, implType))
                    {
                        added = true;
                    }
                }
                {
                    var serviceType = typeof(IMerger<>).MakeGenericType(type);
                    var implType = typeof(ReflectionMerger<>).MakeGenericType(type);
                    if (services.TryAddScoped(serviceType, implType))
                    {
                        added = true;
                    }
                }
                return added;
            }

            if (Nullable.GetUnderlyingType(type) is { } underlyingType)
            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
                var implType = typeof(NullableStructBasicOperations<>).MakeGenericType(underlyingType);
                services.TryAddSingleton(serviceType, implType);
                return false;
            }

            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
                var implType = typeof(ImmutableStructBasicOperations<>).MakeGenericType(type);
                services.TryAddSingleton(serviceType, implType);
            }
            return false;
        }

        void ProcessChildren()
        {
            var members = type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .AsEnumerable<MemberInfo>()
                .Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            foreach (var m in members)
            {
                Type mtype;
                if (m is PropertyInfo p)
                {
                    mtype = p.PropertyType;
                }
                else if (m is FieldInfo f)
                {
                    mtype = f.FieldType;
                }
                else
                {
                    throw Unreachable();
                }

                RegisterBasicOperationsAndMergersForType(services, mtype);
            }
        }
    }

    // why tf doesn't this return bool by default???
    public static bool TryAddSingleton(
        this IServiceCollection collection,
        Type service,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        ArgumentNullException.ThrowIfNull((object) collection, nameof (collection));
        ArgumentNullException.ThrowIfNull((object) service, nameof (service));
        ArgumentNullException.ThrowIfNull((object) implementationType, nameof (implementationType));
        ServiceDescriptor descriptor = ServiceDescriptor.Singleton(service, implementationType);
        return collection.TryAdd(descriptor);
    }
    public static bool TryAddScoped(
        this IServiceCollection collection,
        Type service,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        ArgumentNullException.ThrowIfNull((object) collection, nameof (collection));
        ArgumentNullException.ThrowIfNull((object) service, nameof (service));
        ArgumentNullException.ThrowIfNull((object) implementationType, nameof (implementationType));
        ServiceDescriptor descriptor = ServiceDescriptor.Scoped(service, implementationType);
        return collection.TryAdd(descriptor);
    }

    private static bool TryAdd(this IServiceCollection collection, ServiceDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull((object) collection, nameof (collection));
        ArgumentNullException.ThrowIfNull((object) descriptor, nameof (descriptor));
        int count = collection.Count;
        for (int index = 0; index < count; ++index)
        {
            if (collection[index].ServiceType == descriptor.ServiceType
                && object.Equals(collection[index].ServiceKey, descriptor.ServiceKey))
            {
                return false;
            }
        }
        collection.Add(descriptor);
        return true;
    }

    public static void AddMapper<T>(this IServiceCollection collection)
        where T : class, IConfigMapperBase
    {
        // collection.RegisterRequiredImplementationsOfGenericService<T>(
        //     typeof(IConfigMapperBase),
        //     typeof(IConfigMapper<,>),
        //     ServiceLifetime.Singleton);
        collection.AddSingleton<IConfigMapperBase, T>();
    }
    public static void AddMerger<T>(this IServiceCollection collection)
        where T : IMergerBase
    {
        collection.RegisterRequiredImplementationsOfGenericService<T>(
            typeof(IMergerBase),
            typeof(IMerger<>),
            ServiceLifetime.Singleton);
    }
    public static void AddBasicOperations<T>(this IServiceCollection collection)
        where T : IBasicOperationsBase
    {
        collection.RegisterRequiredImplementationsOfGenericService<T>(
            typeof(IBasicOperationsBase),
            typeof(IBasicOperations<>),
            ServiceLifetime.Singleton);
    }
}
