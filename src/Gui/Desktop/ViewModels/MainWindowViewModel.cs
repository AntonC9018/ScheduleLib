using System.Diagnostics;
using Anton.LayeredConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core.Config.Impl.Impl;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Lesson;

namespace Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApplicationConfigBuilder _configBuilder;

    public MainWindowViewModel(ApplicationConfigBuilder configBuilder)
    {
        _configBuilder = configBuilder;
        ResetUserLayers();
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSelectUser))]
    [NotifyPropertyChangedFor(nameof(CanSelectUserToAdd))]
    [NotifyCanExecuteChangedFor(nameof(RemoveSelectedUserCommand))]
    public partial LayerLevel LayerLevel { get; set; } = LayerLevel.Default;
    public EnumMembers<LayerLevel> AllLayerLevels => new();

    public bool CanSelectUser => LayerLevel == LayerLevel.User;
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

    public bool CanSelectUserToAdd => LayerLevel == LayerLevel.User;

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
    }

    public bool CanRemoveSelectedUser
    {
        get
        {
            if (!CanSelectUser)
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
        UiLayerHelper.MaybeRemoveLayer(selectedUser.Leaf.Layer, _configBuilder);
        ResetUserLayers();
        SelectedUserLayer = NoUser;
    }

    public bool CanEnableSelectedUser
    {
        get
        {
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
        var newLayer = user.MaybeInitUiLayer();
        ResetUserLayers();
        SelectedUserLayer = newLayer;
    }
}

internal static class UiLayerHelper
{
    public static readonly LayerName UiTeacherLayer = LayerName.Create("User-UI");

    public static bool IsUiLayer(this MutableLayer layer)
    {
        return layer.Name == UiTeacherLayer;
    }

    public static IEnumerable<ApplicationConfigLayerBuilder> GetUiLayersForAll(
        this ApplicationConfigBuilder builder)
    {
        foreach (var x in builder.GetMarkerLayers())
        {
            var ret = MaybeCreateUiLayer(x.Builder, x.Config);
            yield return ret;
        }
    }

    public static IEnumerable<ApplicationConfigLayerBuilder> GetUiLayers(
        this ApplicationConfigBuilder builder)
    {
        foreach (var x in builder.GetMarkerLayers())
        {
            if (x.Builder.Layer.IsUiLayer())
            {
                yield return x.Builder;
            }
        }
    }

    extension(ApplicationConfigLayerBuilder builder)
    {
        public ApplicationConfigLayerBuilder CreateUiLayer()
        {
            Debug.Assert(!builder.Layer.IsUiLayer());
            var b = builder.AddLayer(UiTeacherLayer);
            b.Builder(TeacherLayerConfig.Key).Enable();
            return b;
        }

        public ApplicationConfigLayerBuilder MaybeCreateUiLayer()
        {
            if (!builder.Layer.IsUiLayer())
            {
                return builder.CreateUiLayer();
            }
            return builder;
        }

        public ApplicationConfigLayerBuilder MaybeCreateUiLayer(TeacherLayerConfig config)
        {
            var b = MaybeCreateUiLayer(builder);
            b.Builder(TeacherLayerConfig.Key).CopyValue(config);
            return b;
        }
    }

    public static void MaybeRemoveLayer(
        MutableLayer layerToRemove,
        ApplicationConfigBuilder root)
    {
        root.RemoveLayers(layer =>
        {
            return ReferenceEquals(layerToRemove, layer);
            // if (!layer.IsUiLayer())
            // {
            //     return false;
            // }
            // var config1 = layer.GetConfig(TeacherLayerConfig.Key);
            // if (!config1.Exists)
            // {
            //     return false;
            // }
            // var value = config1.Value.GetValue();
            // if (value is null)
            // {
            //     return false;
            // }
            // if (EqualityComparer<Name>.Default.Equals(value.TeacherName, name))
            // {
            //     return true;
            // }
            // return false;
        });
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
    public WrappedLayer MaybeInitUiLayer() => new(Leaf.MaybeCreateUiLayer(Marker));
    public override string ToString() => Name.ToString();
}

public enum LayerLevel
{
    Default,
    User,
}
