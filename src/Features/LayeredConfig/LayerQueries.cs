using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredConfig;

public readonly record struct LayerPath(ImmutableArray<MutableLayer> Path)
{
    public readonly MutableLayer Root => Path[0];
    public readonly MutableLayer Leaf => Path[^1];
}

public enum VisitorAction
{
    Recurse,
    Skip,
    StopAll,
}

public abstract class ILayerVisitorActor
{
    public virtual VisitorAction BeforeProcess(MutableLayer layer)
    {
        _ = layer;
        return VisitorAction.Recurse;
    }
    public virtual void AfterProcess(MutableLayer layer)
    {
    }
}

public sealed class DefaultVisitorActor : ILayerVisitorActor
{
    public static readonly DefaultVisitorActor Instance = new();
}

public sealed class LayerVisitor
{
    private readonly ILayerVisitorActor _actor;

    public LayerVisitor(ILayerVisitorActor actor)
    {
        _actor = actor;
    }

    public VisitorAction BeforeProcess(MutableLayer layer)
    {
        return _actor.BeforeProcess(layer);
    }

    public VisitorAction Visit(MutableLayer layer)
    {
        {
            var result = BeforeProcess(layer);
            if (result is VisitorAction.Skip or VisitorAction.StopAll)
            {
                return result;
            }
            Debug.Assert(result is VisitorAction.Recurse);
        }

        foreach (var x in layer.ChildLayers)
        {
            var result = Visit(x.Model);
            if (result == VisitorAction.StopAll)
            {
                return VisitorAction.StopAll;
            }
        }

        _actor.AfterProcess(layer);
        return VisitorAction.Recurse;
    }
}

public static class LayerQueries
{
    extension (MutableLayer layer)
    {
        public IEnumerable<MutableLayer> GetDescendantsOrSelf()
        {

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

        public IEnumerable<LayerPath> GetLeafLayerPaths()
        {
            var builder = ImmutableArray.CreateBuilder<MutableLayer>();
            return Helper(layer);

            IEnumerable<LayerPath> Helper(MutableLayer layer)
            {
                builder.Add(layer);
                var children = layer.ChildLayers;
                if (children.Count == 0)
                {
                    yield return new(builder.ToImmutableArray());
                }
                foreach (var ch in children)
                {
                    foreach (var x in Helper(ch.Model))
                    {
                        yield return x;
                    }
                }
                builder.Count--;
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
