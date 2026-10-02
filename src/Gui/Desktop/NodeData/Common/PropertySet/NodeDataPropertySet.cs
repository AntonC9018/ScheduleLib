using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using Anton.LayeredData;
using Avalonia.Controls;
using Desktop.NodeData.Features.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

public readonly record struct PropertyId(string Name);
public readonly record struct PropertySetId(string Key);
public readonly record struct OptionalViewId(Type? ViewType);
public readonly record struct ViewId(Type ViewType)
{
    public static implicit operator OptionalViewId(ViewId id) => new(id.ViewType);
}
public readonly record struct ViewId<T>() where T : Control
{
    public static implicit operator ViewId(ViewId<T> id) => new(typeof(T));
}
public readonly record struct OptionalViewModelId(Type? ViewModelType);
public readonly record struct ViewModelId(Type ViewModelType)
{
    public static implicit operator OptionalViewModelId(ViewModelId id) => new(id.ViewModelType);
}
public readonly record struct ViewModelId<T>() where T : IPropertyEditorViewModel
{
    public static implicit operator ViewModelId(ViewModelId<T> id) => new(typeof(T));
}

/// <summary>The contract for an editor supplied with UseVm. Update never writes to the model.</summary>
public interface IPropertyEditorViewModel : INotifyPropertyChanged, IDisposable
{
    void Update(PropertyEditorContext context);
}

public sealed record PropertyEditorContext(
    object? Parent,
    object? Value,
    bool IsEditable,
    Action<object?> SetValue);

internal sealed class PropertyDefinition(PropertyId id, Type valueType)
{
    public PropertyId Id { get; } = id;
    public Type ValueType { get; } = valueType;
    public Type? SourceType { get; set; }
    public Func<object, object?>? ReadSource { get; set; }
    public Action<object, object?>? WriteSource { get; set; }
    public Func<object?, object?>? Getter { get; set; }
    public Func<object?, object?, object?>? Setter { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? Hidden { get; set; }
    public Type? ViewType { get; set; }
    public Type? ViewModelType { get; set; }
    public Func<IServiceProvider, PropertyChoices>? Choices { get; set; }
    public bool HasNullValue { get; set; }
    public object? NullValue { get; set; }

    public PropertyDefinition Copy() => (PropertyDefinition)MemberwiseClone();

    public void Overlay(PropertyDefinition other)
    {
        if (other.ReadSource is not null)
        {
            SourceType = other.SourceType;
            ReadSource = other.ReadSource;
            WriteSource = other.WriteSource;
        }
        Getter = other.Getter ?? Getter;
        Setter = other.Setter ?? Setter;
        Name = other.Name ?? Name;
        Description = other.Description ?? Description;
        Hidden = other.Hidden ?? Hidden;
        ViewType = other.ViewType ?? ViewType;
        ViewModelType = other.ViewModelType ?? ViewModelType;
        Choices = other.Choices ?? Choices;
        if (other.HasNullValue)
        {
            HasNullValue = true;
            NullValue = other.NullValue;
        }
    }

    public object? Read(object parent) => Getter is null ? ReadSource!(parent) : Getter(ReadSource!(parent));
    public void Write(object parent, object? value)
    {
        var source = Setter is null ? value : Setter(ReadSource!(parent), value);
        WriteSource!(parent, source);
    }
}

public abstract class PropertyMetadataBuilder
{
    internal PropertyDefinition Definition { get; }
    internal PropertyMetadataBuilder(PropertyDefinition definition) => Definition = definition;
}

public static class PropertyMetadataConfiguration
{
    public static TBuilder Rename<TBuilder>(this TBuilder builder, string name) where TBuilder : PropertyMetadataBuilder
    {
        builder.Definition.Name = name;
        return builder;
    }
    public static TBuilder Describe<TBuilder>(this TBuilder builder, string description) where TBuilder : PropertyMetadataBuilder
    {
        builder.Definition.Description = description;
        return builder;
    }
    public static TBuilder Hide<TBuilder>(this TBuilder builder) where TBuilder : PropertyMetadataBuilder
    {
        builder.Definition.Hidden = true;
        return builder;
    }
    public static TBuilder UseView<TBuilder>(this TBuilder builder, ViewId id) where TBuilder : PropertyMetadataBuilder
    {
        builder.Definition.ViewType = id.ViewType;
        return builder;
    }
    public static TBuilder UseVm<TBuilder>(this TBuilder builder, ViewModelId id) where TBuilder : PropertyMetadataBuilder
    {
        builder.Definition.ViewModelType = id.ViewModelType;
        return builder;
    }
}

public sealed class PropertyDefinitionBuilder<TParent, TValue> : PropertyMetadataBuilder where TParent : class
{
    internal PropertyDefinitionBuilder(PropertyDefinition definition) : base(definition) { }
    public MappedPropertyBuilder<TSource, TValue> Uses<TSource>(Expression<Func<TParent, TSource>> access)
    {
        PropertySetConfiguration.SetAccess(Definition, access);
        return new(Definition);
    }
}

public sealed class MappedPropertyBuilder<TSource, TValue> : PropertyMetadataBuilder
{
    internal MappedPropertyBuilder(PropertyDefinition definition) : base(definition) { }
    public MappedPropertyBuilder<TSource, TValue> Get(Func<TSource, TValue> getter)
    {
        Definition.Getter = source => getter((TSource)source!);
        return this;
    }
    public MappedPropertyBuilder<TSource, TValue> Set(Func<TSource, TValue, TSource> setter)
    {
        Definition.Setter = (source, value) => setter((TSource)source!, (TValue)value!);
        return this;
    }
    public MappedPropertyBuilder<TSource, TValue> NullValue(TValue value)
    {
        Definition.HasNullValue = true;
        Definition.NullValue = value;
        return this;
    }
    public MappedPropertyBuilder<TSource, TValue> UseDefaultRegistry()
    {
        // Nullable<T> uses Registry<T>, so nullable enums share the same named choices.
        var type = Nullable.GetUnderlyingType(typeof(TValue)) ?? typeof(TValue);
        Definition.Choices = sp => PropertyChoices.FromRegistry(sp, type);
        return this;
    }
    public MappedPropertyBuilder<TSource, TValue> UseRegistry(Action<ThingRegistryOptions<TValue>> configure)
    {
        var options = new ThingRegistryOptions<TValue>();
        configure(options);
        var snapshot = PropertyChoices.From(options.Values, options.Comparer);
        Definition.Choices = _ => snapshot;
        return this;
    }
}

public static class NullablePropertyMapping
{
    public static MappedPropertyBuilder<TSource?, TValue> GetUnwrap<TSource, TValue>(
        this MappedPropertyBuilder<TSource?, TValue> builder, Func<TSource, TValue> getter) where TSource : struct
        => builder.Get(source => source is { } value ? getter(value) : default!);

