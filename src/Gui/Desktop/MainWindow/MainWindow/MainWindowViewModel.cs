using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using Desktop.NodeData.Editor;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.MainWindow;

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly TreeContext _treeContext;
    private TreeBuilder Tree => _treeContext.Tree;
    internal readonly DataStore _dataStore;
    private readonly UiTreeSerializer _serializer;
    private readonly UpdateTreeHelper _updateTreeHelper;

    private readonly EventSubscription<LayerLevel> _layerChangedSub;
    private readonly EventSubscription<UiNode> _nodeSelectedSub;
    private readonly ScheduleLoading _scheduleLoading;
    private readonly EventSubscription<string> _scheduleStatusSub;
    private readonly EventSubscription _userAddedSub;
    public string ScheduleStatus => _scheduleLoading.Status.Get();

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = "Select a teacher or add one from the schedule to get started.";

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public string SelectionTitle => SelectedUiNode.IsNull ? "Shared configuration" : SelectedUiNode.Name.ToString();
    public string SelectionDescription => LayerLevel switch
    {
        LayerLevel.Default => "Application defaults · read only",
        LayerLevel.ProgrammableUser => "Configured teacher settings · read only. Enable UI editing to make changes.",
        _ => SelectedUiNode.IsEditable
            ? "UI settings · changes apply to this teacher. Save layers to keep your changes."
            : "Select a teacher and enable UI editing to change settings.",
    };

    public NodeDataEditorViewModel NodeDataEditor { get; }
    public UiNodeSelectionViewModel NodeSelection { get; }
    public LayerLevelSelectionViewModel LayerLevelSelection { get; }
    public AddUserViewModel AddUser { get; }

    public MainWindowViewModel(
        TreeContext treeContext,
        UiTreeSerializer serializer,
        ScheduleLoading scheduleLoading,
        IServiceProvider sp)
        : base(treeContext.Dispatcher)
    {
        _treeContext = treeContext;
        _serializer = serializer;
        _scheduleLoading = scheduleLoading;
        _scheduleStatusSub = scheduleLoading.Status.Changed.Sub(_ => OnPropertyChanged(nameof(ScheduleStatus)));
        _dataStore = DataStore.Create(treeContext);
        _updateTreeHelper = new(_dataStore.SelectedNodePath, treeContext);

        NodeDataEditor = ActivatorUtilities.CreateInstance<NodeDataEditorViewModel>(sp, [_dataStore]);

        NodeSelection = new(
            treeContext,
            _dataStore.TreeStructureChanged,
            _dataStore.UiSelectedNodeViewModel);
        LayerLevelSelection = new(
            treeContext.Dispatcher,
            _dataStore.SelectedNodePath);
        AddUser = ActivatorUtilities.CreateInstance<AddUserViewModel>(
            sp,
            treeContext,
            _updateTreeHelper,
            (Event) _dataStore.TreeStructureChanged,
            LayerLevelSelection);

        _userAddedSub = AddUser.UserAdded.Sub(() =>
        {
            StatusMessage = "Teacher added. Save layers to keep the new UI settings.";
        });

        _layerChangedSub = LayerLevelSelection.LayerLevelChanged.Sub(layer =>
        {
            _ = layer;
            OnPropertyChanged(nameof(CanSelectUser));
            OnPropertyChanged(nameof(SelectionTitle));
            OnPropertyChanged(nameof(SelectionDescription));
            EnableSelectedUserCommand.NotifyCanExecuteChanged();
            RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        });

        _nodeSelectedSub = _dataStore.UiSelectedNodeViewModel.NodeSelected().Sub(node =>
        {
            _ = node;
            OnPropertyChanged(nameof(SelectionTitle));
            OnPropertyChanged(nameof(SelectionDescription));
            EnableSelectedUserCommand.NotifyCanExecuteChanged();
            RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        });
    }

    private LayerLevel LayerLevel
    {
        get => LayerLevelSelection.LayerLevel;
        set => LayerLevelSelection.LayerLevel = value;
    }
    private UiNode SelectedUiNode => _dataStore.UiSelectedNodeViewModel.Value;

    public bool CanSelectUser => LayerLevel is LayerLevel.UiUser or LayerLevel.ProgrammableUser;

    public bool CanRemoveSelectedUser
    {
        get
        {
            if (LayerLevel != LayerLevel.UiUser)
            {
                return false;
            }
            var node = SelectedUiNode;
            if (node.IsNull)
            {
                return false;
            }
            if (node.IsUiLayer)
            {
                return true;
            }
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedUser))]
    public void RemoveSelectedUser()
    {
        Debug.Assert(CanRemoveSelectedUser);
        _updateTreeHelper.ExecTreeAction(() =>
        {
            UiLayerHelper.MaybeRemoveLayer(SelectedUiNode.Leaf.Node, Tree);
            return default;
        });
        StatusMessage = "UI settings removed. Save layers to keep this change.";
    }

    public bool CanEnableSelectedUser
    {
        get
        {
            if (LayerLevel != LayerLevel.ProgrammableUser
                && LayerLevel != LayerLevel.UiUser)
            {
                return false;
            }
            var n = SelectedUiNode;
            if (n.IsNull)
            {
                return false;
            }
            if (n.IsEditable)
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnableSelectedUser))]
    public void EnableSelectedUser()
    {
        if (SelectedUiNode.IsNull)
        {
            return;
        }
        _updateTreeHelper.ExecTreeAction(() =>
        {
            var n = SelectedUiNode;
            var b = n.Leaf.MaybeCreateUiLayer(n.Marker);
            _ = b;
            return default;
        });
        LayerLevel = LayerLevel.UiUser;
        StatusMessage = "UI editing enabled. Save layers to keep your changes.";
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        await RunFileOperation(async () =>
        {
            await _serializer.Serialize(Tree);
            return "UI layers saved to ui-layers.json.";
        });
    }

    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await RunFileOperation(async () =>
        {
            bool loaded = false;
            await _updateTreeHelper.ExecTreeActionAsync(async () =>
            {
                loaded = await _serializer.Deserialize(Tree);
                return default;
            });
            if (!loaded)
            {
                return "No saved UI layers found. Save your layers first.";
            }
            _dataStore.TreeStructureChanged.Invoke();
            _dataStore.NodeDataChangeDispatcher.DataChanged.Invoke();
            return "UI layers loaded from ui-layers.json.";
        });
    }

    private async Task RunFileOperation(Func<Task<string>> action)
    {
        if (IsBusy)
        {
            return;
        }
        IsBusy = true;
        StatusMessage = "Working…";
        try
        {
            StatusMessage = await action();
        }
        catch (Exception e)
        {
            StatusMessage = $"Could not save or load UI layers: {e.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void Dispose()
    {
        _layerChangedSub.Dispose();
        _nodeSelectedSub.Dispose();
        _scheduleStatusSub.Dispose();
        _userAddedSub.Dispose();
        NodeDataEditor.Dispose();
        AddUser.Dispose();
        NodeSelection.Dispose();
        LayerLevelSelection.Dispose();
        _dataStore.Dispose();
    }
}
