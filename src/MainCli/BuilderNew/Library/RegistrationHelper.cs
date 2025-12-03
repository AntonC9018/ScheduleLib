using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MainCli.BuilderNew;

public static class RegistrationHelper
{
    public static void RegisterBasicOperationsAndMergers<T>(this ServiceCollection services)
        where T : class, IConfig<T>
    {
        RegisterBasicOperationsAndMergersForType(services, typeof(T));
    }

    public static void RegisterBasicOperationsAndMergersForType(
        ServiceCollection services,
        Type type)
    {
        {
            var serviceType = typeof(IMerger<>).MakeGenericType(type);
            var implType = typeof(ReflectionMerger<>).MakeGenericType(type);
            services.AddSingleton(serviceType, implType);
        }
        {
            var serviceType = typeof(IBasicOperations<>).MakeGenericType(type);
            var implType = typeof(ReflectionBasicOperations<>).MakeGenericType(type);
            services.AddSingleton(serviceType, implType);
        }

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

            if (mtype == typeof(string)
                || mtype == typeof(Type)
                || mtype.IsInterface)
            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(mtype);
                var implType = typeof(ImmutableClassBasicOperations<>).MakeGenericType(mtype);
                services.TryAddSingleton(serviceType, implType);
            }
            // TODO: also check base types
            else if (mtype.IsGenericType && mtype.GetGenericTypeDefinition() == typeof(List<>))
            {
                var elementType = mtype.GetGenericArguments()[0];
                var listType = typeof(List<>).MakeGenericType(elementType);
                Debug.Assert(listType == mtype);
                if (!Add())
                {
                    RegisterBasicOperationsAndMergersForType(services, elementType);
                }

                bool Add()
                {
                    bool added = false;
                    {
                        var serviceType = typeof(IBasicOperations<>).MakeGenericType(listType);
                        var implType = typeof(ListBasicOperations<>).MakeGenericType(elementType);
                        if (services.TryAddSingleton(serviceType, implType))
                        {
                            added = true;
                        }
                    }
                    {
                        var serviceType = typeof(IMerger<>).MakeGenericType(listType);
                        var implType = typeof(ListMerger<>).MakeGenericType(elementType);
                        if (services.TryAddSingleton(serviceType, implType))
                        {
                            added = true;
                        }
                    }
                    return added;
                }
            }
            else if (mtype.IsClass)
            {
                if (!Add())
                {
                    RegisterBasicOperationsAndMergersForType(services, mtype);
                }

                bool Add()
                {
                    bool added = false;
                    {
                        var serviceType = typeof(IBasicOperations<>).MakeGenericType(mtype);
                        var implType = typeof(ReflectionBasicOperations<>).MakeGenericType(mtype);
                        services.TryAddSingleton(serviceType, implType);
                    }
                    {
                        var serviceType = typeof(IMerger<>).MakeGenericType(mtype);
                        var implType = typeof(ReflectionMerger<>).MakeGenericType(mtype);
                        services.TryAddSingleton(serviceType, implType);
                    }
                    return added;
                }
            }
            else if (Nullable.GetUnderlyingType(mtype) is { } underlyingType)
            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(mtype);
                var implType = typeof(NullableStructBasicOperations<>).MakeGenericType(underlyingType);
                services.TryAddSingleton(serviceType, implType);
            }
            else
            {
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(mtype);
                var implType = typeof(ImmutableStructBasicOperations<>).MakeGenericType(mtype);
                services.TryAddSingleton(serviceType, implType);
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

}
