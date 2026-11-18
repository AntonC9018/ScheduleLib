namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Resources opened by the initializers of one scope. They close when that scope ends, after its
/// forks, so forks that borrowed values built on them never see them closed.
/// An initializer adds its resource here and returns a non-disposable value that uses it.
/// </summary>
public sealed class ScopeResources
{
    private readonly Lock _lock = new();
    private readonly List<IAsyncDisposable> _resources = [];
    private bool _closed;

    /// <summary>
    /// Takes ownership of <paramref name="resource"/>. Once the scope has started ending this
    /// throws, and the caller still owns the resource.
    /// </summary>
    public T Add<T>(T resource)
        where T : IAsyncDisposable
    {
        lock (_lock)
        {
            if (_closed)
            {
                throw new ScopeResourcesClosedException(typeof(T));
            }
            _resources.Add(resource);
        }
        return resource;
    }

    /// <summary>Closes every resource, the last added first, recording failures without stopping.</summary>
    internal async ValueTask CloseAsync(CleanupFailures failures)
    {
        IAsyncDisposable[] resources;
        lock (_lock)
        {
            _closed = true;
            resources = [.. _resources];
            _resources.Clear();
        }
        for (int i = resources.Length - 1; i >= 0; i--)
        {
            var resource = resources[i];
            await failures.Run(resource.DisposeAsync);
        }
    }
}
