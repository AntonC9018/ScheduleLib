using System.Collections.Immutable;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;

namespace Anton.LayeredConfig;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path)
{
    public readonly MutableLayer Root => Path[0];
    public readonly MutableLayer Leaf => Path[^1];
}

public sealed class LayerPathContext() : IDfsEnumerationContext
{
    public static readonly EnumerationContextKey<LayerPathContext> Key = EnumerationContextKey.Registry.Register<LayerPathContext>();
    public readonly ImmutableArray<MutableLayer>.Builder Builder = ImmutableArray.CreateBuilder<MutableLayer>();

    public void Update(DfsEnumerationContext context)
    {
        switch (context.State)
        {
            case DfsVisitationState.Process:
            {
                Builder.Add(context.Layer);
                break;
            }
            case DfsVisitationState.AfterProcess:
            {
                Builder.Count--;
                break;
            }
        }
    }

    public LayerPath Path() => new(Builder.ToImmutable());
    public MutableLayer? Parent => Builder.Count == 1 ? null : Builder[^2];
}

public static class LayerPathEnumerationExtensions
{
    public static DfsEnumerable AddLayerPath(this DfsEnumerable e)
    {
        e.AddContext(LayerPathContext.Key, () => new());
        return e;
    }
}
