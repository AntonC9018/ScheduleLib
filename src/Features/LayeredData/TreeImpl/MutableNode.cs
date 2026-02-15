using System.Collections.Concurrent;

namespace Anton.LayeredData;

public sealed class MutableNode
{
    internal readonly ConcurrentDictionary<NodeDataKey, NodeDataContainer> _configs = new();
    internal readonly List<MutableNode> _childNodes = new();

    public Layer Layer { get; set; } = Layer.Unnamed;

    public IReadOnlyList<MutableNode> ChildNodes => _childNodes;

    public ICollection<NodeDataKey> ConfigKeys => _configs.Keys;

    public IEnumerable<NodeDataAccessorHelper> Configs => _configs.Select(x => new NodeDataAccessorHelper(x.Key, x.Value));

    public NodeDataContainer? GetUntyped(NodeDataKey key)
    {
        var container = _configs.GetValueOrDefault(key, null!);
        return container;
    }

    public MaybeNodeDataContainer<T> Get<T>(NodeDataKey<T> key) where T : class
    {
        var container = _configs.GetValueOrDefault(key.Value, null!);
        return new(container);
    }

    public NodeDataContainer<T> GetOrAdd<T>(NodeDataKey<T> key) where T : class
    {
        var container = _configs.GetOrAdd(key.Value, _ => new());
        return new(container);
    }
}
