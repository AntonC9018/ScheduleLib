using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anton.LayeredConfig;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Newtonsoft.Json;
using ScheduleLib.Application.Core.Config.Impl.Impl;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Desktop.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ApplicationConfigBuilder _configBuilder;
    private readonly ConfigSerializationHelper _serializationHelper;

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
        var marker = selectedUser.Marker;
        UiLayerHelper.MaybeRemoveLayer(selectedUser.Leaf.Layer, _configBuilder);
        ResetUserLayers();
        SelectedUserLayer = UserLayers
            .Where(x => x is WrappedLayer w
                && EqualityComparer<TeacherLayerConfig>.Default.Equals(w.Marker, marker))
            .FirstOrDefault(NoUser);
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
        var newLayer = user.MaybeInitUiLayer();
        ResetUserLayers();
        SelectedUserLayer = newLayer;
        LayerLevel = LayerLevel.UiUser;
    }

    [RelayCommand]
    public async Task SerializeUiLayers()
    {
        await using var output = File.OpenWrite("ui-layers.json");
        await _serializationHelper.SerializeUiLayers(output, _configBuilder);
        ExplorerHelper.TryOpenExplorerAndSelectFile("ui-layers.json");

        ResetUserLayers();
    }
    [RelayCommand]
    public async Task DeserializeUiLayers()
    {
        await using var output = File.OpenRead("ui-layers.json");
        await _serializationHelper.DeserializeUiLayers(output, _configBuilder);
    }
}

internal static class UiLayerHelper
{
    public static readonly LayerName UiTeacherLayer = LayerName.Create("User-UI");

    public static bool IsUiLayer(this MutableLayer layer)
    {
        return layer.Name == UiTeacherLayer;
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
            if (ReferenceEquals(layerToRemove, layer))
            {
                return true;
            }
            return false;
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

    extension(ConfigSerializationHelper helper)
    {
        public async Task SerializeUiLayers(
            Stream output,
            ApplicationConfigBuilder configRoot)
        {
            var uiLayers = configRoot.GetUiLayers().Select(x => x.Layer);
            await helper.SerializeValues(uiLayers, output, serializeLayerName: false);
            output.SetLength(output.Position);
        }

        public async Task DeserializeUiLayers(
            Stream output,
            ApplicationConfigBuilder configRoot)
        {
            await helper.DeserializeValues(
                output,
                (layerName, configs) =>
                {
                    {
                        if (layerName is { } x && x != UiTeacherLayer)
                        {
                            throw new InvalidOperationException($"Expected a layer with name {UiTeacherLayer}");
                        }
                    }
                    {
                        var marker = (TeacherLayerConfig) configs.Find(x => x.Key == TeacherLayerConfig.Key).Value;
                        var markerLayer = configRoot
                            .GetMarkerLayers()
                            .FirstOrDefault(x => x.Config.Equals(marker))
                            .Builder;

                        if (markerLayer.IsNull)
                        {
                            markerLayer = configRoot.Defaults;
                        }

                        var ret = markerLayer.MaybeCreateUiLayer();
                        return ret;
                    }
                });
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
    public WrappedLayer MaybeInitUiLayer() => new(Leaf.MaybeCreateUiLayer(Marker));
    public override string ToString() => Name.ToString();
}

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}
