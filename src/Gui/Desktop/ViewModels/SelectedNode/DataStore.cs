using Anton.LayeredData;

namespace Desktop.ViewModels;

public sealed class DataStore : IDisposable
{
    public required TreeBuilder TreeBuilder { get; init; }
    public required SelectedNodePathModel SelectedNodePath { get; init; }
    public required NodeDataChangeDispatcher NodeDataChangeDispatcher { get; init; }
    public required UiSelectedNodeModel UiSelectedNode { get; init; }

    public static DataStore Create(TreeBuilder tree)
    {
        var nodePath = new SelectedNodePathModel(tree);
        var dispatcher = new NodeDataChangeDispatcher(nodePath);
        var uiNode = new UiSelectedNodeModel(tree, nodePath);
        return new()
        {
            TreeBuilder = tree,
            NodeDataChangeDispatcher = dispatcher,
            SelectedNodePath = nodePath,
            UiSelectedNode = uiNode,
        };
    }

    public void Dispose()
    {
        NodeDataChangeDispatcher.Dispose();
        UiSelectedNode.Dispose();
    }
}
