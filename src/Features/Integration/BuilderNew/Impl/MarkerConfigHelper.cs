using System.Diagnostics;
using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
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
        public IEnumerable<(TeacherLayerConfig Config, LayerPath Path)> GetMarkerLayerPaths()
        {
            b.Defaults.GetLeafBuilders
        }

        public IEnumerable<(TeacherLayerConfig Config, ApplicationConfigLayerBuilder Builder)> GetMarkerLayers()
        {
            // NOTE:
            // We assume that a TeacherLayerConfig exists on ALL levels of the layers.
            // But we only consider the last such layers.
            return b.Defaults.GetLeafBuilders()
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
                    return (value, x);
                })
                .WhereNotDefault();
        }

        public void RemoveLayers(Func<MutableLayer, bool> pred)
        {
            Helper(b.Defaults);

            void Helper(ApplicationConfigLayerBuilder parent)
            {
                var children = parent.Layer.ChildLayers;
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (pred(child.Model))
                    {
                        if (child.Model.ChildLayers.Count != 0)
                        {
                            throw new NotImplementedException();
                        }
                        parent.RemoveLayers(child);
                    }
                    else
                    {
                        var builder = parent.CreateLayerBuilder(child);
                        Helper(builder);
                    }
                }
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
        var path = _builder
            .GetMarkerLayers()
            .GetPathsOfDescendantsOrSelf(x =>
            {
                var c = x.GetConfig(TeacherLayerConfig.Key);
                if (!c.Exists)
                {
                    return false;
                }
                return CheckEquality(c.Value.GetValue(), config);
            })
            .FirstOrDefault();
        if (path == default)
        {
            return null;
        }
        if (path.Path[^1].ChildLayers.Count != 0)
        {
            Debug.Fail("Not terminal layer.");
        }
        return path;

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
