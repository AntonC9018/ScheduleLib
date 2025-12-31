namespace Anton.LayeredConfig.Retrieval;

// Type-erased marker config.
// Should be cast in GetCurrentPath.
public readonly record struct MarkerConfig(object Value);

public interface IMarkerConfigHelper
{
    MarkerConfig GetMarkerConfig(IServiceProvider sp);
    LayerPath? GetCurrentPath(MarkerConfig config);
}

public abstract class MarkerConfigHelperBase<T> : IMarkerConfigHelper
    where T : class, IConfig<T>
{
    MarkerConfig IMarkerConfigHelper.GetMarkerConfig(IServiceProvider sp)
    {
        var c = GetMarkerConfig(sp);
        return new(c);
    }
    LayerPath? IMarkerConfigHelper.GetCurrentPath(MarkerConfig config)
    {
        if (config.Value is not T c)
        {
            throw new InvalidOperationException("Marker config type mismatch.");
        }
        return GetCurrentPath(c);
    }

    protected abstract T GetMarkerConfig(IServiceProvider sp);
    protected abstract LayerPath? GetCurrentPath(T config);
}

