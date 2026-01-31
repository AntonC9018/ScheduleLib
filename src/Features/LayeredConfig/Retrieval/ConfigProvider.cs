using System.Reflection;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig.Retrieval;

// Must be a scoped service.
// TODO: cache
[AutoConstructor]
public sealed partial class ConfigProvider
{
    private readonly IMarkerConfigHelper _helper;
    private readonly IServiceProvider _sp;
    private readonly ConfigMappingRegistry _mappingRegistry;

    public object? GetUntyped(LayerConfigKey key)
    {
        var type = LayerConfigKey.Registry.GetTypeFromKey(key);
        return GetUntypedInternal(type, key);
    }
    private object? GetUntypedInternal(Type outputType, LayerConfigKey key)
    {
        // TODO: Cache globally maybe
        var method = GetConfigMethod.MakeGenericMethod(outputType);
        var func = method.CreateDelegate<GetUntypedDelegate>(this);

        var ret = func(key);
        return ret;
    }
    private delegate object? GetUntypedDelegate(LayerConfigKey key);

    private static readonly MethodInfo GetConfigMethod =
        typeof(ConfigProvider).GetMethod(nameof(GetConfigWrapper), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private object? GetConfigWrapper<T>(LayerConfigKey key)
        where T : class
    {
        return Get<T>(new(key));
    }

    public T? Get<T>(LayerConfigKey<T> key)
        where T : class
    {
        var markerConfig = _helper.GetMarkerConfig(_sp);
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
            var type = LayerConfigKey.Registry.GetTypeFromKey(key.Value);
            if (type != typeof(T))
            {
                throw new InvalidOperationException($"Config mapper not registered for type `{typeof(T).Name}`");
            }
        }

        var config = path.ConstructConfig(key, _sp);
        return config;
    }
}

[AutoConstructor]
public sealed partial class ConfigProvider<T> where T : class
{
    private readonly ConfigProvider _provider;
    private readonly LayerConfigKey<T> _key;

    public T? Get() => _provider.Get(_key);
}

public static class ConfigProviderHelper
{
    extension (IServiceCollection services)
    {
        private static Func<IServiceProvider, ConfigProvider<T>> Factory<T>(LayerConfigKey<T> key)
            where T : class
        {
            return sp => new(sp.GetRequiredService<ConfigProvider>(), key);
        }

        public void AddConfigProvider<T>(LayerConfigKey<T> key) where T : class
        {
            var f = Factory(key);
            services.AddScoped(f);
        }
    }
}
