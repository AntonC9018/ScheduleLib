using Anton.LayeredData;

namespace Desktop.ViewModels;

public sealed class NodeDataChangeDispatcher : IDisposable
{
    private readonly EventSubscription<MutableNode?> _subscription;
    public readonly EventSource<Void> DataChanged;

    public NodeDataChangeDispatcher(
        TreeEventDispatcher dispatcher,
        Event<MutableNode?> nodePathChangedEvent)
    {
        DataChanged = dispatcher.CreateEvent();
        _subscription = nodePathChangedEvent.Sub(node =>
        {
            _ = node;
            DataChanged.Invoke();
        });
    }

    public void Dispose() => _subscription.Dispose();
}
