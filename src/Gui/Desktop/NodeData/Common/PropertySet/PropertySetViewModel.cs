using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Desktop.MvvmEssentials;
using Desktop.NodeData.Features.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

public sealed record PropertyChoice(string Name, object? Value, bool IsDefault = false)
{
    public override string ToString() => Name;
}

internal sealed record PropertyChoices(IReadOnlyList<PropertyChoice> Values, Func<object?, object?, bool> Equal)
{
    public static PropertyChoices From<T>(IEnumerable<Named<T>> values, IEqualityComparer<T> comparer) => new(
        values.Select(x => new PropertyChoice(x.Name, x.Value)).ToArray(),
        (left, right) => left is null || right is null ? left is null && right is null : comparer.Equals((T)left, (T)right));

    public static PropertyChoices FromRegistry(IServiceProvider sp, Type type)
    {
        var factory = typeof(PropertyChoices).GetMethod(nameof(ResolveRegistry), BindingFlags.Static | BindingFlags.NonPublic)!
            .MakeGenericMethod(type).CreateDelegate<Func<IServiceProvider, PropertyChoices>>();
        return factory(sp);
    }

    private static PropertyChoices ResolveRegistry<T>(IServiceProvider sp)
    {
        var registry = sp.GetRequiredService<Registry<T>>();
        return From(registry.Values, registry.Comparer);
    }
}

internal interface IPropertySetSource : IDisposable
{
    object? Data { get; }
    object? EditableData { get; }
    bool IsEditable { get; }
}
internal sealed class PropertySetSource<T> : IPropertySetSource where T : class
{
    private readonly NodeDataAccessor<T> _accessor;
    private bool _disposed;
    public PropertySetSource(NodeDataVMCreateParams p, NodeDataKey<T> key) => _accessor = new(
        p.DataStore.TreeContext.Tree, p.DataStore.SelectedNodePath, key, p.DataStore.NodeDataChangeDispatcher.DataChanged);
    public object? Data
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _accessor.Data; }
    }
    public object? EditableData
    {
        get { ObjectDisposedException.ThrowIf(_disposed, this); return _accessor.EditableData; }
    }
    public bool IsEditable => !_disposed && _accessor.IsEditable;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _accessor.Dispose();
    }
}

public abstract class PropertySetItemViewModel : ObservableObject
{
    public abstract string? Name { get; }
    public Type? ViewType { get; internal init; }
    public abstract bool IsEditable { get; }
    internal abstract void Refresh();
}

public class PropertySetGroupViewModel : PropertySetItemViewModel
{
    private readonly IPropertySetSource? _source;
    public override string? Name { get; }
    public IReadOnlyList<PropertySetItemViewModel> Items { get; }
    public IEnumerable<PropertyValueViewModel> Properties => Items.SelectMany(item => item is PropertyValueViewModel property
        ? [property] : ((PropertySetGroupViewModel)item).Properties);
    internal PropertySetGroupViewModel(string? name, Type? viewType, IPropertySetSource? source,
        IReadOnlyList<PropertySetItemViewModel> items)
    {
        Name = name;
        ViewType = viewType;
        _source = source;
        Items = items;
    }
    public override bool IsEditable => _source?.IsEditable ?? Items.Any(x => x.IsEditable);
    internal override void Refresh()
    {
        foreach (var item in Items) item.Refresh();
        OnPropertyChanged(nameof(IsEditable));
    }
}

public sealed class PropertySetViewModel : PropertySetGroupViewModel, IDisposable
{
    private readonly IReadOnlyList<IPropertySetSource> _sources;
    private readonly IReadOnlyList<IDisposable> _editors;
    private EventSubscription _subscription;
    private bool _disposed;
    internal IServiceProvider ServiceProvider { get; }

    private PropertySetViewModel(NodeDataVMCreateParams p, IReadOnlyList<PropertySetItemViewModel> items,
        IReadOnlyList<IPropertySetSource> sources, IReadOnlyList<IDisposable> editors)
        : base(null, null, null, items)
    {
        ServiceProvider = p.ServiceProvider;
        _sources = sources;
        _editors = editors;
    }

