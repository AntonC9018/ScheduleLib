using System.Collections;

namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

public static class LayerStateEnumerableExtensions
{
    extension(IEnumerable<LayerStateEnumerator.Value> builder)
    {
        public IEnumerable<MutableLayer> SelectWithState(
            VisitorState state)
        {
            foreach (var x in builder)
            {
                if (x.State == state)
                {
                    yield return x.Layer;
                }
            }
        }

        public IEnumerable<MutableLayer> Process()
        {
            return builder.SelectWithState(VisitorState.Process);
        }
    }

    extension(ILayerStateEnumerable builder)
    {
        public SingleUseLayerStateEnumerable AsSingleUse(
            out LayerStateVisitationController controller)
        {
            controller = new LayerStateVisitationController();
            var ret = new SingleUseLayerStateEnumerable(builder, controller);
            return ret;
        }
    }
}

public sealed class SingleUseLayerStateEnumerable : IEnumerable<LayerStateEnumerator.Value>
{
    private readonly LayerStateVisitationController _controller;
    private SingleUseItemHelper<ILayerStateEnumerable> _e;

    public SingleUseLayerStateEnumerable(
        ILayerStateEnumerable e,
        LayerStateVisitationController controller)
    {
        _controller = controller;
        _e = new(e);
    }

    public ILayerStateEnumerator GetEnumerator()
    {
        if (_e.Get() is not { } x)
        {
            throw new InvalidOperationException("May only be enumerated once.");
        }
        var e = x.GetEnumerator();
        try
        {
            _controller.Init(e);
        }
        catch
        {
            e.Dispose();
            throw;
        }
        return e;
    }

    IEnumerator<LayerStateEnumerator.Value> IEnumerable<LayerStateEnumerator.Value>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
