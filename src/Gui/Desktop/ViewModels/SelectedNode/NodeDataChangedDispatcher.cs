using Anton.LayeredData;

namespace Desktop.ViewModels;

public interface INodeDataChangedEventProvider
{
    public event Action? DataChanged;
}

public sealed class NodeDataChangedDispatcher : INodeDataChangedEventProvider, IDisposable
{
    private readonly SelectedNodePathModel _nodePath;
    private readonly Action<MutableNode?> _subscription;

    public event Action? DataChanged;

    public NodeDataChangedDispatcher(
        SelectedNodePathModel nodePath)
    {
        _nodePath = nodePath;
        _subscription = node =>
        {
            _ = node;
            DataChanged?.Invoke();
        };
        nodePath.SelectedNodeChanged += _subscription;
    }

    public void TriggerDataChanged()
    {
        DataChanged?.Invoke();
    }

    public void Dispose()
    {
        _nodePath.SelectedNodeChanged -= _subscription;
    }
}
