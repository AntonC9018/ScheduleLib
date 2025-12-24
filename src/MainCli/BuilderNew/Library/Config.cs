using System.Collections.Concurrent;

namespace MainCli.BuilderNew;

public sealed class ConfigKeyRegistry
{
    private readonly NameRegistry<LayerConfigKey> _impl = new();
    private readonly ConcurrentDictionary<LayerConfigKey, Type> _typeMap = new();

    public Type GetTypeFromKey(LayerConfigKey key)
    {
        return _typeMap[key];
    }
    public LayerConfigKey<T> Register<T>() where T : class
    {
        var ret = Register<T>(typeof(T).Name);
        return ret;
    }
    public LayerConfigKey<T> Register<T>(string name) where T : class
    {
        var ret = _impl.Register(name);
        _typeMap.TryAdd(ret, typeof(T));
        return new(ret);
    }
}

public readonly record struct LayerConfigKey<T>(LayerConfigKey Value) where T : class;
public readonly record struct LayerConfigKey(string Value) : ICreateFromString<LayerConfigKey>
{
    public static readonly ConfigKeyRegistry Registry = new();
    public static LayerConfigKey Create(string val) => new(val);
}

public struct LayerConfigFlags
{
    public bool Remove;
    public bool Clean;
}

public sealed class LayerConfigContainer
{
    public object? Value;
    public LayerConfigFlags Flags = new();
}

public readonly struct LayerConfigContainer<T>
{
    private readonly LayerConfigContainer _impl;

    public LayerConfigContainer(LayerConfigContainer impl)
    {
        _impl = impl;
    }

    public readonly T Value
    {
        get => (T) _impl.Value!;
        set => _impl.Value = value;
    }
    public readonly ref LayerConfigFlags Flags => ref _impl.Flags;
}

public readonly struct MaybeLayerConfigContainer<T>
{
    private readonly LayerConfigContainer? _impl;

    public MaybeLayerConfigContainer(LayerConfigContainer? impl)
    {
        _impl = impl;
    }

    public bool Exists => _impl != null;
    public LayerConfigContainer<T> Value
    {
        get
        {
            if (!Exists)
            {
                throw new InvalidOperationException("Does not exist!");
            }
            return new(_impl!);
        }
    }
}

// TODO: Source generate the key property,
// source generate the BasicOperations class,
// source generate the merger class.
public interface IConfigBase
{
}

public interface IConfig<T> : IConfigBase
    where T : class
{
    public static abstract LayerConfigKey<T> Key { get; }
}

public readonly struct ConfigBuilder<T>
    where T : class
{
    internal readonly MutableLayer _layer;
    public LayerConfigKey<T> ConfigKey { get; }

    public ConfigBuilder(
        MutableLayer layer,
        LayerConfigKey<T> configKey)
    {
        _layer = layer;
        ConfigKey = configKey;
    }

    public T GetConfig()
    {
        var val = _layer.Get(ConfigKey);
        return val.Value.Value;
    }
}

public static class ConfigBuilder
{
    // public static ConfigBuilder<T> Create<T>(MutableLayer layer)
    //     where T : class, IConfig<T>
    // {
    //     return new(layer, T.Key);
    // }

    public static ConfigBuilder<T> Create<T>(MutableLayer layer, LayerConfigKey<T> key)
        where T : class
    {
        return new(layer, key);
    }
}

public static class BaseExtensions
{
    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<T> CreateConfigBuilder<T>()
            where T : class, IConfig<T>
        {
            return new(builder.Layer, T.Key);
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        public LayerConfigContainer<T> Enable(Func<T> factory)
        {
            return builder._layer.GetOrAdd(builder.ConfigKey, factory);
        }
    }

    extension (MutableLayer layer)
    {
        public LayerConfigContainer<T> GetOrAdd<T>(LayerConfigKey<T> key)
            where T : class, new()
        {
            return layer.GetOrAdd(key, () => new T());
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class, new()
    {
        public LayerConfigContainer<T> Enable()
        {
            return builder._layer.GetOrAdd(builder.ConfigKey);
        }

        public void Remove()
        {
            builder.Enable().Flags.Remove = true;
        }
        public void NoInherit()
        {
            builder.Enable().Flags.Clean = true;
        }
        public void Configure(Action<T> configure)
        {
            configure(builder.Enable().Value);
        }
        public void ConfigureLayer(Action<ConfigBuilder<T>> configure)
        {
            configure(builder);
        }
    }

}
