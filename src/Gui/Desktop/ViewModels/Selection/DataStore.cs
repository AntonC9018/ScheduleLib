using Anton.LayeredData;

namespace Desktop.ViewModels;

public sealed class DataStore : IDisposable
{
    public required TreeBuilder TreeBuilder { get; init; }
    public required SelectedNodePathModel SelectedNodePath { get; init; }
    public required NodeDataChangeDispatcher NodeDataChangeDispatcher { get; init; }
    public required UiSelectedNodeViewModel UiSelectedNodeView { get; init; }
    public required EventSource TreeStructureChanged { get; init; }

    public static DataStore Create(TreeBuilder tree)
    {
        var nodePath = new SelectedNodePathModel(tree);
        var dispatcher = new NodeDataChangeDispatcher(nodePath.SelectedNode.Changed);
        var uiNode = new UiSelectedNodeViewModel(tree, nodePath);
        return new()
        {
            TreeBuilder = tree,
            NodeDataChangeDispatcher = dispatcher,
            SelectedNodePath = nodePath,
            UiSelectedNodeView = uiNode,
            TreeStructureChanged = new(),
        };
    }

    public void Dispose()
    {
        NodeDataChangeDispatcher.Dispose();
        UiSelectedNodeView.Dispose();
    }
}
