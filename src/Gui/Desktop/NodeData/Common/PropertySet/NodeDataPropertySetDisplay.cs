using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Anton.LayeredData;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

public sealed class RootPropertySetBuilder
{
    internal List<PropertySetLayout> Sources { get; } = new();
    public PropertySetLayoutBuilder<T> From<T>(NodeDataKey<T> key) where T : class
    {
        var builder = PropertySet.From(key);
        Add(builder);
        return builder;
    }
    public void Add<T>(PropertySetLayoutBuilder<T> builder) where T : class => Sources.Add(builder.Layout);
}

public sealed class PropertySetDisplayFactoryBuilder
{
    private readonly IServiceProvider _sp;
    private readonly RootPropertySetBuilder _root = new();
    public PropertySetDisplayFactoryBuilder(IServiceProvider sp) => _sp = sp;
    public PropertySetDisplayFactoryBuilder Source(Action<RootPropertySetBuilder> configure)
    {
        configure(_root);
        return this;
    }
    public PropertySetDisplayFactoryBuilder SourceFrom<T>(NodeDataKey<T> key,
        Action<PropertySetLayoutBuilder<T>>? configure = null) where T : class
    {
        var builder = _root.From(key);
        if (configure is null) builder.IncludeAll();
        else configure(builder);
        return this;
    }
    public PropertySetDisplayFactory CreateFactory()
    {
        if (_root.Sources.Count == 0) throw new InvalidOperationException("The display must contain at least one property-set source.");
        var configuration = _sp.GetService<PropertySetConfiguration>() ?? new();
        return new(_root.Sources.Select(source => Compile(source, configuration, null)).ToArray());
    }

    private static CompiledPropertyGroup Compile(PropertySetLayout layout, PropertySetConfiguration configuration,
        IReadOnlyList<PropertyDefinition>? inherited)
    {
        var properties = layout.Resolve is null ? inherited! : layout.Resolve(configuration);
        var children = new List<CompiledPropertyItem>();
        var requested = layout.Items.OfType<PropertyDisplay>().Select(x => x.Id).ToHashSet();
        if (layout.IncludesAll)
        {
            foreach (var definition in properties.Where(p => p.Hidden != true && !requested.Contains(p.Id)))
            {
                children.Add(CompileProperty(definition.Copy()));
            }
        }
        foreach (var item in layout.Items)
        {
            if (item is PropertyDisplay display)
            {
                var definition = properties.FirstOrDefault(x => x.Id == display.Id)?.Copy()
                    ?? throw new ArgumentException($"Unknown property '{display.Id.Name}' in {layout.ParentType}.");
                definition.Overlay(display.Definition);
                if (definition.Hidden != true) children.Add(CompileProperty(definition));
            }
            else
            {
                children.Add(Compile((PropertySetLayout)item, configuration, properties));
            }
        }
        ValidateView(layout.ViewType);
        return new(layout.Name, layout.ViewType, layout.CreateSource, children.ToArray());
    }

    private static CompiledProperty CompileProperty(PropertyDefinition definition)
    {
        if (definition.ReadSource is null || definition.WriteSource is null)
        {
            throw new ArgumentException($"Property '{definition.Id.Name}' needs a readable/writable source. Use Uses for a derived property.");
        }
        if ((definition.Getter is null) != (definition.Setter is null) ||
            (definition.SourceType != definition.ValueType && definition.Getter is null))
        {
            throw new ArgumentException($"Mapped property '{definition.Id.Name}' requires both Get and Set.");
        }
        ValidateView(definition.ViewType);
        if (definition.ViewModelType is { } vm)
        {
            if (!typeof(IPropertyEditorViewModel).IsAssignableFrom(vm) || vm.IsAbstract || vm.ContainsGenericParameters)
            {
                throw new ArgumentException($"Custom editor {vm} must be a concrete IPropertyEditorViewModel.");
            }
            if (definition.ViewType is null)
            {
                definition.ViewType = ViewAndViewModelConverter.TypeFromViewModelToView(vm);
                ValidateView(definition.ViewType);
            }
        }
        var type = Nullable.GetUnderlyingType(definition.ValueType) ?? definition.ValueType;
        if (definition.ViewType is null && definition.ViewModelType is null && definition.Choices is null &&
            type != typeof(bool) && type != typeof(string) && !type.IsEnum && !PropertyValueViewModel.IsNumber(type))
        {
            throw new ArgumentException($"Property '{definition.Id.Name}' ({type.Name}) requires a registry, UseVm, or UseView.");
        }
        return new(definition);
    }

    internal static void ValidateView(Type? type)
    {
        if (type is not null && (!typeof(Control).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters))
        {
            throw new ArgumentException($"Custom view {type} must be a concrete Avalonia Control.");
        }
    }
}

