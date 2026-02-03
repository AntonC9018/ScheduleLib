using System.Collections.Immutable;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;

namespace Anton.LayeredConfig;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path)
{
    public readonly MutableLayer Root => Path[0];
    public readonly MutableLayer Leaf => Path[^1];
}

public readonly struct LayerPathContext() : ILayerStateEnumerationContext
{
    public readonly ImmutableArray<MutableLayer>.Builder Builder = ImmutableArray.CreateBuilder<MutableLayer>();

    public void Consume(LayerStateEnumerator.Value value)
    {
        switch (value.State)
        {
            case VisitorState.Process:
            {
                Builder.Add(value.Layer);
                break;
            }
            case VisitorState.AfterProcess:
            {
                Builder.Count--;
                break;
            }
        }
    }

    public LayerPath Path() => new(Builder.ToImmutable());
}

public static class LayerPathEnumerationExtensions
{
    public static IEnumerable<LayerStateEnumerator.Value> AddLayerPath(
        this IEnumerable<LayerStateEnumerator.Value> e,
        out LayerPathContext layerPath)
    {
        e = e.DebugAssertSingleUse();

        var x = new LayerPathContext();
        layerPath = x;
        return new ContextWrappedLayerEnumerable(() => x, e);
    }
}
