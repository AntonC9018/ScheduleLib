using System.Diagnostics;
using Anton.LayeredConfig;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;

namespace Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApplicationConfigBuilder _configBuilder;
    private readonly ConfigSerializationHelper _serializationHelper;

#pragma warning disable CS9264 // Non-nullable property must contain a non-null value when exiting constructor. Consider adding the 'required' modifier, or declaring the property as nullable, or adding '[field: MaybeNull, AllowNull]' attributes.
    public MainWindowViewModel(
        ApplicationConfigBuilder configBuilder,
        ConfigSerializationHelper serializationHelper)
    {
        _configBuilder = configBuilder;
        _serializationHelper = serializationHelper;
        ResetUserLayers();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectUser))]
    [NotifyPropertyChangedFor(nameof(CanSelectUserToAdd))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedUserCommand))]
    public partial LayerLevel LayerLevel { get; set; } = LayerLevel.Default;
    public EnumMembers<LayerLevel> AllLayerLevels => new();

    public bool CanSelectUser => LayerLevel is LayerLevel.UiUser or LayerLevel.ProgrammableUser;
    [ObservableProperty]
    public partial object[] UserLayers { get; private set; }
    private void ResetUserLayers()
    {
        UserLayers = new[]
            {
                NoUser,
            }
            .Concat(
                _configBuilder
                    .GetMarkerLayers()
                    .Select(x => (object) new WrappedLayer(x.Builder)))
            .ToArray();
    }

    private const string NoUser = "No User";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedUserCommand))]
    public partial object SelectedUserLayer { get; set; } = NoUser;

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
        var layer = _configBuilder.Defaults.CreateUiLayer();
        var val = layer.Builder<TeacherLayerConfig>().Value();
        val.TeacherName = name;
        ResetUserLayers();
        UserNameToAdd = "";
        SelectedUserLayer = new WrappedLayer(layer);
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
            if (SelectedUserLayer is not WrappedLayer w)
            {
                return false;
            }
            if (w.IsUiLayer)
            {
                return true;
            }
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedUser))]
    public void RemoveSelectedUser()
    {
        if (SelectedUserLayer is not WrappedLayer selectedUser
            || !selectedUser.IsUiLayer)
        {
            Debug.Fail("Cannot remove this layer");
            return;
        }
        ExecTreeAction(() =>
        {
            UiLayerHelper.MaybeRemoveLayer(selectedUser.Leaf.Layer, _configBuilder);
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
            if (SelectedUserLayer is not WrappedLayer user)
            {
                return false;
            }
            if (user.IsUiLayer)
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnableSelectedUser))]
    public void EnableSelectedUser()
    {
        if (SelectedUserLayer is not WrappedLayer user)
        {
            return;
        }
        ExecTreeAction(() =>
        {
            user.MaybeInitUiLayer();
        });
        LayerLevel = LayerLevel.UiUser;
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        await ExecTreeAction(async () =>
        {
            await using var output = File.OpenWrite("ui-layers.json");
            await _serializationHelper.SerializeUiLayers(output, _configBuilder);
            ExplorerHelper.TryOpenExplorerAndSelectFile("ui-layers.json");
        });
    }
    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await ExecTreeAction(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializationHelper.DeserializeUiLayers(output, _configBuilder);
        });
    }

    private void ExecTreeAction(Action change)
    {
        ExecTreeAction(() =>
        {
            change();
            return ValueTask.CompletedTask;
        }).EnsureCompletedSync();
    }

    private async ValueTask ExecTreeAction(Func<ValueTask> change)
    {
        TeacherLayerConfig? marker = null;
        if (SelectedUserLayer is WrappedLayer selectedUser)
        {
            marker = selectedUser.Marker;
        }
        await change();
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            ResetUserLayers();
            if (marker != null)
            {
                SelectedUserLayer = UserLayers
                    .Where(x => x is WrappedLayer w
                        && EqualityComparer<TeacherLayerConfig>.Default.Equals(w.Marker, marker))
                    .FirstOrDefault(NoUser);
            }
            else
            {
                SelectedUserLayer = NoUser;
            }
        }
    }
}

public sealed record class WrappedLayer
{
    public readonly ApplicationConfigLayerBuilder Leaf;
    public WrappedLayer(ApplicationConfigLayerBuilder leaf)
    {
        Leaf = leaf;
    }

    public TeacherLayerConfig Marker => Leaf.Layer.GetConfig(TeacherLayerConfig.Key).Value.GetValue()!;
    public Name Name => Marker.TeacherName;
    public bool IsUiLayer => Leaf.Layer.IsUiLayer();
    public void MaybeInitUiLayer() => Leaf.MaybeCreateUiLayer(Marker);
    public override string ToString() => Name.ToString();
}

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}
