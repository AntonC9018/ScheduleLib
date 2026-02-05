using System.Diagnostics;

namespace Anton.LayeredData;

public sealed class TreeBuilder
{
    private readonly MutableNode _baseNode = new();
    private readonly IServiceProvider _singletonServiceProvider;

    public TreeBuilder(IServiceProvider singletonServiceProvider)
    {
        _singletonServiceProvider = singletonServiceProvider;
    }

    public NodeBuilder Defaults
    {
        get
        {
            return new(_baseNode, _singletonServiceProvider);
        }
    }

    public NodeBuilder AddNode(Layer layer)
    {
        return Defaults.AddLayer(layer);
    }

    public MutableNode BaseNode => _baseNode;

    // Might want to remove this.
    public NodeBuilder CreateBuilder(MutableNode node)
    {
        return new(node, _singletonServiceProvider);
    }
}

public readonly struct NodeBuilder : IEquatable<NodeBuilder>
{
    public MutableNode Node { get; }
    public IServiceProvider SingletonServiceProvider { get; }

    public bool IsNull => Node is null;

    public bool Equals(NodeBuilder other)
    {
        return other.Node == Node;
    }

    public NodeBuilder(
        MutableNode node,
        IServiceProvider singletonServiceProvider)
    {
        Node = node;
        SingletonServiceProvider = singletonServiceProvider;
    }

    public NodeBuilder AddLayer(Layer layer)
    {
        var node = new MutableNode();
        node.Layer = layer;
        Node._childNodes.Add(node);
        return CreateNodeBuilder(node);
    }

    public NodeBuilder CreateNodeBuilder(MutableNode node)
    {
        if (!Node._childNodes.Contains(node))
        {
            throw new InvalidOperationException("This node is not a child");
        }
        return new(node, SingletonServiceProvider);
    }

    public void RemoveNode(MutableNode node)
    {
        bool removed = Node._childNodes.Remove(node);
        Debug.Assert(removed);
    }

    public void Configure(Action<NodeBuilder> configure)
    {
        configure(this);
    }
}
