using Anton.LayeredData;

namespace Desktop.ViewModels;

public interface INodeDataChangedEventProvider
{
    public event Action? DataChanged;
}

public sealed class NodeDataChangeDispatcher : INodeDataChangedEventProvider, IDisposable
{
    private readonly SelectedNodePathModel _nodePath;
    private readonly Action<MutableNode?> _onSelectedNodeChanged;

    public event Action? DataChanged;

    public NodeDataChangeDispatcher(
        SelectedNodePathModel nodePath)
    {
        _nodePath = nodePath;
        _onSelectedNodeChanged = node =>
        {
            _ = node;
            DataChanged?.Invoke();
        };
        nodePath.SelectedNodeChanged += _onSelectedNodeChanged;
    }

    public void TriggerDataChanged()
    {
        DataChanged?.Invoke();
    }

    public void Dispose()
    {
        _nodePath.SelectedNodeChanged -= _onSelectedNodeChanged;
    }
}
