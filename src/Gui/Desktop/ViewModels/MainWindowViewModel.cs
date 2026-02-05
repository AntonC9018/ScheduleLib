using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly TreeBuilder _configBuilder;
    private readonly ConfigSerializationHelper _serializationHelper;
    private readonly SelectedUserNodeViewModel _nodeSelection;

    public MainWindowViewModel(
        TreeBuilder configBuilder,
        ConfigSerializationHelper serializationHelper)
    {
        _configBuilder = configBuilder;
        _serializationHelper = serializationHelper;

        _nodeSelection = new(configBuilder);
        _nodeSelection.PropertyChanged += (o, args) =>
        {
            _ = o;
            if (args.PropertyName == nameof(_nodeSelection.Model))
            {
                OnUserNodeSelectionChanged();
                AttachUserSelectedChanged();
            }
        };
        AttachUserSelectedChanged();
    }

    private void AttachUserSelectedChanged()
    {
        _nodeSelection.Model.PropertyChanged += (o, args) =>
        {
            Debug.Assert(args.PropertyName == nameof(_nodeSelection.Model.SelectedNode));
            _ = o;
            _ = args;
            OnUserSelectedChanged();
        };
    }

    private void OnUserSelectedChanged()
    {
        RemoveSelectedUserCommand.NotifyCanExecuteChanged();
        EnableSelectedUserCommand.NotifyCanExecuteChanged();
    }

    private void OnUserNodeSelectionChanged()
    {
        OnPropertyChanged(nameof(UserNodeSelection));
        OnUserSelectedChanged();
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
            return new WrappedNode(layer);
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
            if (_nodeSelection.SelectedNode.IsNull)
            {
                return false;
            }
            if (_nodeSelection.SelectedNode.IsUiLayer)
            {
                return true;
            }
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedUser))]
    public void RemoveSelectedUser()
    {
        if (_nodeSelection.SelectedNode.IsNull
            || !_nodeSelection.SelectedNode.IsUiLayer)
        {
            Debug.Fail("Cannot remove this layer");
            return;
        }
        _nodeSelection.ExecTreeAction(() =>
        {
            UiLayerHelper.MaybeRemoveLayer(_nodeSelection.SelectedNode.Leaf.Node, _configBuilder);
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
            if (_nodeSelection.SelectedNode.IsNull)
            {
                return false;
            }
            if (_nodeSelection.SelectedNode.IsUiLayer)
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnableSelectedUser))]
    public void EnableSelectedUser()
    {
        if (_nodeSelection.SelectedNode.IsNull)
        {
            return;
        }
        _nodeSelection.ExecTreeAction(() =>
        {
            var b = _nodeSelection.SelectedNode.Leaf.MaybeCreateUiLayer(_nodeSelection.SelectedNode.Marker);
            return new(b);
        });
        LayerLevel = LayerLevel.UiUser;
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        await _nodeSelection.ExecTreeAction(async () =>
        {
            await using var output = File.OpenWrite("ui-layers.json");
            await _serializationHelper.SerializeUiLayers(output, _configBuilder);
            ExplorerHelper.TryOpenExplorerAndSelectFile("ui-layers.json");
            return null;
        });
    }
    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await _nodeSelection.ExecTreeAction(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializationHelper.DeserializeUiLayers(output, _configBuilder);
            return null;
        });
    }
}

public sealed record class WrappedNode
{
    public readonly NodeBuilder Leaf;
    public WrappedNode(NodeBuilder leaf)
    {
        Leaf = leaf;
    }

    public static readonly WrappedNode Null = new(default(NodeBuilder));
    public bool IsNull => Leaf.IsNull;
    public TeacherLayerConfig Marker => Leaf.Node.Get(TeacherLayerConfig.Key).Value.GetValue()!;
    public Name Name => Marker.TeacherName;
    public bool IsUiLayer => Leaf.Node.IsUiLayer();
    public override string ToString() => IsNull ? "No User" : Name.ToString();
}

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}
