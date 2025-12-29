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

public interface IUpdateActionBase
{
}

public interface IUpdater<T> : IUpdateActionBase
    where T : class
{
    public T? Update(IServiceProvider sp, T value);
}

public sealed class MergeValueUpdater<T> : IUpdater<T>
    where T : class
{
    public T Value { get; set; }

    public MergeValueUpdater(T initialValue)
    {
        Value = initialValue;
    }

    public T? Update(IServiceProvider sp, T value)
    {
        var merger = sp.GetRequiredService<IMerger<T>>();
        var ret = merger.Merge(from: Value, into: value);
        return ret;
    }
}

public sealed class LayerConfigContainer
{
    // Think about making the regular config value system into a subsystem of UpdateActions.
    // Think about using an interface instead of a Delegate here.
    public List<IUpdateActionBase>? UpdateActions = null;
}

public readonly struct UpdateActionsList<T> : IEnumerable<IUpdater<T>>
    where T : class
{
    private readonly LayerConfigContainer _impl;

    public UpdateActionsList(LayerConfigContainer impl)
    {
        _impl = impl;
    }

    private readonly List<IUpdateActionBase> List() => _impl.UpdateActions ??= new();
    public readonly void Add(IUpdater<T> value) => List().Add(value);

    public IEnumerator<IUpdater<T>> GetEnumerator()
    {
        if (_impl.UpdateActions is null)
        {
            yield break;
        }
        foreach (var x in _impl.UpdateActions)
        {
            yield return (IUpdater<T>) x;
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
    internal readonly MutableLayer _layer;
    public LayerConfigKey<T> ConfigKey { get; }

    public ConfigBuilder(
        MutableLayer layer,
        LayerConfigKey<T> configKey)
    {
        _layer = layer;
        ConfigKey = configKey;
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
        public ConfigBuilder<T> Builder<T>()
            where T : class, IConfig<T>
        {
            return builder.Builder(T.Key);
        }

        public ConfigBuilder<T> Builder<T>(LayerConfigKey<T> key)
            where T : class
        {
            return new(builder.Layer, key);
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        public LayerConfigContainer<T> Enable(Func<T> factory)
        {
            var container = builder._layer.GetOrAdd(builder.ConfigKey);
            container.SetValue(factory());
            return container;
        }
    }

    extension (MutableLayer layer)
    {
        public LayerConfigContainer<T> GetOrAdd<T>(LayerConfigKey<T> key)
            where T : class, new()
        {
            return layer.GetOrAdd(key);
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class, new()
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
            if (x.GetValue() is { } val)
            {
                return val;
            }
            val = new T();
            x.SetValue(val);
            return val;
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class
    {
        public LayerConfigContainer<T> Enable()
        {
            return builder._layer.GetOrAdd(builder.ConfigKey);
        }
        // TODO:
        // Add validation that would deal with using stuff along this thing.
        // They just won't run ever is the problem, so it's probably not desired.
        public void Remove()
        {
            builder.AddUpdate(RemoveValueUpdater<T>.Instance);
        }
        public void ConfigureLayer(Action<ConfigBuilder<T>> configure)
        {
            configure(builder);
        }
        public void AddUpdate(IUpdater<T> updater)
        {
            builder.Enable().UpdateActions.Add(updater);
        }
        public void AddUpdate(Func<IServiceProvider, T, T?> updateAction)
        {
            var update = new DelegateUpdater<T>(updateAction);
            builder.AddUpdate(update);
        }
        public void AddUpdate(Func<T, T?> updateAction)
        {
            builder.AddUpdate((sp, x) =>
            {
                _ = sp;
                return updateAction(x);
            });
        }
        public void AddUpdate(Action<T> updateAction)
        {
            builder.AddUpdate((sp, x) =>
            {
                _ = sp;
                updateAction(x);
                return x;
            });
        }
    }
}

public sealed class DelegateUpdater<T> : IUpdater<T>
    where T : class
{
    private readonly Func<IServiceProvider, T, T?> _action;

    public DelegateUpdater(Func<IServiceProvider, T, T?> action)
    {
        _action = action;
    }

    public T? Update(IServiceProvider sp, T value) => _action(sp, value);
}

public sealed class RemoveValueUpdater<T> : IUpdater<T>
    where T : class
{
    public static readonly RemoveValueUpdater<T> Instance = new();
    public T? Update(IServiceProvider sp, T value) => null;
}

public sealed class ResetValueUpdater<T> : IUpdater<T>
    where T : class
{
    public static readonly ResetValueUpdater<T> Instance = new();
    public T? Update(IServiceProvider sp, T value)
    {
        var basicOps = sp.GetRequiredService<IBasicOperations<T>>();
        var ret = basicOps.Reset(value);
        return ret;
    }
}
