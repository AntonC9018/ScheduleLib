using System.Diagnostics;
using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Anton.LayeredConfig.TreeEnumeration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Config;

public static class MarkerConfigExtension
{
    public static void AddMarkerServices(this IServiceCollection services)
    {
        MarkerConfigHelper.Register(services);
        services.AddLayeredConfig();
        TeacherLayerConfig.Register(services);
    }

    public static AsyncServiceScope CreateMarkerScope(
        this IServiceProvider sp,
        Action<TeacherLayerConfig> configure)
    {
        var ret = sp.CreateAsyncScope();
        // Doesn't have to be an option snapshot, actually.
        var config = ret.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
        configure(config);
        return ret;
    }

    public static AsyncServiceScope CreateMarkerScope(
        this IServiceProvider sp,
        TeacherLayerConfig marker)
    {
        var ret = sp.CreateAsyncScope();
        // Doesn't have to be an option snapshot, actually.
        var config = ret.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
        config.TeacherName = marker.TeacherName;
        return ret;
    }

    extension(ApplicationConfigBuilder b)
    {
        public IEnumerable<(TeacherLayerConfig Config, ApplicationConfigLayerBuilder Builder)> GetMarkerLayers()
        {
            // NOTE:
            // We assume that a TeacherLayerConfig exists on ALL levels of the layers.
            // But we only consider the last such layers.
            return b.BaseLayer
                .Dfs()
                .Process()
                .Where(x => x.Layer.IsLeaf())
                .Select(x =>
                {
                    var config = x.Layer.GetConfig(TeacherLayerConfig.Key);
                    if (!config.Exists)
                    {
                        return default;
                    }
                    var value = config.Value.GetValue();
                    if (value == null)
                    {
                        throw new InvalidOperationException("Teacher layer config must be explicitly set!");
                    }
                    return (value, b.CreateBuilder(x.Layer));
                })
                .WhereNotDefault();
        }

        public void RemoveLayers(Func<MutableLayer, bool> pred)
        {
            var deletionList = b.BaseLayer
                .Dfs(x => x.AddParent())
                .SkipLayers(1)
                .Select(c =>
                {
                    if (c.State != DfsVisitationState.BeforeProcess)
                    {
                        return default;
                    }
                    if (!pred(c.Layer))
                    {
                        c.Controller.Action = DfsAction.PreventRecursionOnce;
                        var parent = c.Get(ParentContext.Key).Parent;
                        Debug.Assert(parent != null);
                        return (Parent: parent, Node: c.Layer);
                    }
                    return default;
                })
                .ToList();
            foreach (var x in deletionList)
            {
                b.CreateBuilder(x.Parent).RemoveLayer(x.Node);
            }
        }

        public IEnumerable<TeacherLayerConfig> GetAllMarkers()
        {
            return b.GetMarkerLayers().Select(x => x.Config);
        }
    }
}

public sealed class MarkerConfigHelper : MarkerConfigHelperBase<TeacherLayerConfig>
{
    private readonly ApplicationConfigBuilder _builder;

    public MarkerConfigHelper(
        ApplicationConfigBuilder builder)
    {
        _builder = builder;
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IMarkerConfigHelper, MarkerConfigHelper>();
        // Used as ambient context.
        services.AddScoped<TeacherLayerConfig>();
    }

    public override TeacherLayerConfig GetMarkerConfig(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<TeacherLayerConfig>();
        return config;
    }

    public override LayerPath? GetCurrentPath(TeacherLayerConfig config)
    {
        var c = _builder.BaseLayer
            .Dfs()
            .AddLayerPath()
            .Where(x => x.Layer.IsLeaf())
            .Where(x =>
            {
                var c = x.Layer.GetConfig(TeacherLayerConfig.Key);
                if (!c.Exists)
                {
                    return false;
                }
                return CheckEquality(c.Value.GetValue(), config);
            })
            .FirstOrDefault();
        if (c.IsNull)
        {
            return null;
        }
        return c.Get(LayerPathContext.Key).Path();

        static bool CheckEquality(TeacherLayerConfig? existing, TeacherLayerConfig scoped)
        {
            if (existing is null)
            {
                return false;
            }
            if (!existing.TeacherName.Equals(scoped.TeacherName))
            {
                return false;
            }
            return true;
        }
    }
}
