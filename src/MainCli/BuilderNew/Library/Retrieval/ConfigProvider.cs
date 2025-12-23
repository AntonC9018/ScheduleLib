using System.Diagnostics;
using System.Reflection;
using AutoConstructor.Attributes;

namespace MainCli.BuilderNew.Retrieval;

// Must be a scoped service.
// TODO: cache
[AutoConstructor]
public sealed partial class ConfigProvider
{
    private readonly IMarkerConfigHelper _helper;
    private readonly IServiceProvider _sp;

    public object GetUntyped(LayerConfigKey key)
    {
        var type = LayerConfigKey.Registry.GetTypeFromKey(key);

        // TODO: Cache globally maybe
        var method = GetConfigMethod.MakeGenericMethod(type);
        var func = method.CreateDelegate<Func<LayerConfigKey, object>>(this);

        var ret = func(key);
        return ret;
    }

    private static readonly MethodInfo GetConfigMethod =
        typeof(ConfigProvider).GetMethod(nameof(GetConfigWrapper), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private object GetConfigWrapper<T>(LayerConfigKey key)
        where T : class
    {
        return Get<T>(new(key));
    }

    public T Get<T>(LayerConfigKey<T> key)
        where T : class
    {
        var markerConfig = _helper.GetMarkerConfig(_sp);
        if (markerConfig.Value is T m)
        {
            return m;
        }
        if (_helper.GetCurrentPath(markerConfig) is not { } path)
        {
            throw new InvalidOperationException("No matching path found for the current configuration.");
        }

        var config = path.ConstructConfig(key, _sp);
        Debug.Assert(config != null);
        return config;
    }
}

[AutoConstructor]
public sealed partial class ConfigProvider<T> where T : class
{
    private readonly ConfigProvider _provider;
    private readonly LayerConfigKey<T> _key;

    public T Get() => _provider.Get(_key);
}
