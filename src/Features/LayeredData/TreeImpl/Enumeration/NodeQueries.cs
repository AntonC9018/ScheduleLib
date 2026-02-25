using System.Diagnostics;
using Anton.LayeredData.TreeEnumeration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Anton.LayeredData.TreeEnumeration;

public static class LayerQueries
{
    extension(IEnumerable<DfsEnumerationContext> builder)
    {
        public IEnumerable<DfsEnumerationContext> Process()
        {
            return builder.SelectWithState(DfsVisitationState.Process);
        }
    }

    extension (MutableNode node)
    {
        public DfsEnumerable Dfs(Action<DfsEnumerable>? contextBuilder = null)
        {
            var ret = new DfsEnumerable(node);
            contextBuilder?.Invoke(ret);
            return ret;
        }

        public IEnumerable<MutableNode> GetDescendantsOrSelf()
        {
            return node
                .Dfs()
                .Process()
                .Select(x => x.Node);
        }

        public IEnumerable<MutableNode> GetLeafLayers()
        {
            return node
                .GetDescendantsOrSelf()
                .Where(x => x.IsLeaf());
        }

        public bool IsLeaf()
        {
            return node.ChildNodes.Count == 0;
        }

        public T? GetValue<T>(NodeDataKey<T> key) where T : class
        {
            var t = node.Get(key);
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
    }

    // TODO: When building the config, collect information about which layer the value came from
    public static T? ConstructValue<T>(
        this NodePath path,
        NodeDataKey<T> key,
        IServiceProvider serviceProvider)

        where T : class
    {
        var basicOperations = serviceProvider.GetRequiredService<IBasicOperations<T>>();
        T? current = null;

        foreach (var node in path.Path)
        {
            var maybe = node.Get(key);
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
