using System.ComponentModel;
using Desktop.MvvmEssentials;

namespace Desktop.NodeData.Common;

public interface INodeDataVmHost
{
    public INotifyPropertyChanged Inner { get; }
    public bool IsEditable { get; }
}

public sealed class NodeDataVmHost<T> : ViewModelBase, INodeDataVmHost, IDisposable
    where T : class
{
    private readonly NodeDataAccessor<T> _accessor;
    private readonly EventSubscription _subscription;

    public NodeDataVmHost(
        IDispatcher dispatcher,
        NodeDataAccessor<T> accessor,
        IDisposable inner,
        Action onChange,
        Event dataChangedProvider)
        : base(dispatcher)
    {
        _accessor = accessor;
        Inner = inner;
        _subscription = dataChangedProvider.Sub(() =>
        {
            accessor.MaybeBuilder();
            onChange();
            OnPropertyChanged(nameof(IsEditable));
        });
        onChange();
    }

    public IDisposable Inner { get; }
    INotifyPropertyChanged INodeDataVmHost.Inner => (INotifyPropertyChanged) Inner;
    public void Dispose() => _subscription.Dispose();
    public bool IsEditable => _accessor.IsEditable;
}
