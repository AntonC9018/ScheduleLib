using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly TreeBuilder _configBuilder;
    private readonly TreeSerializer _serializer;
    internal readonly DataStore _dataStore;
    private readonly UpdateTreeHelper _updateTreeHelper;

    public NodeDataEditorViewModel NodeDataEditor { get; }
    public UiNodeSelectionViewModel NodeSelection { get; }
    public LayerLevelSelectionViewModel LayerLevelSelection { get; }

    public MainWindowViewModel(
        TreeBuilder configBuilder,
        TreeSerializer serializer,
        IServiceProvider sp)
    {
        _configBuilder = configBuilder;
        _serializer = serializer;
        _dataStore = DataStore.Create(configBuilder);
        _updateTreeHelper = new(_dataStore.SelectedNodePath, configBuilder);

        // We own the instance, not the SP
        NodeDataEditor = ActivatorUtilities.CreateInstance<NodeDataEditorViewModel>(sp, [_dataStore]);
        NodeSelection = new(
            configBuilder,
            _dataStore.TreeStructureChanged,
            _dataStore.UiSelectedNodeView);
        LayerLevelSelection = new(_dataStore.SelectedNodePath);

        LayerLevelSelection.LayerLevelChanged.Sub(layer =>
        {
            _ = layer;
            OnPropertyChanged(nameof(CanSelectUser));
            OnPropertyChanged(nameof(CanSelectUserToAdd));
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
            var layer = _configBuilder.Defaults.CreateUiLayer();
            var val = layer.Builder<TeacherLayerConfig>().Value();
            val.TeacherName = name;
            return new([_configBuilder.BaseNode, layer.Node]);
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
            UiLayerHelper.MaybeRemoveLayer(SelectedUiNode.Leaf.Node, _configBuilder);
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
        var file = "ui-layers.json";
        await using var output = File.OpenWrite(file);
        await _serializer.SerializeUiLayers(output, _configBuilder).ConfigureAwait(false);
        ExplorerHelper.TryOpenExplorerAndSelectFile(file);
    }

    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await _updateTreeHelper.ExecTreeActionAsync(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializer.DeserializeUiLayers(output, _configBuilder).ConfigureAwait(false);
            return default;
        });
        _dataStore.TreeStructureChanged.Invoke();
    }
}
