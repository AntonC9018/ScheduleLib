using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;

namespace Desktop.ViewModels;

public enum LayerLevel
{
    Default,
    ProgrammableUser,
    UiUser,
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

    private readonly EventSource<LayerLevel> _layerLevelChanged = new();
    public EventSource<LayerLevel> LayerLevelChanged => _layerLevelChanged;

    private readonly EventSubscription<Layer> _layerChangedSub;
    private readonly SelectedNodePathModel _path;

    public LayerLevelSelectionViewModel(SelectedNodePathModel path)
    {
        _path = path;
        _layerChangedSub = path.SelectedLayer.Changed.Sub(layer =>
        {
            LayerLevel = _mappings.FindKeyOrDefault(layer, LayerLevel.Default);
        });
    }

    partial void OnLayerLevelChanged(LayerLevel value)
    {
        _path.SelectedLayer.Set(_mappings[value]);
        _layerLevelChanged.Invoke(value);
    }

    public void Dispose() => _layerChangedSub.Dispose();
}
