using System.ComponentModel;
using System.Diagnostics;
using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

public interface ITreeChangedEventProvider
{
    public event Action TreeStructureChanged;
}

public sealed partial class TreeChangedDispatcher
{
    public event Action TreeStructureChanged;

    public void Trigger()
    {
    }
}

public sealed partial class UiNodeSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly Action _pathChangedSub;
    private readonly ITreeChangedEventProvider _treeChanged;
    private readonly SelectedNodePathModel _path;
    private readonly TreeBuilder _tree;

    [ObservableProperty]
    private UiNode[] _allNodes;

    [ObservableProperty]
    private UiNode _selectedNode;

    partial void OnSelectedNodeChanged(UiNode value)
    {
        _path.NodePath = _tree.BaseNode
            .Dfs()
            .AddLayerPath()
            .Process()
            .Where(x => x.Value.Node.IsLeaf())
            .Where(x => x.Node == value.Leaf.Node)
            .Select(x => x.Get(LayerPathContext.Key).Path())
            .FirstOrDefault(_path.CreateEmptyPath(_tree));
    }

    public UiNodeSelectionViewModel(
        SelectedNodePathModel path,
        TreeBuilder tree,
        ITreeChangedEventProvider treeChanged)
    {
        _path = path;
        _tree = tree;
        _treeChanged = treeChanged;

        _pathChangedSub += () =>
        {
            Reset();
        };
        treeChanged.TreeStructureChanged += _pathChangedSub;

        _allNodes = null!;
        _selectedNode = UiNode.Null;
        ResetNoEvent();
    }

    private void ResetNoEvent()
    {
#pragma warning disable MVVMTK0034
        _allNodes = GetUserNodes();
        if (!_allNodes.Contains(_selectedNode))
        {
            _selectedNode = UiNode.Null;
        }
#pragma warning restore MVVMTK0034
    }

    private void Reset()
    {
        ResetNoEvent();
        OnPropertyChanged((string?) null);
    }

    private UiNode[] GetUserNodes()
    {
        return new[]
            {
                UiNode.Null,
            }
            .Concat(
                _tree
                    .GetMarkerNodes()
                    .Select(x => new UiNode(x.Builder)))
            .ToArray();
    }

    public void Dispose()
    {
        _treeChanged.TreeStructureChanged -= _pathChangedSub;
    }
}

public sealed partial class SelectedUserNodeViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial UserNodeSelectionModel Model { get; private set; }

    public void ExecTreeAction(Func<UiNode?> change)
    {
        ExecTreeAction(() =>
        {
            var ret = change();
            return ValueTask.FromResult(ret);
        }).EnsureCompletedSync();
    }

    public async ValueTask ExecTreeAction(Func<ValueTask<UiNode?>> change)
    {
        TeacherLayerConfig? marker = null;
        if (!Model.SelectedUiNode.IsNull)
        {
            marker = Model.SelectedUiNode.Marker;
        }
        var selectedLayer = await change();
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            var userNodes = GetUserNodes();

            UiNode selectedUiNode;
            if (selectedLayer != null)
            {
                selectedUiNode = selectedLayer;
            }
            else if (marker != null)
            {
                selectedUiNode = userNodes
                    // Remove the Null object
                    .Skip(1)
                    .Where(x => EqualityComparer<TeacherLayerConfig>.Default.Equals(x.Marker, marker))
                    .FirstOrDefault(UiNode.Null);
            }
            else
            {
                selectedUiNode = UiNode.Null;
            }

            ResetModel(new(userNodes, selectedUiNode));
        }
    }
}
