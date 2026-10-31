using System.Diagnostics;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using Anton.LayeredData.TreeEnumeration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Config;

public static class MarkerConfigExtension
{
    /// <summary>A teacher configuration found on a layer together with the builder for its node.</summary>
    public readonly record struct MarkerNode(TeacherLayerConfig Config, NodeBuilder Builder);

    public static void AddMarkerServices(this IServiceCollection services)
    {
        MarkerDataHelper.Register(services);
        services.AddLayeredData();
        TeacherLayerConfig.Register(services);
    }

    public static AsyncServiceScope CreateMarkerScope(
        this IServiceProvider sp,
        Action<TeacherLayerConfig> configure)
    {
        var ret = sp.CreateAsyncScope();
        // Doesn't have to be an option snapshot, actually.
        var config = ret.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
        try
        {
            configure(config);
        }
        catch
        {
            ret.Dispose();
            throw;
        }
        return ret;
    }

    public static AsyncServiceScope CreateMarkerScope(
        this IServiceProvider sp,
        TeacherLayerConfig marker)
    {
        var ret = sp.CreateMarkerScope(x =>
        {
            x.TeacherName = marker.TeacherName;
        });
        return ret;
    }

    /// <summary>A node to be deleted together with its parent.</summary>
    private readonly record struct NodeDeletion(MutableNode Parent, MutableNode Node);

    extension(TreeBuilder b)
    {
        public IEnumerable<MarkerNode> GetMarkerNodes()
        {
            // NOTE:
            // We assume that a TeacherLayerConfig exists on ALL levels of the layers.
            // But we only consider the last such layers.
            MarkerNode ProjectValue(DfsEnumerationContext x)
            {
                var config = x.Node.Get(TeacherLayerConfig.Key);
                if (!config.Exists)
                {
                    return default;
                }
                var value = config.Value.GetValue();
                if (value == null)
                {
                    throw new InvalidOperationException("Teacher layer config must be explicitly set!");
                }
                var nodeBuilder = b.CreateBuilder(x.Node);
                return new(value, nodeBuilder);
            }

            return b.BaseNode
                .Dfs()
                .Process()
                .Where(x => x.Node.IsLeaf())
                .Select(ProjectValue)
                .WhereNotDefault();
        }

        public void RemoveNodes(Func<MutableNode, bool> pred)
        {
            var deletionList = b.BaseNode
                .Dfs(x => x.AddParent())
                .SkipLayers(1)
                .SelectWithState(DfsVisitationState.BeforeProcess)
                .Where(x => pred(x.Node))
                .Select(c =>
                {
                    c.Controller.Action = DfsAction.PreventRecursionOnce;
                    var parent = c.Get(ParentContext.Key).Parent;
                    Debug.Assert(parent != null);
                    return new NodeDeletion(Parent: parent, Node: c.Node);
                })
                .ToList();
            foreach (var x in deletionList)
            {
                b.CreateBuilder(x.Parent)
                    .RemoveNode(x.Node);
            }
        }

        public IEnumerable<TeacherLayerConfig> GetAllMarkers()
        {
            return b.GetMarkerNodes()
                .Select(x => x.Config);
        }
    }
}

public sealed class MarkerDataHelper : MarkerDataHelperBase<TeacherLayerConfig>
{
    private readonly TreeBuilder _builder;

    public MarkerDataHelper(
        TreeBuilder builder)
    {
        _builder = builder;
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IMarkerDataHelper, MarkerDataHelper>();
        // Used as ambient context.
        services.AddScoped<TeacherLayerConfig>();
    }

    public override TeacherLayerConfig GetMarkerData(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<TeacherLayerConfig>();
        return config;
    }

    public override NodePath? GetCurrentPath(TeacherLayerConfig config)
    {
        var c = _builder.BaseNode
            .Dfs()
            .AddLayerPath()
            .Process()
            .Where(x => x.Node.IsLeaf())
            .Where(x =>
            {
                var c = x.Node.Get(TeacherLayerConfig.Key);
                if (!c.Exists)
                {
                    return false;
                }

                if (CheckEquality(existing: c.Value.GetValue(), @scoped: config))
                {
                    return true;
                }

                return false;
            })
            .FirstOrDefault();
        if (c.IsNull)
        {
            return null;
        }
        var ret = c.Get(LayerPathContext.Key)
            .Path();
        return ret;

        static bool CheckEquality(TeacherLayerConfig? existing, TeacherLayerConfig scoped)
        {
            if (existing is null)
            {
                return false;
            }

            if (existing.TeacherName.Equals(scoped.TeacherName))
            {
                return true;
            }

            return false;
        }
    }
}
