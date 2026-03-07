using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Desktop.ViewModels;

public sealed partial class NodeDataEditorViewModel : ViewModelBase, IDisposable
{
    private readonly DataStore _user;
    private readonly NodeDataViewModelResolver _modelResolver;

    public static ConfigType[] CachedConfigTypes => field ??= NodeDataKey.Registry.KeyTypeMappings.Select(
        x => new ConfigType
        {
            Key = x.Key,
            Type = x.Value,
        }).ToArray();

    public NodeDataEditorViewModel(
        DataStore dataStore,
        NodeDataViewModelResolver modelResolver)
        : base(dataStore.TreeContext.Dispatcher)
    {
        _user = dataStore;
        _modelResolver = modelResolver;
        ConfigTypes = CachedConfigTypes.Where(x => modelResolver.Supported.Contains(x.Key)).ToArray();
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
            _selectedViewModel = _modelResolver.Resolve(value.Key, _user);
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
    public required Type Type { get; init; }

    public override string ToString() => Key.Value.ToString();
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

