using System.Collections;
using System.Collections.Concurrent;
using MainCli.BuilderNew;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

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

public sealed class LayerConfigContainer
{
    internal IReadOnlyList<IUpdaterBase>? UpdateActions = null;
}

public readonly struct UpdateActionsList<T> : IEnumerable<IUpdater<T>>
    where T : class
{
    private readonly LayerConfigContainer _impl;

    public UpdateActionsList(LayerConfigContainer impl)
    {
        _impl = impl;
    }

    public readonly List<IUpdater<T>> List()
    {
        if (_impl.UpdateActions is not { } val)
        {
            val = new List<IUpdater<T>>();
            _impl.UpdateActions = val;
        }
        return (List<IUpdater<T>>) val;
    }
    public readonly List<IUpdater<T>>? MaybeList()
    {
        if (_impl.UpdateActions is { } val)
        {
            return (List<IUpdater<T>>) val;
        }
        return null;
    }

    public readonly bool IsEmpty => MaybeList() is not { } x || x.Count == 0;
    public readonly void Add(IUpdater<T> value) => List().Add(value);

    public IEnumerator<IUpdater<T>> GetEnumerator()
    {
        if (_impl.UpdateActions is null)
        {
            yield break;
        }
        foreach (var x in (List<IUpdater<T>>) _impl.UpdateActions)
        {
            yield return x;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

public readonly struct LayerConfigContainer<T>
    where T : class
{
    private readonly LayerConfigContainer _impl;

    public LayerConfigContainer(LayerConfigContainer impl)
    {
        _impl = impl;
    }

    private readonly MergeValueUpdater<T>? ValueHolder
    {
        get
        {
            foreach (var x in UpdateActions)
            {
                if (x is MergeValueUpdater<T> y)
                {
                    return y;
                }
            }
            return null;
        }
    }

    public readonly T? GetValue() => ValueHolder?.Value;
    public readonly void SetValue(T value)
    {
        if (ValueHolder is not { } holder)
        {
            holder = new MergeValueUpdater<T>(value);
            UpdateActions.Add(holder);
        }
        else
        {
            holder.Value = value;
        }
    }
    public readonly UpdateActionsList<T> UpdateActions => new(_impl);
}

public readonly struct MaybeLayerConfigContainer<T>
    where T : class
{
    private readonly LayerConfigContainer? _impl;

    public MaybeLayerConfigContainer(LayerConfigContainer? impl)
    {
        _impl = impl;
    }

    public bool Exists => _impl != default;
    public LayerConfigContainer<T> Value
    {
        get
        {
            if (_impl is { } value)
            {
                return new(value);
            }
            {
                throw new InvalidOperationException("Does not exist!");
            }
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
    private readonly ApplicationConfigLayerBuilder _layer;
    public LayerConfigKey<T> ConfigKey { get; }
    internal MutableLayer Layer => _layer.Layer;
    public IServiceProvider SingletonServiceProvider => _layer.SingletonServiceProvider;

    public ConfigBuilder(
        ApplicationConfigLayerBuilder layer,
        LayerConfigKey<T> configKey)
    {
        _layer = layer;
        ConfigKey = configKey;
    }
}

public static class BaseExtensions
{
    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<T> Builder<T>()
            where T : class, IConfig<T>
        {
            return builder.Builder(T.Key);
        }

        public ConfigBuilder<T> Builder<T>(LayerConfigKey<T> key)
            where T : class
        {
            return new(builder, key);
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        public T ConfigureValue(Action<T> configure)
        {
            var val = builder.Value();
            configure(val);
            return val;
        }
        public T Value()
        {
            var x = builder.Enable();
            if (x.GetValue() is not { } val)
            {
                val = builder.SingletonServiceProvider.GetRequiredService<IBasicOperations<T>>().Empty();
                if (val is null)
                {
                    throw new InvalidOperationException("Root BasicOperations is supposed to create actual objects, not null.");
                }
                x.SetValue(val);
            }
            return val;
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        public LayerConfigContainer<T> Enable()
        {
            return builder.Layer.GetOrAddConfig(builder.ConfigKey);
        }
        public void Configure(Action<ConfigBuilder<T>> configure)
        {
            configure(builder);
        }
    }
}
