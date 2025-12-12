using System.Collections.Concurrent;
using MainCli.BuilderNew;

public sealed class ApplicationConfigBuilder
{
    private readonly MutableLayer _baseLayer = new();

    public ApplicationConfigLayerBuilder Defaults
    {
        get
        {
            return new(_baseLayer);
        }
    }

    public ApplicationConfigLayerBuilder AddLayer(Layer layer)
    {
        return Defaults.AddLayer(layer);
    }

    public MutableLayer BaseLayer => _baseLayer;
}

public readonly struct ApplicationConfigLayerBuilder
{
    public readonly MutableLayer Layer { get; }

    public ApplicationConfigLayerBuilder(MutableLayer layer)
    {
        Layer = layer;
    }

    public ApplicationConfigLayerBuilder AddLayer(Layer layer)
    {
        var model = new MutableLayer();
        Layer._childLayers.Add(new(layer, model));
        return new(model);
    }

    public void Configure(Action<ApplicationConfigLayerBuilder> configure)
    {
        configure(this);
    }
}

public readonly record struct NamedLayer(Layer Name, MutableLayer Model);
public readonly record struct Layer(string Value) : ICreateFromString<Layer>
{
    public static readonly NameRegistry<Layer> Registry = new();
    public static readonly Layer DefaultLayer = Registry.Register("Default");
    public static Layer Unnamed => new("");
    public static Layer Create(string v) => new(v);
}


public sealed class MutableLayer
{
    private readonly ConcurrentDictionary<LayerConfigKey, LayerConfigContainer> _configs = new();
    internal readonly List<NamedLayer> _childLayers = new();

    public IReadOnlyList<NamedLayer> ChildLayers => _childLayers;

    public ICollection<LayerConfigKey> ConfigKeys => _configs.Keys;

    public LayerConfigContainer? GetUntyped(LayerConfigKey key)
    {
        var container = _configs.GetValueOrDefault(key, null!);
        return container;
    }

    public MaybeLayerConfigContainer<T> Get<T>(LayerConfigKey<T> key) where T : class
    {
        var container = _configs.GetValueOrDefault(key.Value, null!);
        return new(container);
    }

    public LayerConfigContainer<T> GetOrAdd<T>(
        LayerConfigKey<T> key,
        Func<T> factory) where T : class
    {
        var container = _configs.GetOrAdd(key.Value, _ => new()
        {
            Value = factory(),
        });
        return new(container);
    }
}