public sealed class PropertySetDisplayFactory
{
    private readonly CompiledPropertyGroup[] _sources;
    internal PropertySetDisplayFactory(CompiledPropertyGroup[] sources) => _sources = sources;
    [SuppressMessage("Reliability", "CA2000", Justification = "The returned NodeDataViewModelResult owns and disposes the generated view model.")]
    public NodeDataViewModelResult BuildVm(NodeDataVMCreateParams p)
    {
        var vm = PropertySetViewModel.Create(p, _sources);
        return new(vm, vm);
    }
}

public sealed class VmFactoryService(PropertySetDisplayFactory impl, PropertySetId key) : IPropertySetViewModelFactory
{
    public PropertySetId Key => key;
    public NodeDataViewModelResult Create(NodeDataVMCreateParams p) => impl.BuildVm(p);
}

public static class PropertySetRegistration
{
    public static void AddPropertySetDisplayFactory(this IServiceCollection services, PropertySetId key,
        Action<PropertySetDisplayFactoryBuilder> configure)
    {
        PropertySetConfigurationRegistration.Configuration(services);
        services.AddSingleton<IPropertySetViewModelFactory>(sp =>
        {
            var builder = new PropertySetDisplayFactoryBuilder(sp);
            configure(builder);
            return new VmFactoryService(builder.CreateFactory(), key);
        });
    }
}

internal abstract class PropertyLayoutItem { }
internal sealed class PropertyDisplay(PropertyId id) : PropertyLayoutItem
{
    public PropertyId Id { get; } = id;
    public PropertyDefinition Definition { get; } = new(id, typeof(object));
}
internal sealed class PropertySetLayout : PropertyLayoutItem
{
    public string? Name { get; set; }
    public Type? ViewType { get; set; }
    public Type? ParentType { get; init; }
    public bool IncludesAll { get; set; }
    public Func<PropertySetConfiguration, IReadOnlyList<PropertyDefinition>>? Resolve { get; init; }
    public Func<NodeDataVMCreateParams, IPropertySetSource>? CreateSource { get; init; }
    public List<PropertyLayoutItem> Items { get; } = new();
}

public sealed class PropertyDisplayBuilder : PropertyMetadataBuilder
{
    internal PropertyDisplayBuilder(PropertyDisplay display) : base(display.Definition) { }
}
public sealed class PropertySetLayoutBuilder<T> where T : class
{
    internal PropertySetLayout Layout { get; }
    internal PropertySetLayoutBuilder(PropertySetLayout layout) => Layout = layout;
    public PropertySetLayoutBuilder<T> Title(string name) { Layout.Name = name; return this; }
    public PropertySetLayoutBuilder<T> UseView(ViewId id) { Layout.ViewType = id.ViewType; return this; }
    public PropertySetLayoutBuilder<T> IncludeAll() { Layout.IncludesAll = true; return this; }
    public PropertySetLayoutBuilder<T> Include(Action<PropertySetLayoutBuilder<T>> configure)
    {
        var group = new PropertySetLayout { ParentType = typeof(T) };
        configure(new(group));
        Layout.Items.Add(group);
        return this;
    }
    public PropertyDisplayBuilder IncludeProperty<TValue>(Expression<Func<T, TValue>> access)
        => IncludeProperty(PropertySetConfiguration.DirectProperty(access).Name);
    public PropertyDisplayBuilder IncludeProperty(string name)
    {
        var display = new PropertyDisplay(new(name));
        if (Layout.Items.OfType<PropertyDisplay>().Any(x => x.Id == display.Id))
        {
            throw new ArgumentException($"Property '{name}' is already included in this group.");
        }
        Layout.Items.Add(display);
        return new(display);
    }
    // The sketch used Property inside Include; retain that shorthand for group layouts.
    public PropertyDisplayBuilder Property<TValue>(Expression<Func<T, TValue>> access) => IncludeProperty(access);
    public PropertyDisplayBuilder Property(string name) => IncludeProperty(name);
}

public static class PropertySet
{
    public static PropertySetLayoutBuilder<T> From<T>(NodeDataKey<T> key) where T : class => new(new()
    {
        ParentType = typeof(T),
        Resolve = configuration => configuration.Resolve(key),
        CreateSource = p => new PropertySetSource<T>(p, key),
    });
}

internal abstract record CompiledPropertyItem;
internal sealed record CompiledProperty(PropertyDefinition Definition) : CompiledPropertyItem;
internal sealed record CompiledPropertyGroup(string? Name, Type? ViewType,
    Func<NodeDataVMCreateParams, IPropertySetSource>? CreateSource, CompiledPropertyItem[] Items) : CompiledPropertyItem;