    internal static PropertySetViewModel Create(NodeDataVMCreateParams p, CompiledPropertyGroup[] groups)
    {
        var sources = new List<IPropertySetSource>();
        var editors = new List<IDisposable>();
        PropertySetViewModel? result = null;
        try
        {
            var items = groups.Select(g => BuildGroup(g, null)).ToArray();
            result = new(p, items, sources, editors);
            // Accessor invalidation subscriptions were installed first, before this refresh subscription.
            result._subscription = ((Event)p.DataStore.NodeDataChangeDispatcher.DataChanged).Sub(result.Refresh);
            result.Refresh();
            return result;
        }
        catch
        {
            if (result is not null) result.Dispose();
            else
            {
                foreach (var editor in editors) editor.Dispose();
                foreach (var source in sources) source.Dispose();
            }
            throw;
        }

        PropertySetGroupViewModel BuildGroup(CompiledPropertyGroup group, IPropertySetSource? inherited)
        {
            var source = inherited;
            if (group.CreateSource is not null)
            {
                source = group.CreateSource(p);
                sources.Add(source);
            }
            var items = group.Items.Select(item => item switch
            {
                CompiledPropertyGroup child => (PropertySetItemViewModel)BuildGroup(child, source),
                CompiledProperty property => BuildProperty(property.Definition, source!),
                _ => throw new InvalidOperationException("Unknown property-set layout item."),
            }).ToArray();
            return new(group.Name, group.ViewType, source, items);
        }

        PropertyValueViewModel BuildProperty(PropertyDefinition definition, IPropertySetSource source)
        {
            IPropertyEditorViewModel? editor = null;
            if (definition.ViewModelType is { } vmType)
            {
                editor = (IPropertyEditorViewModel)ActivatorUtilities.CreateInstance(p.ServiceProvider, vmType);
                editors.Add(editor);
            }
            return new(definition, source, definition.Choices?.Invoke(p.ServiceProvider), editor, () => result!.Refresh());
        }
    }

    internal override void Refresh()
    {
        if (!_disposed) base.Refresh();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_subscription.IsNull) _subscription.Dispose();
        foreach (var editor in _editors) editor.Dispose();
        foreach (var source in _sources) source.Dispose();
    }
}

/// <summary>A generated property editor; writes always go through its source's editability guard.</summary>
public sealed class PropertyValueViewModel : PropertySetItemViewModel
{
    private readonly PropertyDefinition _definition;
    private readonly IPropertySetSource _source;
    private readonly Action _changed;
    private readonly PropertyChoices? _catalog;
    private readonly IReadOnlyList<PropertyChoice> _baseChoices;
    private readonly ObservableCollection<PropertyChoice> _choices;
    private bool _refreshing;
    private PropertyChoice? _selectedChoice;
    public override string Name => _definition.Name ?? _definition.Id.Name;
    public PropertyId Id => _definition.Id;
    public string? Description => _definition.Description;
    public Type ValueType => _definition.ValueType;
    public override bool IsEditable => _source.IsEditable;
    public bool CanReset => _definition.HasNullValue || !ValueType.IsValueType || Nullable.GetUnderlyingType(ValueType) is not null;
    public IPropertyEditorViewModel? Editor { get; }
    public IRelayCommand ResetCommand { get; }
    public IReadOnlyList<PropertyChoice> Choices => _choices;
    public bool HasChoices => _catalog is not null;

    internal PropertyValueViewModel(PropertyDefinition definition, IPropertySetSource source,
        PropertyChoices? choices, IPropertyEditorViewModel? editor, Action changed)
    {
        _definition = definition;
        _source = source;
        _changed = changed;
        Editor = editor;
        ViewType = definition.ViewType;
        var type = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
        _catalog = choices ?? (type.IsEnum ? new PropertyChoices(
            Enum.GetValues(type).Cast<object>().Select(value => new PropertyChoice(value.ToString()!, value)).ToArray(), Equals) : null);
        _baseChoices = _catalog is null ? [] : CanReset
            ? [new("Default", ResetValue, true), .. _catalog.Values] : _catalog.Values;
        _choices = new(_baseChoices);
        ResetCommand = new RelayCommand(() => Value = ResetValue, () => IsEditable && CanReset);
    }

