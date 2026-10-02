using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;

namespace Desktop.ViewModelData;

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
}

public sealed record LayerLevelChoice(LayerLevel Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class LayerLevelSelectionViewModel : ViewModelBase, IDisposable
{
    private static readonly OneForEachEnumMemberArray<LayerLevel, Layer> _mappings;
    static LayerLevelSelectionViewModel()
    {
        _mappings = new();
        _mappings[LayerLevel.Default] = Layer.DefaultLayer;
        _mappings[LayerLevel.UiUser] = UiLayerHelper.UiTeacherLayer;
        _mappings[LayerLevel.ProgrammableUser] = LayerKeys.TeacherLayerKey;
        Debug.Assert(_mappings.All(x => x.Value != default));
    }

    [ObservableProperty]
    public partial LayerLevel LayerLevel { get; set; } = LayerLevel.Default;
    public EnumMembers<LayerLevel> AllLayerLevels => new();
    public IReadOnlyList<LayerLevelChoice> LayerChoices { get; } =
    [
        new(LayerLevel.Default, "Application defaults"),
        new(LayerLevel.ProgrammableUser, "Configured teachers"),
        new(LayerLevel.UiUser, "UI settings"),
    ];
    public LayerLevelChoice SelectedChoice
    {
        get => LayerChoices.First(x => x.Value == LayerLevel);
        set
        {
            if (value is not null)
            {
                LayerLevel = value.Value;
            }
        }
    }

    private readonly EventSource<LayerLevel> _layerLevelChanged;
    public Event<LayerLevel> LayerLevelChanged => _layerLevelChanged;

    private readonly EventSubscription<Layer> _layerChangedSub;
    private readonly SelectedNodePathModel _path;

    public LayerLevelSelectionViewModel(
        TreeEventDispatcher dispatcher,
        SelectedNodePathModel path)
        : base(dispatcher)
    {
        _layerLevelChanged = dispatcher.CreateEvent<LayerLevel>();
        _path = path;
        _layerChangedSub = path.SelectedLayer.Changed.Sub(layer =>
        {
            LayerLevel = _mappings.FindKeyOrDefault(layer, LayerLevel.Default);
        });
    }

    partial void OnLayerLevelChanged(LayerLevel value)
    {
        OnPropertyChanged(nameof(SelectedChoice));
        _path.SelectedLayer.Set(_mappings[value]);
        _layerLevelChanged.Invoke(value);
    }

    public void Dispose() => _layerChangedSub.Dispose();
}
