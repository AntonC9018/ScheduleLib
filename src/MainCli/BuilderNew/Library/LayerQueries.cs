using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace MainCli.BuilderNew;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path);

public static class LayerQueries
{
    extension (MutableLayer layer)
    {
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
    }

    public static T? ConstructConfig<T>(
        this LayerPath path,
        IServiceProvider serviceProvider)

        where T : class, IConfig<T>
    {
        var key = T.Key;
        var basicOperations = serviceProvider.GetRequiredService<IBasicOperations<T>>();
        var merger = serviceProvider.GetRequiredService<IMerger<T>>();
        var current = basicOperations.Empty();
        bool remove = false;

        foreach (var layer in path.Path)
        {
            var maybe = layer.Get(key);
            if (!maybe.Exists)
            {
                continue;
            }

            if (remove)
            {
                throw new NotSupportedException("Appending context to a thing removed previously is not supported");
            }

            var config = maybe.Value;
            if (config.Flags.Remove)
            {
                remove = true;
                continue;
            }

            if (config.Flags.Clean)
            {
                current = basicOperations.Reset(current);
            }

            current = merger.Merge(config.Value, current);
        }

        if (remove)
        {
            return null;
        }
        return current;
    }

}
