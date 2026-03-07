using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;

namespace Desktop.ViewModels;

public sealed partial class SelectedNodePathModel
{
    private ObservableValueSource<NodePath> _nodePath;
    public ObservableValue<NodePath> NodePath => _nodePath.As();

    private ObservableValueSource<Layer> _selectedLayer;
    public ObservableValue<Layer> SelectedLayer => _selectedLayer.As();

    private ObservableValueSource<MutableNode?> _selectedNode;
    public ReadOnlyObservableValue<MutableNode?> SelectedNode => _selectedNode.AsReadOnly();

    public SelectedNodePathModel(TreeContext t)
    {
        var n = t.Tree.BaseNode;
        var empty = CreateEmptyPath(t.Tree);

        _nodePath = t.Dispatcher.CreateObservableValue(empty);
        _selectedLayer = t.Dispatcher.CreateObservableValue(n.Layer);
        _selectedNode = t.Dispatcher.CreateObservableValue(FindNode());

        _nodePath.Event.Sub(p =>
        {
            _ = p;
            ResetNode();
        });
        _selectedLayer.Event.Sub(l =>
        {
            _ = l;
            ResetNode();
        });
    }

    public NodePath CreateEmptyPath(TreeBuilder b) => new([b.BaseNode]);

    private void ResetNode()
    {
        var node = FindNode();
        _selectedNode.Value = node;
    }

    private MutableNode? FindNode()
    {
        foreach (var x in _nodePath.Value.Path)
        {
            if (x.Layer == _selectedLayer.Value)
            {
                return x;
            }
        }
        return null;
    }
}
