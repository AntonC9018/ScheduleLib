using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;

namespace Desktop.ViewModels;

// This is required to be able to update both AllNodes and SelectedNode at once.
// ComboBoxes are supposed to bind to the whole model atomically.
// This model cannot be reused and must be fully replaced.
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

#pragma warning disable CS9264 // Non-nullable property must contain a non-null value when exiting constructor. Consider adding the 'required' modifier, or declaring the property as nullable, or adding '[field: MaybeNull, AllowNull]' attributes.
    public SelectedUserNodeViewModel(TreeBuilder configBuilder)
    {
        _configBuilder = configBuilder;
        ResetModel(new(GetUserNodes(), WrappedNode.Null));
    }
#pragma warning restore CS9264 // Non-nullable property must contain a non-null value when exiting constructor. Consider adding the 'required' modifier, or declaring the property as nullable, or adding '[field: MaybeNull, AllowNull]' attributes.

    [ObservableProperty]
    public partial UserNodeSelectionModel Model { get; private set; }

    public WrappedNode SelectedNode
    {
        get => Model.SelectedNode;
        set => Model.SelectedNode = value;
    }

    private void ResetModel(UserNodeSelectionModel model)
    {
        Debug.Assert(!ReferenceEquals(model, Model));
        model.PropertyChanged += (o, args) =>
        {
            _ = o;
            Debug.Assert(args.PropertyName == nameof(model.SelectedNode));
            OnPropertyChanged(nameof(SelectedNode));
        };
        // Null in the constructor.
        var oldValue = Model?.SelectedNode;
        Model = model;
        if (oldValue != model.SelectedNode)
        {
            OnPropertyChanged(nameof(model.SelectedNode));
        }
    }

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

            ResetModel(new(userNodes, selectedNode));
        }
    }
}
