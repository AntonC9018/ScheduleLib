using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

public sealed class NodeDataAccessor<T> : IDisposable
    where T : class
{
    internal SelectedNodePathModel SelectedNodeModel { get; }
    private readonly TreeBuilder _tree;
    private readonly NodeDataKey<T> _key;
    private readonly EventSubscription _nodeDataChangedSub;

    private T? _someValue;
    private bool _valueSaved;

    public NodeDataAccessor(
        TreeBuilder tree,
        SelectedNodePathModel selectedNodeModel,
        NodeDataKey<T> key,
        Event nodeDataChanged)
    {
        SelectedNodeModel = selectedNodeModel;
        _key = key;
        _tree = tree;
        _nodeDataChangedSub = nodeDataChanged.Sub(() =>
        {
            _someValue = null;
            _valueSaved = false;
        });
    }

    public void Dispose()
    {
        _nodeDataChangedSub.Dispose();
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

    private NodeDataBuilder<T> _CreateBuilder()
    {
        return _tree.CreateBuilder(SelectedNode!).Builder(_key);
    }

    public NodeDataBuilder<T> Builder()
    {
        if (!IsEditable)
        {
            throw new InvalidOperationException("Node is not editable.");
        }
        return _CreateBuilder();
    }

    public T? EditableData
    {
        get
        {
            if (IsEditable)
            {
                return ConditionallyEditableData.Value;
            }
            return null;
        }
    }

    public T? Data
    {
        get
        {
            if (IsEditable)
            {
                return EditableData;
            }
            return ConditionallyEditableData.Value;
        }
    }

    public ConditionallyEditableData<T> ConditionallyEditableData
    {
        get
        {
            if (!_valueSaved)
            {
                _someValue = CreateConditionallyEditableData();
                _valueSaved = true;
            }
            return new(IsEditable: IsEditable, _someValue);
        }
    }

    private const bool constructsValue = false;

    private T? CreateConditionallyEditableData()
    {
        var selectedNode = SelectedNode;
        if (selectedNode is null)
        {
            return null;
        }
        if (IsEditable)
        {
            return _CreateBuilder().Value();
        }
#pragma warning disable CS0162 // Unreachable code detected
        if (constructsValue)
        {
            var path = SelectedNodeModel.NodePath.Get();
            var pathPart = path.SliceUntilInclusive(selectedNode);
            using var scope = _tree.SingletonServiceProvider.CreateScope();
            try
            {
                var value = pathPart.ConstructValue(_key, scope.ServiceProvider);
                return value;
            }
            catch
            {
                // Might fail to work if there are updaters
                // that expects services to be set up in some particular way.
                return null;
            }
        }
        else
        {
            var ret = _CreateBuilder().TryGetValue();
            return ret;
        }
#pragma warning restore CS0162 // Unreachable code detected
    }
}

public readonly record struct ConditionallyEditableData<T>(
    bool IsEditable,
    T? Value)
{
}
