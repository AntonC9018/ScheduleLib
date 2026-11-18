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
    private THandler? _handler;
    private bool _handlerIsActivated;

    internal PlannedScope(AsyncServiceScope scope)
    {
        _scope = scope;
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>
    /// The registered handler, or else one created from the scope's services. A created handler
    /// is owned by this scope, since the container does not track it.
    /// </summary>
    public THandler Handler => _handler ??= ResolveHandler();

    private THandler ResolveHandler()
    {
        if (Services.GetService<THandler>() is { } registered)
        {
            return registered;
        }
        var activated = ActivatorUtilities.CreateInstance<THandler>(Services);
        _handlerIsActivated = true;
        return activated;
    }

    /// <summary>Disposes a handler this scope created, then the scope, even when the handler fails.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_handlerIsActivated)
            {
                await DisposeActivatedHandler(_handler!);
            }
        }
        finally
        {
            await _scope.DisposeAsync();
        }
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
