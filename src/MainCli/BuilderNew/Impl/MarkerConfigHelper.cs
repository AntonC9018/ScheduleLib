using System.Diagnostics;
using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Parsing;

namespace MainCli.BuilderNew.Impl;

public static class MarkerConfigExtension
{
    public static void AddMarkerServices(this IServiceCollection services)
    {
        services.AddSingleton<IMarkerConfigHelper, MarkerConfigHelper>();
        services.AddSingleton<ApplicationConfigBuilder>();
        services.AddScoped<ConfigProvider>();
        // services.AddSingleton<IEqualityComparer<TeacherLayerConfig>>();
        services.AddScoped<TeacherLayerConfig>();
        services.AddSingleton<IBasicOperations<Name>, ImmutableClassBasicOperations<Name>>();
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

    public static IEnumerable<TeacherLayerConfig> GetAllMarkers(
        this ApplicationConfigBuilder b)
    {
        return b.BaseLayer.GetConfigs(TeacherLayerConfig.Key).Select(x => x.Config);
    }
}

public sealed class MarkerConfigHelper : MarkerConfigHelperBase<TeacherLayerConfig>
{
    private readonly ApplicationConfigBuilder _builder;
    private readonly IEqualityComparer<TeacherLayerConfig> _comparer;

    public MarkerConfigHelper(
        ApplicationConfigBuilder builder,
        IEqualityComparer<TeacherLayerConfig>? comparer = null)
    {
        _builder = builder;
        _comparer = comparer ?? EqualityComparer<TeacherLayerConfig>.Default;
    }

    protected override TeacherLayerConfig GetMarkerConfig(IServiceProvider sp)
    {
        var config = sp.GetRequiredService<TeacherLayerConfig>();
        return config;
    }

    protected override LayerPath? GetCurrentPath(TeacherLayerConfig config)
    {
        var path = _builder.BaseLayer
            .GetPathsOfDescendantsOrSelf(x =>
            {
                var c = x.Get(TeacherLayerConfig.Key);
                if (!c.Exists)
                {
                    return false;
                }
                return CheckEquality(c.Value.Value, config);
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

        static bool CheckEquality(TeacherLayerConfig existing, TeacherLayerConfig scoped)
        {
            if (!existing.TeacherName.Equals(scoped.TeacherName))
            {
                return false;
            }
            return true;
        }
    }
}
