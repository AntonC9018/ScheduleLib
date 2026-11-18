namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Resources opened by the initializers of one scope. They close when that scope ends, after its
/// forks, so forks that borrowed values built on them never see them closed.
/// An initializer adds its resource here and returns a non-disposable value that uses it.
/// </summary>
public sealed class ScopeResources
{
    private readonly List<IAsyncDisposable> _resources = [];

    public T Add<T>(T resource)
        where T : IAsyncDisposable
    {
        _resources.Add(resource);
        return resource;
    }

    internal async ValueTask CloseAsync()
    {
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            await _resources[i].DisposeAsync();
        }
        _resources.Clear();
    }
}
