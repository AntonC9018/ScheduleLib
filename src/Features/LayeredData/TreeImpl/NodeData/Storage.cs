using System.Collections;

namespace Anton.LayeredData;

public sealed class NodeDataContainer
{
    internal IReadOnlyList<IUpdaterBase>? UpdateActions = null;
}

public readonly struct UpdateActionsList<T> : IEnumerable<IUpdater<T>>
    where T : class
{
    private readonly NodeDataContainer _impl;

    public UpdateActionsList(NodeDataContainer impl)
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

public readonly struct UntypedNodeDataContainer
{
    internal readonly NodeDataContainer Container { get; }

    public UntypedNodeDataContainer(NodeDataContainer container)
    {
        Container = container;
    }

    internal readonly IMergeValueUpdaterBase? ValueHolder
    {
        get
        {
            if (Container.UpdateActions is null)
            {
                return null;
            }
            foreach (var x in Container.UpdateActions)
            {
                if (x is IMergeValueUpdaterBase y)
                {
                    return y;
                }
            }
            return null;
        }
    }

    public readonly object? GetValue() => ValueHolder?.Value;
}

public readonly struct NodeDataContainer<T>
    where T : class
{
    private readonly UntypedNodeDataContainer _impl;

    public NodeDataContainer(NodeDataContainer impl)
    {
        _impl = new(impl);
    }

    private readonly MergeValueUpdater<T>? ValueHolder => (MergeValueUpdater<T>?) _impl.ValueHolder;

    public readonly T? GetValue() => (T?) _impl.GetValue();
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
    public readonly UpdateActionsList<T> UpdateActions => new(_impl.Container);
}

public readonly struct MaybeNodeDataContainer<T>
    where T : class
{
    private readonly NodeDataContainer? _impl;

    public MaybeNodeDataContainer(NodeDataContainer? impl)
    {
        _impl = impl;
    }

    public bool Exists => _impl != default;
    public NodeDataContainer<T> Value
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

public readonly record struct NodeDataAccessorHelper
{
    public NodeDataKey Key { get; }
    private readonly NodeDataContainer _container;

    internal NodeDataAccessorHelper(
        NodeDataKey key,
        NodeDataContainer container)
    {
        Key = key;
        _container = container;
    }

    public MaybeNodeDataContainer<T> As<T>(NodeDataKey<T> key)
        where T : class
    {
        if (key != Key)
        {
            return new(null);
        }
        return new(_container);
    }

    public UntypedNodeDataContainer Container => new(_container);
}