    private object? ResetValue => _definition.HasNullValue ? _definition.NullValue : null;
    public object? Value
    {
        get => _source.Data is { } data ? _definition.Read(data) : null;
        set
        {
            if (Equals(Value, value)) return;
            var data = _source.EditableData ?? throw new InvalidOperationException($"Property '{Id.Name}' is read only.");
            if (value is null && ValueType.IsValueType && Nullable.GetUnderlyingType(ValueType) is null)
            {
                throw new ArgumentException($"Property '{Id.Name}' does not accept null.", nameof(value));
            }
            if (value is not null && !(Nullable.GetUnderlyingType(ValueType) ?? ValueType).IsInstanceOfType(value))
            {
                throw new ArgumentException($"Property '{Id.Name}' expects {ValueType}.", nameof(value));
            }
            _definition.Write(data, value);
            _changed();
        }
    }
    public bool? BooleanValue
    {
        get => (bool?)Value;
        set => Value = value;
    }
    public string? TextValue
    {
        get => (string?)Value;
        set => Value = value;
    }
    public decimal? NumberValue
    {
        get => Value is { } value ? Convert.ToDecimal(value, CultureInfo.InvariantCulture) : null;
        set
        {
            var type = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
            if (value is { } number && IsInteger(type) && number != decimal.Truncate(number))
            {
                throw new ArgumentException("Enter a whole number.", nameof(value));
            }
            Value = value is null ? null : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
        }
    }
    public PropertyChoice? SelectedChoice
    {
        get => _selectedChoice;
        set
        {
            if (_refreshing || value is null || ReferenceEquals(value, _selectedChoice)) return;
            if (!_choices.Contains(value)) throw new ArgumentException("Select a choice from this property's registry.", nameof(value));
            Value = value.IsDefault ? ResetValue : value.Value;
        }
    }

    internal override void Refresh()
    {
        _refreshing = true;
        try
        {
            if (_catalog is not null)
            {
                var value = Value;
                var selected = _baseChoices.FirstOrDefault(choice => choice.IsDefault
                    ? Equals(value, ResetValue)
                    : !(CanReset && Equals(value, ResetValue)) && _catalog.Equal(choice.Value, value));
                if (selected is null && value is not null)
                {
                    selected = _choices.FirstOrDefault(choice => !choice.IsDefault && Equals(value, choice.Value))
                        ?? new($"Unregistered value ({value})", value);
                    if (!_choices.Contains(selected)) _choices.Add(selected);
                }
                _selectedChoice = selected;
                OnPropertyChanged(nameof(SelectedChoice));
                // Retain encountered values for this editor lifetime. Removing items during a
                // TwoWay selection write would invalidate Avalonia's active selection operation.
            }
            Editor?.Update(new(_source.Data, Value, IsEditable, value => Value = value));
            OnPropertyChanged(nameof(Value));
            OnPropertyChanged(nameof(IsEditable));
            var type = Nullable.GetUnderlyingType(ValueType) ?? ValueType;
            if (type == typeof(bool)) OnPropertyChanged(nameof(BooleanValue));
            if (type == typeof(string)) OnPropertyChanged(nameof(TextValue));
            if (IsNumber(type)) OnPropertyChanged(nameof(NumberValue));
            ResetCommand.NotifyCanExecuteChanged();
        }
        finally { _refreshing = false; }
    }

    internal static bool IsInteger(Type type) => Type.GetTypeCode(type) is
        TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32 or TypeCode.Int64 or TypeCode.UInt64;
    internal static bool IsNumber(Type type) => IsInteger(type) || Type.GetTypeCode(type) is TypeCode.Single or TypeCode.Double or TypeCode.Decimal;
}
