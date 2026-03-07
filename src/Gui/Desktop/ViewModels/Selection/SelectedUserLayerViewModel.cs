using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

public sealed partial class UiNodeSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly EventSubscription _treeStructureChangedSub;
    private readonly EventSubscription<UiNode> _uiNodeChangedSub;
    private readonly TreeBuilder _tree;
    private readonly UiSelectedNodeViewModel _uiNode;

    [ObservableProperty]
    private UiNode[] _allNodes;

    [ObservableProperty]
    private UiNode _selectedNode;

    partial void OnSelectedNodeChanged(UiNode value)
    {
        _uiNode.Value = value;
    }

    public UiNodeSelectionViewModel(
        TreeContext t,
        Event treeStructureChanged,
        UiSelectedNodeViewModel uiNode)
        : base(t.Dispatcher)
    {
        _tree = t.Tree;
        _uiNode = uiNode;
        _treeStructureChangedSub = treeStructureChanged.Sub(() =>
        {
            Reset();
        });
        _uiNodeChangedSub = uiNode.NodeSelected().Sub(n =>
        {
            // Maybe assert this?
            Debug.Assert(AllNodes.Contains(n));
            SelectedNode = n;
        });
        _allNodes = null!;
        _selectedNode = UiNode.Null;
        _allNodes = GetUserNodes();
    }

    private void Reset()
    {
#pragma warning disable MVVMTK0034
        _allNodes = GetUserNodes();
        if (!_allNodes.Contains(_selectedNode))
        {
            _selectedNode = UiNode.Null;
        }
#pragma warning restore MVVMTK0034

        // All changed.
        OnPropertyChanged((string?) null);
        // Have to manually trigger this.
        OnSelectedNodeChanged(SelectedNode);
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
        _treeStructureChangedSub.Dispose();
        _uiNodeChangedSub.Dispose();
    }
}
