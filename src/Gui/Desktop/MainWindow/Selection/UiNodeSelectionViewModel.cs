using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;
using ScheduleLib.Application.Config;

namespace Desktop.MainWindow;

// I don't know how to do this without an atomic model.
// I've tried everything I could think of,
// but I can't make the UI NOT set the node to null every time the source changes,
// even when it's from a "changed all" event.
public sealed partial class AtomicSelectionModel : ViewModelBase
{
    public AtomicSelectionModel(
        IDispatcher dispatcher,
        UiNode[] allNodes,
        UiNode selectedNode)
        : base(dispatcher)
    {
        Debug.Assert(allNodes.Contains(selectedNode));
        AllNodes = allNodes;
        SelectedNode = selectedNode;
    }

    public UiNode[] AllNodes { get; }

    [ObservableProperty]
    public partial UiNode SelectedNode { get; set; }
}

public sealed partial class UiNodeSelectionViewModel : ViewModelBase, IDisposable
{
    private readonly EventSubscription _treeStructureChangedSub;
    private readonly EventSubscription<UiNode> _uiNodeChangedSub;
    private readonly TreeBuilder _tree;
    private readonly UiSelectedNodeViewModel _uiNode;

    [ObservableProperty]
    public partial AtomicSelectionModel Model { get; private set; }

    private void OnSelectedNodeChanged(UiNode newNode)
    {
        Debug.Assert(newNode != null);
        _uiNode.Value = newNode;
    }

    partial void OnModelChanged(AtomicSelectionModel value)
    {
        value.PropertyChanged += (o, args) =>
        {
            _ = o;
            if (args.PropertyName is "" or null or nameof(Model.SelectedNode))
            {
                OnSelectedNodeChanged(Model.SelectedNode);
            }
        };
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
            ResetModel(_uiNode.Value);
        });
        _uiNodeChangedSub = uiNode.NodeSelected().Sub(n =>
        {
            ResetModel(n);
        });
        Model = null!;
        ResetModel(UiNode.Null);
    }

    private void ResetModel(UiNode n)
    {
        var allNodes = GetUserNodes();
        Model = new(_dispatcher, allNodes, n);
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
