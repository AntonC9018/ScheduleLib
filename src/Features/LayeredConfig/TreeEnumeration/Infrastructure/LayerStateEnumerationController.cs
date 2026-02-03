namespace Anton.LayeredConfig.TreeEnumeration.Infrastructure;

// This is really difficult to make into a struct.
// The enumerator will basically always have to be boxed.
// To make this a ref struct, the enumeration itself must never box,
// and all methods must have a counterpart where the parameter is a ref struct.
// Implementing that is going to take forever (and it's pointless in this case).
public interface ILayerStateVisitationController
{
    void Init(ILayerStateEnumerator e);
    VisitorAction Action { get; set; }
}

public sealed class LayerStateVisitationController : ILayerStateVisitationController
{
    private ILayerStateEnumerator? _e;

    public void Init(ILayerStateEnumerator e) => _e = e;

    public VisitorAction Action
    {
        get => _e!.Action;
        set => _e!.Action = value;
    }
}

