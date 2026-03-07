using System.ComponentModel;
using Anton.LayeredData;

namespace Desktop.ViewModels;

public interface IConfigNodeVmHost
{
    public INotifyPropertyChanged Inner { get; }
    public bool IsEditable { get; }
}

public sealed class ConfigNodeVmHost<T> : ViewModelBase, IDisposable
    where T : class
{
    private readonly ConfigAccessor<T> _accessor;
    private readonly EventSubscription _subscription;

    public ConfigNodeVmHost(
        ConfigAccessor<T> accessor,
        IDisposable inner,
        Action<NodeDataBuilder<T>> onChange,
        Event dataChangedProvider)
    {
        _accessor = accessor;
        Inner = inner;
        _subscription = dataChangedProvider.Sub(() =>
        {
            var b = accessor.MaybeBuilder();
            onChange(b);
            OnPropertyChanged(nameof(IsEditable));
        });
        onChange(accessor.MaybeBuilder());
    }

    public IDisposable Inner { get; }
    public void Dispose() => _subscription.Dispose();
    public bool IsEditable => _accessor.IsEditable;
}

public interface IConfigViewModel<T> : INotifyPropertyChanged
    where T : class
{
    public void UpdateSelection(NodeDataBuilder<T> builder);
}

public abstract class NodeDataViewModelBase<T> : ViewModelBase, IDisposable, IConfigViewModel<T>
    where T : class
{
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }

    public virtual void UpdateSelection(NodeDataBuilder<T> builder)
    {
    }

    void IConfigViewModel<T>.UpdateSelection(NodeDataBuilder<T> builder)
    {
        UpdateSelection(builder);
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
        TreeBuilder tree,
        SelectedNodePathModel nodePathModel,
        NodeDataKey<T> key)
        where T : class
    {
        return new(tree, nodePathModel, key);
    }
}

public sealed class ConfigAccessor<T> where T : class
{
    internal SelectedNodePathModel SelectedNodeModel { get; }
    private readonly TreeBuilder _tree;
    private readonly NodeDataKey<T> _key;

    public ConfigAccessor(
        TreeBuilder tree,
        SelectedNodePathModel selectedNodeModel,
        NodeDataKey<T> key)
    {
        SelectedNodeModel = selectedNodeModel;
        _key = key;
        _tree = tree;
    }

    private MutableNode? SelectedNode => SelectedNodeModel.SelectedNode.Get();
    public bool IsEditable => SelectedNode?.IsOnEditableLayer() ?? false;

    public NodeDataBuilder<T> MaybeBuilder()
    {
        if (!IsEditable)
        {
            return default;
        }
        return Builder();
    }

    public NodeDataBuilder<T> Builder()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Node is not editable.");
        }
        return _tree.CreateBuilder(SelectedNode!).Builder(_key);
    }

    public T? Config
    {
        get
        {
            if (IsEditable)
            {
                return null;
            }
            var ret = MaybeBuilder().Value();
            return ret;
        }
    }
}
