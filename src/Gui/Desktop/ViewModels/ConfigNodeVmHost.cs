using System.ComponentModel;
using Anton.LayeredData;

namespace Desktop.ViewModels;

public interface IConfigNodeVmHost
{
    public object Inner { get; }
    public bool IsEditable { get; }
}

public sealed class ConfigNodeVmHost<T> : ViewModelBase, IDisposable, IConfigNodeVmHost
    where T : class
{
    private readonly ConfigViewModelSubscription<T> _subscription;

    public ConfigNodeVmHost(
        ConfigAccessor<T> accessor,
        IConfigViewModel<T> inner)
    {
        Inner = inner;
        _subscription = new(accessor, b =>
        {
            inner.UpdateSelection(b);
            OnPropertyChanged(nameof(IsEditable));
        });
    }

    public IConfigViewModel<T> Inner { get; }
    object IConfigNodeVmHost.Inner => Inner;
    public void Dispose() => _subscription.Dispose();
    public bool IsEditable => !_subscription.Accessor.IsNull;
}

public interface IConfigViewModel<T> : INotifyPropertyChanged
    where T : class
{
    public void UpdateSelection(NodeDataBuilder<T> builder);
}

public abstract class ConfigViewModelBase<T> : ViewModelBase, IDisposable, IConfigViewModel<T>
    where T : class
{
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public virtual void UpdateSelection(NodeDataBuilder<T> builder)
    {
        AllPropertiesChanged();
    }

    protected void AllPropertiesChanged()
    {
        OnPropertyChanged((string?) null);
    }
}

public static class ConfigAccessor
{
    public static ConfigAccessor<T> Create<T>(
        ISelectedUserNode selectedNodeProvider,
        NodeDataKey<T> key)
        where T : class
    {
        return new(selectedNodeProvider, key);
    }
}

public sealed class ConfigAccessor<T> where T : class
{
    internal ISelectedUserNode SelectedNodeProvider { get; }
    private readonly NodeDataKey<T> _key;

    public ConfigAccessor(
        ISelectedUserNode selectedNodeProvider,
        NodeDataKey<T> key)
    {
        SelectedNodeProvider = selectedNodeProvider;
        _key = key;
    }

    private WrappedNode SelectedNode => SelectedNodeProvider.SelectedNode;

    public bool IsNull => SelectedNode.IsNull || !SelectedNode.IsUiLayer;

    public NodeDataBuilder<T> MaybeBuilder()
    {
        if (IsNull)
        {
            return default;
        }
        return Builder();
    }
    public NodeDataBuilder<T> Builder()
    {
        if (IsNull)
        {
            throw new InvalidOperationException("Node is not editable.");
        }
        return SelectedNode.Leaf.Builder(_key);
    }

    public T? Config
    {
        get
        {
            if (IsNull)
            {
                return null;
            }
            var ret = MaybeBuilder().Value();
            return ret;
        }
    }
}

public readonly struct ConfigViewModelSubscription<T> : IDisposable
    where T : class
{
    public ConfigAccessor<T> Accessor { get; }
    private readonly Action<WrappedNode> _nodeChanged;

    public ConfigViewModelSubscription(
        ConfigAccessor<T> accessor,
        Action<NodeDataBuilder<T>> onNodeChanged)
    {
        Accessor = accessor;
        _nodeChanged = node =>
        {
            _ = node;
            var b = accessor.MaybeBuilder();
            onNodeChanged(b);
        };
        Accessor.SelectedNodeProvider.OnSelectedNodeChanged += _nodeChanged;
    }

    public void Dispose()
    {
        Accessor.SelectedNodeProvider.OnSelectedNodeChanged -= _nodeChanged;
    }
}