    public static MappedPropertyBuilder<TSource?, TValue> SetUnwrap<TSource, TValue>(
        this MappedPropertyBuilder<TSource?, TValue> builder, Func<TSource, TValue, TSource?> setter) where TSource : struct
        => builder.Set((source, value) => source is { } present ? setter(present, value) : null);
}

public sealed class PropertySetConfigurationBuilder<T> where T : class
{
    private readonly Dictionary<PropertyId, PropertyDefinition> _properties;
    internal PropertySetConfigurationBuilder(Dictionary<PropertyId, PropertyDefinition> properties) => _properties = properties;

    private PropertyDefinition Definition(PropertyId id, Type valueType)
    {
        if (!_properties.TryGetValue(id, out var definition))
        {
            definition = new(id, valueType);
            _properties.Add(id, definition);
        }
        if (definition.ValueType != valueType)
        {
            throw new ArgumentException($"Property '{id.Name}' was already configured as {definition.ValueType}.");
        }
        return definition;
    }

    public PropertyDefinitionBuilder<T, TValue> Property<TValue>(string name)
        => new(Definition(new(name), typeof(TValue)));
    public PropertyDefinitionBuilder<T, TValue> Property<TValue>(PropertyId id)
        => new(Definition(id, typeof(TValue)));

    public MappedPropertyBuilder<TValue, TValue> Property<TValue>(Expression<Func<T, TValue>> access)
    {
        var property = PropertySetConfiguration.DirectProperty(access);
        var definition = Definition(new(property.Name), typeof(TValue));
        PropertySetConfiguration.SetAccess(definition, access);
        return new(definition);
    }
}

public sealed class PropertySetDefaultsBuilder
{
    private readonly PropertySetConfiguration _configuration;
    internal PropertySetDefaultsBuilder(PropertySetConfiguration configuration) => _configuration = configuration;
    public void PropertiesWithType<TValue>(Action<MappedPropertyBuilder<TValue, TValue>> configure)
        => _configuration.TypeDefaults.Add((typeof(TValue), definition => configure(new(definition))));
    public void Parent<TParent>(Action<PropertySetConfigurationBuilder<TParent>> configure) where TParent : class
        => _configuration.ParentDefaults.Add((typeof(TParent), properties => configure(new(properties))));
}

public static class PropertySetConfigurationRegistration
{
    internal static PropertySetConfiguration Configuration(IServiceCollection services)
    {
        var existing = services.FirstOrDefault(x => x.ServiceType == typeof(PropertySetConfiguration));
        if (existing?.ImplementationInstance is PropertySetConfiguration configuration)
        {
            return configuration;
        }
        configuration = new();
        services.AddSingleton(configuration);
        return configuration;
    }

