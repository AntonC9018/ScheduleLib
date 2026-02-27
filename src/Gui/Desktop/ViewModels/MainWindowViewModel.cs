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

public sealed partial class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly TreeBuilder _configBuilder;
    private readonly TreeSerializer _serializationHelper;
    internal readonly DataStore _dataStore;

    public NodeDataEditorViewModel NodeDataEditor { get; }

    public MainWindowViewModel(
        TreeBuilder configBuilder,
        TreeSerializer serializationHelper,
        IServiceProvider sp)
    {
        _configBuilder = configBuilder;
        _serializationHelper = serializationHelper;
        _dataStore = DataStore.Create(configBuilder);

        // We own the instance, not the SP
        NodeDataEditor = ActivatorUtilities.CreateInstance<NodeDataEditorViewModel>(sp, [_nodeSelection]);
    }

    public void Dispose()
    {
        NodeDataEditor.Dispose();
    }

    public UserNodeSelectionModel UserNodeSelection => _nodeSelection.Model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectUser))]
    [NotifyPropertyChangedFor(nameof(CanSelectUserToAdd))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedUserCommand))]
    public partial LayerLevel LayerLevel { get; set; } = LayerLevel.Default;
    public EnumMembers<LayerLevel> AllLayerLevels => new();

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
        _nodeSelection.ExecTreeAction(() =>
        {
            var layer = _configBuilder.Defaults.CreateUiLayer();
            var val = layer.Builder<TeacherLayerConfig>().Value();
            val.TeacherName = name;
            return new UiNode(layer);
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
            if (_nodeSelection.SelectedUiNode.IsNull)
            {
                return false;
            }
            if (_nodeSelection.SelectedUiNode.IsUiLayer)
            {
                return true;
            }
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedUser))]
    public void RemoveSelectedUser()
    {
        if (_nodeSelection.SelectedUiNode.IsNull
            || !_nodeSelection.SelectedUiNode.IsUiLayer)
        {
            Debug.Fail("Cannot remove this layer");
            return;
        }
        _nodeSelection.ExecTreeAction(() =>
        {
            UiLayerHelper.MaybeRemoveLayer(_nodeSelection.SelectedUiNode.Leaf.Node, _configBuilder);
            return null;
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
            var n = _nodeSelection.SelectedUiNode;
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
        if (_nodeSelection.SelectedUiNode.IsNull)
        {
            return;
        }
        _nodeSelection.ExecTreeAction(() =>
        {
            var b = _nodeSelection.SelectedUiNode.Leaf.MaybeCreateUiLayer(_nodeSelection.SelectedUiNode.Marker);
            return new(b);
        });
        LayerLevel = LayerLevel.UiUser;
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        var file = "ui-layers.json";
        await using var output = File.OpenWrite(file);
        await _serializationHelper.SerializeUiLayers(output, _configBuilder).ConfigureAwait(false);
        ExplorerHelper.TryOpenExplorerAndSelectFile(file);
    }
    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await _nodeSelection.ExecTreeAction(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializationHelper.DeserializeUiLayers(output, _configBuilder).ConfigureAwait(false);
            return null;
        });
    }
}

public sealed record class UiNode
{
    public readonly NodeBuilder Leaf;
    public UiNode(NodeBuilder leaf)
    {
        Leaf = leaf;
    }

    public static readonly UiNode Null = new(default(NodeBuilder));
    public bool IsNull => Leaf.IsNull;
    public bool IsEditable => !IsNull && IsUiLayer;
    public TeacherLayerConfig Marker => Leaf.Node.Get(TeacherLayerConfig.Key).Value.GetValue()!;
    public Name Name => Marker.TeacherName;
    public bool IsUiLayer => Leaf.Node.IsOnEditableLayer();
    public override string ToString() => IsNull ? "No User" : Name.ToString();
}

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}
