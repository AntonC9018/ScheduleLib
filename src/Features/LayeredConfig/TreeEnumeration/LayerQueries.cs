using System.Diagnostics;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;

namespace Anton.LayeredConfig;


public static class LayerQueries
{
    extension (MutableLayer layer)
    {
        public ILayerStateEnumerable Dfs()
        {
            return new LayerStateEnumerable(layer);
        }

        public IEnumerable<MutableLayer> GetDescendantsOrSelf()
        {
            return layer.Dfs().Process();
        }

        public IEnumerable<MutableLayer> GetLeafLayers()
        {
            Debug.Assert(layer.ChildLayers.Count != 0);
            return layer
                .Dfs()
                .Process()
                .Where(x => x.ChildLayers.Count != 0);
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
            return layer.Dfs()
                .AsSingleUse()
                .AddLayerPath(out var layerPath)
                .Process()
                .Where(x => x.IsLeaf())
                .Select(x => x.GetConfigValue(key))
                .WhereNotNull()
                .Select(x => (layerPath.Path(), x));
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
