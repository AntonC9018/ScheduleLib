using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

public sealed partial class UserNodeSelectionModel : ObservableObject
{
    public UserNodeSelectionModel(WrappedNode[] all, WrappedNode? selected = null)
    {
        AllNodes = all;
        SelectedNode = selected ?? WrappedNode.Null;
    }

    [ObservableProperty]
    public partial WrappedNode[] AllNodes { get; private set; }

    [ObservableProperty]
    public partial WrappedNode SelectedNode { get; set; }
}

public sealed partial class SelectedUserNodeViewModel : ViewModelBase
{
    private readonly TreeBuilder _configBuilder;

    public SelectedUserNodeViewModel(TreeBuilder configBuilder)
    {
        _configBuilder = configBuilder;
        Model = new(GetUserNodes(), WrappedNode.Null);
    }

    [ObservableProperty]
    public partial UserNodeSelectionModel Model { get; private set; }

    public WrappedNode SelectedNode => Model.SelectedNode;

    private WrappedNode[] GetUserNodes()
    {
        return new[]
            {
                WrappedNode.Null,
            }
            .Concat(
                _configBuilder
                    .GetMarkerNodes()
                    .Select(x => new WrappedNode(x.Builder)))
            .ToArray();
    }

    public void ExecTreeAction(Func<WrappedNode?> change)
    {
        ExecTreeAction(() =>
        {
            var ret = change();
            return ValueTask.FromResult(ret);
        }).EnsureCompletedSync();
    }

    public async ValueTask ExecTreeAction(Func<ValueTask<WrappedNode?>> change)
    {
        TeacherLayerConfig? marker = null;
        if (!Model.SelectedNode.IsNull)
        {
            marker = Model.SelectedNode.Marker;
        }
        var selectedLayer = await change();
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            var userNodes = GetUserNodes();

            WrappedNode selectedNode;
            if (selectedLayer != null)
            {
                selectedNode = selectedLayer;
            }
            else if (marker != null)
            {
                selectedNode = userNodes
                    // Remove the Null object
                    .Skip(1)
                    .Where(x => EqualityComparer<TeacherLayerConfig>.Default.Equals(x.Marker, marker))
                    .FirstOrDefault(WrappedNode.Null);
            }
            else
            {
                selectedNode = WrappedNode.Null;
            }

            Model = new(userNodes, selectedNode);
        }
    }
}
