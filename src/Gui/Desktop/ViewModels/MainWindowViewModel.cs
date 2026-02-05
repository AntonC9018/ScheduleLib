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
#pragma warning enable CS9264 // Non-nullable property must contain a non-null value when exiting constructor. Consider adding the 'required' modifier, or declaring the property as nullable, or adding '[field: MaybeNull, AllowNull]' attributes.

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectUser))]
    [NotifyPropertyChangedFor(nameof(CanSelectUserToAdd))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedUserCommand))]
    public partial LayerLevel LayerLevel { get; set; } = LayerLevel.Default;
    public EnumMembers<LayerLevel> AllLayerLevels => new();

    public bool CanSelectUser => LayerLevel is LayerLevel.UiUser or LayerLevel.ProgrammableUser;
    [ObservableProperty]
    public partial WrappedLayer[] UserLayers { get; private set; }
    private void ResetUserLayers()
    {
        UserLayers = new[]
            {
                WrappedLayer.Null,
            }
            .Concat(
                _configBuilder
                    .GetMarkerLayers()
                    .Select(x => new WrappedLayer(x.Builder)))
            .ToArray();
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableSelectedUserCommand))]
    public partial WrappedLayer SelectedUserLayer { get; set; } = WrappedLayer.Null;

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
        ExecTreeAction(() =>
        {
            var layer = _configBuilder.Defaults.CreateUiLayer();
            var val = layer.Builder<TeacherLayerConfig>().Value();
            val.TeacherName = name;
            return new WrappedLayer(layer);
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
            if (SelectedUserLayer.IsNull)
            {
                return false;
            }
            if (SelectedUserLayer.IsUiLayer)
            {
                return true;
            }
            return false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemoveSelectedUser))]
    public void RemoveSelectedUser()
    {
        if (SelectedUserLayer.IsNull
            || !SelectedUserLayer.IsUiLayer)
        {
            Debug.Fail("Cannot remove this layer");
            return;
        }
        ExecTreeAction(() =>
        {
            UiLayerHelper.MaybeRemoveLayer(SelectedUserLayer.Leaf.Layer, _configBuilder);
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
            if (SelectedUserLayer.IsNull)
            {
                return false;
            }
            if (SelectedUserLayer.IsUiLayer)
            {
                return false;
            }
            return true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanEnableSelectedUser))]
    public void EnableSelectedUser()
    {
        if (SelectedUserLayer.IsNull)
        {
            return;
        }
        ExecTreeAction(() =>
        {
            var b = SelectedUserLayer.Leaf.MaybeCreateUiLayer(SelectedUserLayer.Marker);
            return new(b);
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
            return null;
        });
    }
    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await ExecTreeAction(async () =>
        {
            await using var output = File.OpenRead("ui-layers.json");
            await _serializationHelper.DeserializeUiLayers(output, _configBuilder);
            return null;
        });
    }

    private void ExecTreeAction(Func<WrappedLayer?> change)
    {
        ExecTreeAction(() =>
        {
            var ret = change();
            return ValueTask.FromResult(ret);
        }).EnsureCompletedSync();
    }

    private async ValueTask ExecTreeAction(Func<ValueTask<WrappedLayer?>> change)
    {
        TeacherLayerConfig? marker = null;
        if (!SelectedUserLayer.IsNull)
        {
            marker = SelectedUserLayer.Marker;
        }
        var selectedLayer = await change();
        await Dispatcher.UIThread.InvokeSyncFallingBackToAsync(Continue);

        void Continue()
        {
            ResetUserLayers();
            if (selectedLayer != null)
            {
                Debug.Assert(UserLayers.Contains(selectedLayer));
                SelectedUserLayer = selectedLayer;
            }
            else if (marker != null)
            {
                SelectedUserLayer = UserLayers
                    .Where(x => EqualityComparer<TeacherLayerConfig>.Default.Equals(x.Marker, marker))
                    .FirstOrDefault(WrappedLayer.Null);
            }
            else
            {
                SelectedUserLayer = WrappedLayer.Null;
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

    public static readonly WrappedLayer Null = new(default(ApplicationConfigLayerBuilder));
    public bool IsNull => Leaf.IsNull;
    public TeacherLayerConfig Marker => Leaf.Layer.GetConfig(TeacherLayerConfig.Key).Value.GetValue()!;
    public Name Name => Marker.TeacherName;
    public bool IsUiLayer => Leaf.Layer.IsUiLayer();
    public override string ToString() => IsNull ? "No User" : Name.ToString();
}

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}
