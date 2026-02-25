using System.ComponentModel;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

public sealed partial class UiSelectedNodeController : ObservableObject, IDisposable
{
    public event Action<UiNode>? NodeSelected;

    [ObservableProperty]
    public partial UiNode SelectedUiNode { get; private set; } = UiNode.Null;

    partial void OnSelectedNodeChanged(UiNode value)
    {
        NodeSelected?.Invoke(value);
    }

    private readonly SelectedNodePathModel _pathSelectionModel;
    private readonly PropertyChangedEventHandler _subscription1;
    private readonly TreeBuilder _builder;

    public UiSelectedNodeController(
        TreeBuilder builder,
        SelectedNodePathModel pathSelectionModel)
    {
        _pathSelectionModel = pathSelectionModel;
        _builder = builder;
        _subscription1 = (o, args) =>
        {
            _ = o;
            if (args.PropertyName is nameof(_pathSelectionModel.NodePath) or "" or null)
            {
                var newNode = CreateCurrentNode();
                SelectedUiNode = newNode;
            }
        };
        pathSelectionModel.PropertyChanged += _subscription1;
    }

    private UiNode CreateCurrentNode()
    {
        var nodePath = _pathSelectionModel.NodePath;
        var leaf = nodePath.Leaf;
        if (leaf.Layer != LayerKeys.TeacherLayerKey
            && leaf.Layer != UiLayerHelper.UiTeacherLayer)
        {
            return UiNode.Null;
        }
        else
        {
            var b = _builder.CreateBuilder(leaf);
            return new(b);
        }
    }

    public void Dispose()
    {
        _pathSelectionModel.PropertyChanged -= _subscription1;
    }
}
