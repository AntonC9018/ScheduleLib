using System.Reflection;
using Anton.LayeredData.TreeEnumeration;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData.Retrieval;

// Must be a scoped service.
// TODO: cache
[AutoConstructor]
public sealed partial class DataProvider
{
    private readonly IMarkerDataHelper _helper;
    private readonly IServiceProvider _sp;
    private readonly DataMappingRegistry _mappingRegistry;

    public object? GetUntyped(NodeDataKey key)
    {
        var type = NodeDataKey.Registry.GetTypeFromKey(key);
        return GetUntypedInternal(type, key);
    }
    private object? GetUntypedInternal(Type outputType, NodeDataKey key)
    {
        // TODO: Cache globally maybe
        var method = GetConfigMethod.MakeGenericMethod(outputType);
        var func = method.CreateDelegate<GetUntypedDelegate>(this);

        var ret = func(key);
        return ret;
    }
    private delegate object? GetUntypedDelegate(NodeDataKey key);

    private static readonly MethodInfo GetConfigMethod =
        typeof(DataProvider).GetMethod(nameof(GetWrapper), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private object? GetWrapper<T>(NodeDataKey key)
        where T : class
    {
        return Get<T>(new(key));
    }

    public T? Get<T>(NodeDataKey<T> key)
        where T : class
    {
        var markerConfig = _helper.GetMarkerData(_sp);
        if (markerConfig.Value is T m)
        {
            return m;
        }

        var mapping = _mappingRegistry.Get(outputType: typeof(T));
        if (!mapping.IsNull)
        {
            var unmappedConfig = GetUntypedInternal(
                outputType: mapping.From,
                key: key.Value);
            if (unmappedConfig is null)
            {
                return null;
            }
            var mappedConfig = mapping.Mapper.Map(
                from: unmappedConfig);
            return (T) mappedConfig;
        }

        if (_helper.GetCurrentPath(markerConfig) is not { } path)
        {
            throw new InvalidOperationException("No matching path found for the current configuration.");
        }

        {
            var type = NodeDataKey.Registry.GetTypeFromKey(key.Value);
            if (type != typeof(T))
            {
                throw new InvalidOperationException($"Config mapper not registered for type `{typeof(T).Name}`");
            }
        }

        var config = path.ConstructValue(key, _sp);
        return config;
    }
}

[AutoConstructor]
public sealed partial class DataProvider<T> where T : class
{
    private readonly DataProvider _provider;
    private readonly NodeDataKey<T> _key;

    public T? Get() => _provider.Get(_key);
}

public static class DataProviderHelper
{
    extension (IServiceCollection services)
    {
        private static Func<IServiceProvider, DataProvider<T>> Factory<T>(NodeDataKey<T> key)
            where T : class
        {
            return sp => new(sp.GetRequiredService<DataProvider>(), key);
        }

        public void AddConfigProvider<T>(NodeDataKey<T> key) where T : class
        {
            var f = Factory(key);
            services.AddScoped(f);
        }
    }
}
