using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

public readonly record struct SelectedNodePathValues(NodePath Path, Layer Layer);

public interface ISelectedNodeProvider
{
    public MutableNode? SelectedNode { get; }
    public event Action<MutableNode?>? SelectedNodeChanged;
}

public sealed partial class SelectedNodePathModel : ObservableObject, ISelectedNodeProvider
{
    // Maybe change to non-observable
    [ObservableProperty] private NodePath _nodePath;
    [ObservableProperty] private Layer _selectedLayer;
    private MutableNode? _selectedNode;
    public MutableNode? SelectedNode => _selectedNode;

    public event Action<MutableNode?>? SelectedNodeChanged;

    public SelectedNodePathModel(TreeBuilder b)
    {
        var n = b.BaseNode;
        var empty = CreateEmptyPath(b);
        ResetNoUpdate(new(empty, n.Layer));
    }

    public NodePath CreateEmptyPath(TreeBuilder b) => new([b.BaseNode]);

    private void ResetNoUpdate(SelectedNodePathValues v)
    {
#pragma warning disable MVVMTK0034
        _nodePath = v.Path;
        _selectedLayer = v.Layer;
        _selectedNode = FindNode();
#pragma warning restore MVVMTK0034
    }

    partial void OnNodePathChanged(NodePath value)
    {
        _ = value;
        ResetNode();
    }
    partial void OnSelectedLayerChanged(Layer value)
    {
        _ = value;
        ResetNode();
    }

    public void Reset(SelectedNodePathValues v)
    {
        ResetNoUpdate(v);
        OnPropertyChanged((string?) null);
    }

    private void ResetNode()
    {
        var node = FindNode();
        if (SetProperty(ref _selectedNode, node, nameof(SelectedNode)))
        {
            SelectedNodeChanged?.Invoke(node);
        }
    }

    private MutableNode? FindNode()
    {
        foreach (var x in NodePath.Path)
        {
            if (x.Layer == SelectedLayer)
            {
                return x;
            }
        }
        return null;
    }

}
