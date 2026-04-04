using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;

namespace Desktop.NodeData.Editor;

public sealed partial class NodeDataEditorViewModel(
        DataStore _dataStore,
        NodeDataViewModelResolver _modelResolver,
        ConfigTypesProvider _configTypesProvider)
    : ViewModelBase(_dataStore.TreeContext.Dispatcher), IDisposable
{
    private NullableOwnedViewModel _selectedViewModel;
    public ObservableObject? SelectedNodeEditorViewModel => _selectedViewModel.Value;
    public IReadOnlyList<ConfigType> ConfigTypes => field ??= _configTypesProvider
        .ConfigTypes
        .Where(x => _modelResolver.Supported.Contains(x.Key)).ToArray();

    [ObservableProperty]
    public partial ConfigType? CurrentConfigType { get; set; }

    partial void OnCurrentConfigTypeChanged(ConfigType? value)
    {
        var oldValue = _selectedViewModel;
        if (value == null)
        {
            _selectedViewModel = NullableOwnedViewModel.Null;
        }
        else
        {
            _selectedViewModel = _modelResolver.Resolve(value.Key, _dataStore);
        }

        try
        {
            OnPropertyChanged(nameof(SelectedNodeEditorViewModel));
        }
        finally
        {
            oldValue.Dispose();
        }
    }

    public void Dispose() => _selectedViewModel.Dispose();
}

public sealed class ConfigType
{
    public required NodeDataKey Key { get; init; }
    public required string DisplayName { get; init; }
    public required Type Type { get; init; }

    public override string ToString() => Key.Value.ToString();
}

public sealed class ConfigTypesProvider
{
    public ConfigType[] ConfigTypes => field ??= NodeDataKey.Registry.KeyTypeMappings.Select(
        x => new ConfigType
        {
            // TODO: Add a custom name here, will do when doing localizations.
            Key = x.Key,
            Type = x.Value,
            DisplayName = x.Key.Value,
        }).ToArray();
}

public readonly struct NullableOwnedViewModel : IDisposable
{
    private readonly OwnedViewModel _model;

    public NullableOwnedViewModel(OwnedViewModel model) => _model = model;
    public static NullableOwnedViewModel Null => new(default);
    public static implicit operator NullableOwnedViewModel(OwnedViewModel model) => new(model);

    public bool IsNull => _model.IsNull;

    public void Dispose()
    {
        if (_model.IsNull)
        {
            return;
        }
        _model.Dispose();
    }

    public ObservableObject? Value
    {
        get
        {
            if (_model.IsNull)
            {
                return null;
            }
            return _model.Value;
        }
    }
}

