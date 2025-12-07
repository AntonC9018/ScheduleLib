using System.Diagnostics;
using MainCli.BuilderNew.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MainCli.BuilderNew.Impl;

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
        var config = sp.GetRequiredService<IOptionsSnapshot<TeacherLayerConfig>>();
        return config.Value;
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
        Debug.Assert(path.Path[^1].ChildLayers.Count == 0);
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
