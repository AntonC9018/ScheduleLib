using System.Collections.Concurrent;

namespace Anton.LayeredData;

public sealed class NodeDataKeyRegistry
{
    private readonly NameRegistry<NodeDataKey> _impl = new();
    private readonly ConcurrentDictionary<NodeDataKey, Type> _typeMap = new();

    public IEnumerable<KeyValuePair<NodeDataKey, Type>> KeyTypeMappings => _typeMap;

    public Type GetTypeFromKey(NodeDataKey key)
    {
        return _typeMap[key];
    }
    public Type? TryGetTypeFromKey(NodeDataKey key)
    {
        return _typeMap.GetValueOrDefault(key);
    }
    public NodeDataKey<T> Register<T>() where T : class
    {
        var ret = Register<T>(typeof(T).Name);
        return ret;
    }
    public NodeDataKey<T> Register<T>(string name) where T : class
    {
        var ret = _impl.Register(name);
        _typeMap.TryAdd(ret, typeof(T));
        return new(ret);
    }
}

public readonly record struct NodeDataKey<T>(NodeDataKey Value) where T : class
{
    public static bool operator==(NodeDataKey<T> self, NodeDataKey other) => self.Value == other;
    public static bool operator!=(NodeDataKey<T> self, NodeDataKey other) => !(self == other);
    public static bool operator==(NodeDataKey self, NodeDataKey<T> other) => other == self;
    public static bool operator!=(NodeDataKey self, NodeDataKey<T> other) => other != self;
}
public readonly record struct NodeDataKey(string Value) : ICreateFromString<NodeDataKey>
{
    public static readonly NodeDataKeyRegistry Registry = new();
    public static NodeDataKey Create(string val) => new(val);
}