    public static void ConfigurePropertySet<T>(this IServiceCollection services, NodeDataKey<T> key,
        Action<PropertySetConfigurationBuilder<T>> configure) where T : class
    {
        var configuration = Configuration(services);
        if (!configuration.Properties.TryGetValue(key.Value, out var properties))
        {
            properties = new();
            configuration.Properties.Add(key.Value, properties);
        }
        configure(new(properties));
    }

    public static void ConfigurePropertySetDefaults(this IServiceCollection services,
        Action<PropertySetDefaultsBuilder> configure) => configure(new(Configuration(services)));
}

internal sealed class PropertySetConfiguration
{
    public Dictionary<NodeDataKey, Dictionary<PropertyId, PropertyDefinition>> Properties { get; } = new();
    public List<(Type Type, Action<PropertyDefinition> Configure)> TypeDefaults { get; } = new();
    public List<(Type Parent, Action<Dictionary<PropertyId, PropertyDefinition>> Configure)> ParentDefaults { get; } = new();

    public IReadOnlyList<PropertyDefinition> Resolve<T>(NodeDataKey<T> key) where T : class
    {
        var definitions = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetMethod is { IsPublic: true } && p.SetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0)
            .Select(p => FromProperty(p)).ToDictionary(p => p.Id);

        Properties.TryGetValue(key.Value, out var configured);
        // Make synthetic properties visible to type and parent defaults as well.
        if (configured is not null)
        {
            foreach (var (id, definition) in configured)
            {
                if (!definitions.TryGetValue(id, out var existing))
                {
                    definitions.Add(id, new(id, definition.ValueType));
                }
                else if (existing.ValueType != definition.ValueType)
                {
                    definitions[id] = new(id, definition.ValueType)
                    {
                        SourceType = existing.SourceType,
                        ReadSource = existing.ReadSource,
                        WriteSource = existing.WriteSource,
                    };
                }
            }
        }
        foreach (var definition in definitions.Values)
        {
            foreach (var (type, configure) in TypeDefaults)
            {
                if (type.IsAssignableFrom(definition.ValueType)) configure(definition);
            }
        }
        foreach (var (parent, configure) in ParentDefaults)
        {
            if (parent.IsAssignableFrom(typeof(T))) configure(definitions);
        }
        if (configured is not null)
        {
            foreach (var (id, definition) in configured) definitions[id].Overlay(definition);
        }
        return definitions.Values.Select(x => x.Copy()).ToArray();
    }

    private static PropertyDefinition FromProperty(PropertyInfo property) => new(new(property.Name), property.PropertyType)
    {
        SourceType = property.PropertyType,
        ReadSource = property.GetValue,
        WriteSource = property.SetValue,
    };

    internal static PropertyInfo DirectProperty(LambdaExpression access)
    {
        if (access.Body is not MemberExpression { Member: PropertyInfo property, Expression: ParameterExpression })
        {
            throw new ArgumentException("Use a direct property access, such as x => x.Credentials.", nameof(access));
        }
        if (property.GetMethod is not { IsPublic: true } || property.SetMethod is not { IsPublic: true })
        {
            throw new ArgumentException($"Property '{property.Name}' must be publicly readable and writable.", nameof(access));
        }
        return property;
    }

    internal static void SetAccess<TParent, TValue>(PropertyDefinition definition, Expression<Func<TParent, TValue>> access)
    {
        var property = DirectProperty(access);
        var getter = access.Compile();
        definition.SourceType = typeof(TValue);
        definition.ReadSource = parent => getter((TParent)parent);
        definition.WriteSource = (parent, value) => property.SetValue(parent, value);
    }
}
