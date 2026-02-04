using System.Collections.Concurrent;

namespace Anton.LayeredConfig;

public sealed class ApplicationConfigBuilder
{
    private readonly MutableLayer _baseLayer = new();
    private readonly IServiceProvider _singletonServiceProvider;

    public ApplicationConfigBuilder(IServiceProvider singletonServiceProvider)
    {
        _singletonServiceProvider = singletonServiceProvider;
    }

    public ApplicationConfigLayerBuilder Defaults
    {
        get
        {
            return new(_baseLayer, _singletonServiceProvider);
        }
    }

    public ApplicationConfigLayerBuilder AddLayer(LayerName layer)
    {
        return Defaults.AddLayer(layer);
    }

    public MutableLayer BaseLayer => _baseLayer;

    // Might want to remove this.
    public ApplicationConfigLayerBuilder CreateBuilder(MutableLayer layer)
    {
        return new(layer, _singletonServiceProvider);
    }
}

public readonly struct ApplicationConfigLayerBuilder : IEquatable<ApplicationConfigLayerBuilder>
{
    public MutableLayer Layer { get; }
    public IServiceProvider SingletonServiceProvider { get; }

    public bool IsNull
    {
        get
        {
            return Layer is null;
        }
    }

    public bool Equals(ApplicationConfigLayerBuilder other)
    {
        return other.Layer == Layer;
    }

    public ApplicationConfigLayerBuilder(
        MutableLayer layer,
        IServiceProvider singletonServiceProvider)
    {
        Layer = layer;
        SingletonServiceProvider = singletonServiceProvider;
    }

    public ApplicationConfigLayerBuilder AddLayer(LayerName layer)
    {
        var model = new MutableLayer();
        model.Name = layer;
        var namedLayer = new NamedLayer(model);
        Layer._childLayers.Add(namedLayer);
        return CreateLayerBuilder(namedLayer);
    }

    public ApplicationConfigLayerBuilder CreateLayerBuilder(NamedLayer layer)
    {
        if (!Layer._childLayers.Contains(layer))
        {
            throw new InvalidOperationException("This layer is not a child");
        }
        return new(layer.Model, SingletonServiceProvider);
    }

    public void RemoveLayer(MutableLayer layer)
    {
        Layer._childLayers.Remove(new(layer));
    }

    public void Configure(Action<ApplicationConfigLayerBuilder> configure)
    {
        configure(this);
    }
}

public readonly record struct NamedLayer(MutableLayer Model);
public readonly record struct LayerName(string Value) : ICreateFromString<LayerName>
{
    public static readonly NameRegistry<LayerName> Registry = new();
    public static readonly LayerName DefaultLayer = Registry.Register("Default");
    public static LayerName Unnamed => new("");
    public static LayerName Create(string v) => new(v);
}

public readonly record struct ConfigAccessorHelper
{
    public LayerConfigKey Key { get; }
    private readonly LayerConfigContainer _container;

    internal ConfigAccessorHelper(
        LayerConfigKey key,
        LayerConfigContainer container)
    {
        Key = key;
        _container = container;
    }

    public MaybeLayerConfigContainer<T> As<T>(LayerConfigKey<T> key)
        where T : class
    {
        if (key != Key)
        {
            return new(null);
        }
        return new(_container);
    }

    public UntypedLayerConfigContainer Container => new(_container);
}

public sealed class MutableLayer
{
    private readonly ConcurrentDictionary<LayerConfigKey, LayerConfigContainer> _configs = new();
    internal readonly List<NamedLayer> _childLayers = new();

    public LayerName Name { get; set; } = LayerName.Unnamed;

    public IReadOnlyList<NamedLayer> ChildLayers => _childLayers;

    public ICollection<LayerConfigKey> ConfigKeys => _configs.Keys;

    public IEnumerable<ConfigAccessorHelper> Configs => _configs.Select(x => new ConfigAccessorHelper(x.Key, x.Value));

    public LayerConfigContainer? GetConfigUntyped(LayerConfigKey key)
    {
        var container = _configs.GetValueOrDefault(key, null!);
        return container;
    }

    public MaybeLayerConfigContainer<T> GetConfig<T>(LayerConfigKey<T> key) where T : class
    {
        var container = _configs.GetValueOrDefault(key.Value, null!);
        return new(container);
    }

    public LayerConfigContainer<T> GetOrAddConfig<T>(LayerConfigKey<T> key) where T : class
    {
        var container = _configs.GetOrAdd(key.Value, _ => new());
        return new(container);
    }
}
