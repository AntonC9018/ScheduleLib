using System.Diagnostics;
using AutoConstructor.Attributes;

namespace MainCli.BuilderNew.Retrieval;

// Must be a scoped service.
// TODO: cache
[AutoConstructor]
public sealed partial class ConfigProvider
{
    private readonly IMarkerConfigHelper _helper;
    private readonly IServiceProvider _sp;

    public T GetConfig<T>()
        where T : class, IConfig<T>
    {
        var markerConfig = _helper.GetMarkerConfig(_sp);
        if (markerConfig.Value is T m)
        {
            return m;
        }
        if (_helper.GetCurrentPath(markerConfig) is not { } path)
        {
            throw new InvalidOperationException("No matching path found for the current configuration.");
        }

        var config = path.ConstructConfig<T>(_sp);
        Debug.Assert(config != null);
        return config;
    }
}
