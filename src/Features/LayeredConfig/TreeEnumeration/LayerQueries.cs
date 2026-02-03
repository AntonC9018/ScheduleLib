using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path)
{
    public readonly MutableLayer Root => Path[0];
    public readonly MutableLayer Leaf => Path[^1];
}

public static class LayerQueries
{
    public readonly struct LayerPathConsumer() : ILayerStateEnumerationConsumer
    {
        public readonly ImmutableArray<MutableLayer>.Builder Builder = ImmutableArray.CreateBuilder<MutableLayer>();

        public void Consume(LayerStateEnumerator.Value value)
        {
            switch (value.State)
            {
                case VisitorState.Process:
                {
                    Builder.Add(value.Layer);
                    break;
                }
                case VisitorState.AfterProcess:
                {
                    Builder.Count--;
                    break;
                }
            }
        }

        public LayerPath Path() => new(Builder.ToImmutable());
    }

    extension (MutableLayer layer)
    {
        public IEnumerable<MutableLayer> GetDescendantsOrSelf()
        {
            var e = new LayerStateEnumerator();
            while (e.MoveNext())
            {
                var c = e.Current;
                if (c.State == VisitorState.Process)
                {
                    yield return c.Layer;
                }
            }
        }

        public IEnumerable<LayerPath> GetPathsOfDescendantsOrSelf(
            Func<MutableLayer, bool> isMatch)
        {
            var layerPath = new LayerPathConsumer();
            var e = new LayerStateEnumerator()
                .WithConsumer(layerPath);
            while (e.MoveNext())
            {
                var c = e.Current;
                if (c.State == VisitorState.Process
                    && isMatch(e.Current.Layer))
                {
                    yield return layerPath.Path();
                }
            }
        }

        public IEnumerable<LayerPath> GetLeafLayerPaths()
        {
            var layerPath = new LayerPathConsumer();
            var e = new LayerStateEnumerator()
                .WithConsumer(layerPath);
            while (e.MoveNext())
            {
                var c = e.Current;
                if (c.State == VisitorState.Process
                    && c.Layer.ChildLayers.Count == 0)
                {
                    yield return layerPath.Path();
                }
            }
        }

        public IEnumerable<NamedLayer> GetLeafLayers()
        {
            Debug.Assert(layer.ChildLayers.Count != 0);
            foreach (var x in layer.ChildLayers)
            {
                foreach (var ch in Helper(x))
                {
                    yield return ch;
                }
            }

            IEnumerable<NamedLayer> Helper(NamedLayer layer)
            {
                var children = layer.Model.ChildLayers;
                if (children.Count == 0)
                {
                    yield return layer;
                }
                foreach (var ch in children)
                {
                    foreach (var x in Helper(ch))
                    {
                        yield return x;
                    }
                }
            }
        }

        public IEnumerable<(LayerPath Path, T Config)> GetConfigs<T>(
            LayerConfigKey<T> key)
            where T : class
        {
            var ret = layer.GetPathsOfDescendantsOrSelf(x => x.GetConfig(key).Exists);
            foreach (var path in ret)
            {
                var last = path.Path[^1];
                var config = last.GetConfig(key).Value;
                if (config.GetValue() is { } val)
                {
                    yield return (path, val);
                }
            }
        }
    }

    extension (ApplicationConfigLayerBuilder builder)
    {
        public IEnumerable<ApplicationConfigLayerBuilder> GetLeafBuilders()
        {
            var leafs = builder.Layer.GetLeafLayers();
            foreach (var leaf in leafs)
            {
                yield return new(leaf.Model, builder.SingletonServiceProvider);
            }
        }
    }

    // TODO: When building the config, collect information about which layer the value came from
    public static T? ConstructConfig<T>(
        this LayerPath path,
        LayerConfigKey<T> key,
        IServiceProvider serviceProvider)

        where T : class
    {
        var basicOperations = serviceProvider.GetRequiredService<IBasicOperations<T>>();
        T? current = null;

        foreach (var layer in path.Path)
        {
            var maybe = layer.GetConfig(key);
            if (!maybe.Exists)
            {
                continue;
            }

            var config = maybe.Value;
            if (config.UpdateActions.IsEmpty)
            {
                continue;
            }

            if (current == null)
            {
                current = basicOperations.Empty();
            }
            Debug.Assert(current != null);

            foreach (var a in config.UpdateActions)
            {
                current = a.Update(serviceProvider, current);
                if (current == null)
                {
                    break;
                }
            }
        }
        return current;
    }

}
