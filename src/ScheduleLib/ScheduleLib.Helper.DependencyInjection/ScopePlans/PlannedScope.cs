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

    internal PlannedScope(AsyncServiceScope scope)
    {
        _scope = scope;
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public THandler Handler => _handler ??= ActivatorUtilities.GetServiceOrCreateInstance<THandler>(Services);

    public ValueTask DisposeAsync()
    {
        return _scope.DisposeAsync();
    }
}
