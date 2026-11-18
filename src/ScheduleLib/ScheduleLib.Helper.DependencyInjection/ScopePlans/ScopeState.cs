using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Per-scope bookkeeping: the values a fork can inherit, open forks and resources.
/// A planned scope resolves it first, so Microsoft DI disposes it last.
/// </summary>
public sealed class ScopeState : IAsyncDisposable
{
    private readonly HashSet<ScopeState> _openForks = [];
    private ScopeState? _parent;
    private AsyncServiceScope? _owner;
    private Type? _root;

    /// <summary>Seeds and initialized values available in this scope.</summary>
    internal Dictionary<ScopeNode, object> Values { get; } = [];

    internal ScopeResources Resources { get; } = new();

    internal void AttachToParent(ScopeState parent, AsyncServiceScope owner, Type root)
    {
        _parent = parent;
        _owner = owner;
        _root = root;
        parent._openForks.Add(this);
    }

    public async ValueTask DisposeAsync()
    {
        var leftOpen = _openForks.ToList();
        foreach (var fork in leftOpen)
        {
            await fork._owner!.Value.DisposeAsync();
        }
        await Resources.CloseAsync();
        _parent?._openForks.Remove(this);
        if (leftOpen.Count > 0)
        {
            throw new ForkLeftOpenException([.. leftOpen.Select(static f => f._root!)]);
        }
    }
}
