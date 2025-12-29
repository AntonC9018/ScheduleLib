using System.Collections.Immutable;
using MainCli.BuilderNew;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path);

public static class LayerQueries
{
    extension (MutableLayer layer)
    {
        public IEnumerable<MutableLayer> GetDescendantsOrSelf()
        {
            foreach (var child in layer._childLayers)
            {
                foreach (var x in child.Model.GetDescendantsOrSelf())
                {
                    yield return x;
                }
            }
        }

        public IEnumerable<MutableLayer> GetDescendantsOrSelf(
            Func<MutableLayer, bool> isMatch)
        {
            if (isMatch(layer))
            {
                yield return layer;
            }
            foreach (var child in layer._childLayers)
            {
                foreach (var x in child.Model.GetDescendantsOrSelf(isMatch))
                {
                    yield return x;
                }
            }
        }

        public IEnumerable<LayerPath> GetPathsOfDescendantsOrSelf(
            Func<MutableLayer, bool> isMatch)
        {
            var builder = ImmutableArray.CreateBuilder<MutableLayer>();
            return Helper(layer);

            IEnumerable<LayerPath> Helper(MutableLayer layer)
            {
                builder.Add(layer);
                if (isMatch(layer))
                {
                    yield return new(builder.ToImmutable());
                }
                foreach (var child in layer._childLayers)
                {
                    foreach (var x in Helper(child.Model))
                    {
                        yield return x;
                    }
                }
                builder.Count--;
            }
        }

        public IEnumerable<(LayerPath Path, T Config)> GetConfigs<T>(
            LayerConfigKey<T> key)
            where T : class
        {
            var ret = layer.GetPathsOfDescendantsOrSelf(x => x.Get(key).Exists);
            foreach (var path in ret)
            {
                var last = path.Path[^1];
                var config = last.Get(key).Value;
                if (config.GetValue() is { } val)
                {
                    yield return (path, val);
                }
            }
        }

    }

    // IDEA: Add a way to have a different model for config that is being built.
    // TODO: Add providers that could modify this after it's constructed?
    public static T? ConstructConfig<T>(
        this LayerPath path,
        LayerConfigKey<T> key,
        IServiceProvider serviceProvider)

        where T : class
    {
        var basicOperations = serviceProvider.GetRequiredService<IBasicOperations<T>>();
        // var merger = serviceProvider.GetRequiredService<IMerger<T>>();
        T? current = null;

        foreach (var layer in path.Path)
        {
            var maybe = layer.Get(key);
            if (!maybe.Exists)
            {
                continue;
            }

            if (current == null)
            {
                current = basicOperations.Empty();
            }

            var config = maybe.Value;
            foreach (var a in config.UpdateActions)
            {
                if (current == null)
                {
                    break;
                }
                current = a.Update(serviceProvider, current);
            }
        }
        return current;
    }

}
