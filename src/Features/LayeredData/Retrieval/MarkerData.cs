namespace Anton.LayeredData.Retrieval;

// Type-erased marker config.
// Should be cast in GetCurrentPath.
public readonly record struct MarkerData(object Value);

public interface IMarkerDataHelper
{
    MarkerData GetMarkerData(IServiceProvider sp);
    NodePath? GetCurrentPath(MarkerData data);
}

public abstract class MarkerDataHelperBase<T> : IMarkerDataHelper
    where T : class, INodeData<T>
{
    MarkerData IMarkerDataHelper.GetMarkerData(IServiceProvider sp)
    {
        var c = GetMarkerData(sp);
        return new(c);
    }
    NodePath? IMarkerDataHelper.GetCurrentPath(MarkerData data)
    {
        if (data.Value is not T c)
        {
            throw new InvalidOperationException("Marker config type mismatch.");
        }
        var ret = GetCurrentPath(c);
        return ret;
    }

    public abstract T GetMarkerData(IServiceProvider sp);
    public abstract NodePath? GetCurrentPath(T marker);
}

