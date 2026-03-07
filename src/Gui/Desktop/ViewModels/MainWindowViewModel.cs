using System.Diagnostics;
using Anton.LayeredData;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public sealed class UiSettings
{
    public string DatabaseFilePath { get; set; } = "ui-layers.json";
    public bool OpenAfterSave { get; set; }
}

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly TreeContext _treeContext;
    private TreeBuilder Tree => _treeContext.Tree;
    private readonly TreeSerializer _serializer;
    internal readonly DataStore _dataStore;
    private readonly UpdateTreeHelper _updateTreeHelper;
    private readonly IOptions<UiSettings> _settings;

    public NodeDataEditorViewModel NodeDataEditor { get; }
    public UiNodeSelectionViewModel NodeSelection { get; }
    public LayerLevelSelectionViewModel LayerLevelSelection { get; }

    public MainWindowViewModel(
        TreeContext treeContext,
        TreeSerializer serializer,
        IServiceProvider sp,
        IOptions<UiSettings> settings)
        : base(treeContext.Dispatcher)
    {
        _treeContext = treeContext;
        _settings = settings;
        _serializer = serializer;
        _dataStore = DataStore.Create(treeContext);
        _updateTreeHelper = new(_dataStore.SelectedNodePath, treeContext);

        // We own the instance, not the SP
        NodeDataEditor = ActivatorUtilities.CreateInstance<NodeDataEditorViewModel>(sp, [_dataStore]);
        NodeSelection = new(
            treeContext,
            _dataStore.TreeStructureChanged,
            _dataStore.UiSelectedNodeView);
        LayerLevelSelection = new(
            treeContext.Dispatcher,
            _dataStore.SelectedNodePath);

        LayerLevelSelection.LayerLevelChanged.Sub(layer =>
        {
            _ = layer;
            OnPropertyChanged(nameof(CanSelectUser));
            OnPropertyChanged(nameof(CanSelectUserToAdd));
            EnableSelectedUserCommand.NotifyCanExecuteChanged();
            RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        });

        _dataStore.UiSelectedNodeView.NodeSelected().Sub(node =>
        {
            _ = node;
            EnableSelectedUserCommand.NotifyCanExecuteChanged();
            RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        });
    }

    // public void Dispose()
    // {
    //     NodeDataEditor.Dispose();
    //     NodeSelection.Dispose();
    //     LayerLevelSelection.Dispose();
    //     _dataStore.Dispose();
    // }
    private LayerLevel LayerLevel
    {
        get => LayerLevelSelection.LayerLevel;
        set => LayerLevelSelection.LayerLevel = value;
    }
    private UiNode SelectedUiNode => _dataStore.UiSelectedNodeView.Value;

    public bool CanSelectUser => LayerLevel is LayerLevel.UiUser or LayerLevel.ProgrammableUser;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddUserWithTypedNameCommand))]
    public partial string UserNameToAdd { get; set; } = "";

    public bool CanSelectUserToAdd => true;

    private Name? ParseUserNameToAdd()
    {
        var parser = new Parser(UserNameToAdd);
        Name? name = NameHelper.TryParseName(ref parser);
        return name;
    }

    public bool CanAddUser => ParseUserNameToAdd() != null;

    [RelayCommand(CanExecute = nameof(CanAddUser))]
    public void AddUserWithTypedName()
    {
        var name = ParseUserNameToAdd();
        if (name is null)
        {
            Debug.Fail("Parsed name was null");
            return;
        }
        AddUser(name);
    }

    private void AddUser(Name name)
    {
        _updateTreeHelper.ExecTreeAction(() =>
        {
            var layer = Tree.Defaults.CreateUiLayer();
            var val = layer.Builder<TeacherLayerConfig>().Value();
            val.TeacherName = name;
            return new([Tree.BaseNode, layer.Node]);
        });

        UserNameToAdd = "";
        LayerLevel = LayerLevel.UiUser;
    }

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
        var s = _settings.Value;
        await using var output = File.OpenWrite(s.DatabaseFilePath);
        await _serializer.SerializeUiLayers(output, Tree).ConfigureAwait(false);
        ExplorerHelper.TryOpenExplorerAndSelectFile(s.DatabaseFilePath);
    }

    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await _updateTreeHelper.ExecTreeActionAsync(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializer.DeserializeUiLayers(output, Tree).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(
                () => _dataStore.TreeStructureChanged.Invoke());
            return default;
        });
    }
}
