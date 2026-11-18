using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>Values the caller supplies explicitly when opening a scope.</summary>
public sealed class ScopeInputs
{
    internal Dictionary<ScopeNode, object> Seeds { get; } = [];

    public ScopeInputs Add<T>(T value)
        where T : class
    {
        return Add(ScopeNode.Unkeyed(typeof(T)), value);
    }

    public ScopeInputs AddKeyed<T>(object key, T value)
        where T : class
    {
        return Add(new(typeof(T), key), value);
    }

    private ScopeInputs Add(ScopeNode node, object value)
    {
        ScopeValueGuard.RejectDisposable(node, value);
        Seeds[node] = value;
        return this;
    }
}

/// <summary>
/// An opened scope whose seeds are filled and initializers have run.
/// Whoever opens it disposes it.
/// </summary>
public sealed class PlannedScope<THandler> : IAsyncDisposable
    where THandler : notnull
{
    private readonly AsyncServiceScope _scope;
    private readonly ScopeState _state;
    private THandler? _handler;
    private bool _handlerIsActivated;

    internal PlannedScope(AsyncServiceScope scope, ScopeState state)
    {
        _scope = scope;
        _state = state;
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>
    /// The registered handler, or else one created from the scope's services. It is resolved
    /// before the scope is returned, so it never races the scope ending. A created handler is
    /// owned by this scope, since the container does not track it.
    /// </summary>
    public THandler Handler => _handler!;

    internal void ResolveHandler()
    {
        if (Services.GetService<THandler>() is { } registered)
        {
            _handler = registered;
            return;
        }
        _handler = ActivatorUtilities.CreateInstance<THandler>(Services);
        _handlerIsActivated = true;
    }

    /// <summary>
    /// Ends the scope: open forks, then the services, then the resources. When the parent already
    /// ended this scope as a fork left open, the parent reports its failures instead.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _state.EndAsync().ThrowIfStartedHereAndFailed();
    }

    /// <summary>The services step of ending: a handler this scope created, then the container scope.</summary>
    internal async ValueTask DisposeServicesAsync(CleanupFailures failures)
    {
        if (_handlerIsActivated)
        {
            await failures.Run(() => DisposeActivatedHandler(_handler!));
        }
        await failures.Run(_scope.DisposeAsync);
    }

    private static async ValueTask DisposeActivatedHandler(THandler handler)
    {
        switch (handler)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
