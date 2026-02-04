using System.Diagnostics;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public static class LayerQueries
{
    extension (MutableLayer layer)
    {
        public DfsEnumerable Dfs(Action<DfsEnumerable>? contextBuilder = null)
        {
            var ret = new DfsEnumerable(layer);
            contextBuilder?.Invoke(ret);
            return ret;
        }

        public IEnumerable<MutableLayer> GetDescendantsOrSelf()
        {
            return layer
                .Dfs()
                .Process()
                .Select(x => x.Layer);
        }

        public IEnumerable<MutableLayer> GetLeafLayers()
        {
            return layer
                .GetDescendantsOrSelf()
                .Where(x => x.IsLeaf());
        }

        public bool IsLeaf()
        {
            return layer.ChildLayers.Count == 0;
        }

        public T? GetConfigValue<T>(LayerConfigKey<T> key) where T : class
        {
            var t = layer.GetConfig(key);
            if (!t.Exists)
            {
                return null;
            }
            if (t.Value.GetValue() is { } val)
            {
                return val;
            }
            return null;
        }

        public IEnumerable<(LayerPath Path, T Config)> GetConfigs<T>(
            LayerConfigKey<T> key)
            where T : class
        {
            return layer
                .Dfs(c => c.AddLayerPath())
                .Process()
                .Where(x => x.Layer.IsLeaf())
                .Select(x => (Context: x, Config: x.Layer.GetConfigValue(key)))
                .Where(x => x.Config != null)
                .Select(x => (x.Context.Get(LayerPathContext.Key).Path(), x.Config!));
        }
    }

    extension (ApplicationConfigLayerBuilder builder)
    {
        public IEnumerable<ApplicationConfigLayerBuilder> GetLeafBuilders()
        {
            var leafs = builder.Layer.GetLeafLayers();
            foreach (var leaf in leafs)
            {
                yield return new(leaf, builder.SingletonServiceProvider);
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
