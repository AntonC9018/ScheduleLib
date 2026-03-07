using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly TreeContext _treeContext;
    private TreeBuilder Tree => _treeContext.Tree;
    internal readonly DataStore _dataStore;
    private readonly UiTreeSerializer _serializer;
    private readonly UpdateTreeHelper _updateTreeHelper;

    public NodeDataEditorViewModel NodeDataEditor { get; }
    public UiNodeSelectionViewModel NodeSelection { get; }
    public LayerLevelSelectionViewModel LayerLevelSelection { get; }
    public AddUserViewModel AddUser { get; }

    public MainWindowViewModel(
        TreeContext treeContext,
        UiTreeSerializer serializer,
        IServiceProvider sp)
        : base(treeContext.Dispatcher)
    {
        _treeContext = treeContext;
        _serializer = serializer;
        _dataStore = DataStore.Create(treeContext);
        _updateTreeHelper = new(_dataStore.SelectedNodePath, treeContext);

        // We own the instance, not the SP
        NodeDataEditor = ActivatorUtilities.CreateInstance<NodeDataEditorViewModel>(sp, [_dataStore]);

        NodeSelection = new(
            treeContext,
            _dataStore.TreeStructureChanged,
            _dataStore.UiSelectedNodeViewModel);
        LayerLevelSelection = new(
            treeContext.Dispatcher,
            _dataStore.SelectedNodePath);
        AddUser = new(
            treeContext,
            _updateTreeHelper,
            _dataStore.TreeStructureChanged,
            LayerLevelSelection);

        LayerLevelSelection.LayerLevelChanged.Sub(layer =>
        {
            _ = layer;
            OnPropertyChanged(nameof(CanSelectUser));
            EnableSelectedUserCommand.NotifyCanExecuteChanged();
            RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        });

        _dataStore.UiSelectedNodeViewModel.NodeSelected().Sub(node =>
        {
            _ = node;
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
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        await _serializer.Serialize(Tree);
    }

    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await _updateTreeHelper.ExecTreeActionAsync(async () =>
        {
            await _serializer.Deserialize(Tree);
            return default;
        });
        _dataStore.TreeStructureChanged.Invoke();
        _dataStore.NodeDataChangeDispatcher.DataChanged.Invoke();
    }
}
