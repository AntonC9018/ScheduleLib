using System.Collections.Immutable;
using Anton.LayeredData.TreeEnumeration.Infrastructure;

namespace Anton.LayeredData.TreeEnumeration;

public readonly record struct NodePath(ImmutableArray<MutableNode> Path)
{
    public readonly MutableNode Root => Path[0];
    public readonly MutableNode Leaf => Path[^1];
}

public sealed class LayerPathContext() : IDfsEnumerationContext
{
    public static readonly EnumerationContextKey<LayerPathContext> Key = EnumerationContextKey.Registry.Register<LayerPathContext>();
    public readonly ImmutableArray<MutableNode>.Builder Builder = ImmutableArray.CreateBuilder<MutableNode>();

    public void Update(DfsEnumerationContext context)
    {
        switch (context.State)
        {
            case DfsVisitationState.Process:
            {
                Builder.Add(context.Node);
                break;
            }
            case DfsVisitationState.AfterProcess:
            {
                Builder.Count--;
                break;
            }
        }
    }

    public NodePath Path() => new(Builder.ToImmutable());
    public MutableNode? Parent => Builder.Count == 1 ? null : Builder[^2];
}

public static class LayerPathEnumerationExtensions
{
    public static DfsEnumerable AddLayerPath(this DfsEnumerable e)
    {
        e.AddContext(LayerPathContext.Key, () => new());
        return e;
    }
}
