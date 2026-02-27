using System.ComponentModel;
using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public sealed record class UiNode
{
    public readonly NodeBuilder Leaf;
    public UiNode(NodeBuilder leaf)
    {
        Leaf = leaf;
    }

    public static readonly UiNode Null = new(default(NodeBuilder));
    public bool IsNull => Leaf.IsNull;
    public bool IsEditable => !IsNull && IsUiLayer;
    public TeacherLayerConfig Marker => Leaf.Node.Get(TeacherLayerConfig.Key).Value.GetValue()!;
    public Name Name => Marker.TeacherName;
    public bool IsUiLayer => Leaf.Node.IsOnEditableLayer();
    public override string ToString() => IsNull ? "No User" : Name.ToString();
}

public sealed partial class UiSelectedNodeViewModel : ObservableObject, IDisposable
{
    private readonly EventSource<UiNode> _nodeSelected = new();
    public Event<UiNode> NodeSelected() => _nodeSelected;

    [ObservableProperty]
    public partial UiNode Value { get; set; } = UiNode.Null;

    partial void OnValueChanged(UiNode value)
    {
        _nodeSelected.Invoke(value);

        var path = _path.CreateEmptyPath(_tree);
        if (!value.IsNull)
        {
            path = _tree.BaseNode
                .Dfs()
                .AddLayerPath()
                .Process()
                .Where(x => x.Value.Node.IsLeaf())
                .Where(x => x.Node == value.Leaf.Node)
                .Select(x => x.Get(LayerPathContext.Key).Path())
                .FirstOrDefault(path);
        }
        _path.NodePath = path;
    }

    private readonly SelectedNodePathModel _path;
    private readonly EventSubscription<NodePath> _nodePathSub;
    private readonly TreeBuilder _tree;

    public UiSelectedNodeViewModel(
        TreeBuilder tree,
        SelectedNodePathModel pathSelectionModel)
    {
        _path = pathSelectionModel;
        _tree = tree;
        _nodePathSub = pathSelectionModel.PathChanged.Sub(path =>
        {
            _ = path;
            var newNode = CreateCurrentNode();
            Value = newNode;
        });
    }

    private UiNode CreateCurrentNode()
    {
        var nodePath = _path.NodePath;
        var leaf = nodePath.Leaf;
        if (leaf.Layer != LayerKeys.TeacherLayerKey
            && leaf.Layer != UiLayerHelper.UiTeacherLayer)
        {
            return UiNode.Null;
        }
        else
        {
            var b = _tree.CreateBuilder(leaf);
            return new(b);
        }
    }

    public void Dispose() => _nodePathSub.Dispose();
}
