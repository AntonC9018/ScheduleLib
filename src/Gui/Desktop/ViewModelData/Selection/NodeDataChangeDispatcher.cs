using Anton.LayeredData;
using Desktop.MvvmEssentials;

namespace Desktop.ViewModelData;

public sealed class NodeDataChangeDispatcher : IDisposable
{
    private readonly EventSubscription<MutableNode?> _subscription;
    public readonly EventSource<Nothing> DataChanged;

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
