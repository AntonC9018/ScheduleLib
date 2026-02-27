using Anton.LayeredData;

namespace Desktop.ViewModels;

public sealed class NodeDataChangeDispatcher : IDisposable
{
    private readonly EventSubscription<MutableNode?> _subscription;
    public readonly EventSource DataChanged = new();

    public NodeDataChangeDispatcher(
        Event<MutableNode?> nodePathChangedEvent)
    {
        _subscription = nodePathChangedEvent.Sub(node =>
        {
            _ = node;
            DataChanged.Invoke();
        });
    }

    public void Dispose() => _subscription.Dispose();
}
