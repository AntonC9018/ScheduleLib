using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.MvvmEssentials;
using Desktop.NodeData.Common;
using Desktop.ViewModelData;

namespace Desktop.NodeData.Editor;

public sealed partial class NodeDataEditorViewModel : ViewModelBase, IDisposable
{
    private readonly DataStore _dataStore;
    private readonly NodeDataViewModelResolver _modelResolver;

    public NodeDataEditorViewModel(
        DataStore dataStore,
        NodeDataViewModelResolver modelResolver,
        ConfigTypesProvider configTypesProvider)
        : base(dataStore.TreeContext.Dispatcher)
    {
        _dataStore = dataStore;
        _modelResolver = modelResolver;
        ConfigTypes = configTypesProvider.ConfigTypes
            .Where(x => modelResolver.Supported.Contains(x.Key)).ToArray();
        CurrentConfigType = ConfigTypes.FirstOrDefault();
    }

    private NullableOwnedViewModel _selectedViewModel;
    public ObservableObject? SelectedNodeEditorViewModel => _selectedViewModel.Value;
    public IReadOnlyList<ConfigType> ConfigTypes { get; }

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
    public required PropertySetId Key { get; init; }
    public required string DisplayName { get; init; }
    // public required Type Type { get; init; }

    public override string ToString() => DisplayName;
}

public sealed class ConfigTypesProvider
{
    // public ConfigType[] ConfigTypes => field ??= NodeDataKey.Registry.KeyTypeMappings.Select(
    //     x => new ConfigType
    //     {
    //         // TODO: Add a custom name here, will do when doing localizations.
    //         Key = x.Key,
    //         Type = x.Value,
    //         DisplayName = x.Key.Value,
    //     }).ToArray();
    public ConfigType[] ConfigTypes = [new()
    {
        DisplayName = "Online registry",
        Key = Desktop.NodeData.Features.Registry.RegistryEditorRegistration.EditorKey,
    }];
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

