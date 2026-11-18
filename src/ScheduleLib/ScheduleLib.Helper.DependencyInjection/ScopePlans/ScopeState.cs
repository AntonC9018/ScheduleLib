using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Per-scope bookkeeping: the values a fork can inherit, open forks and resources.
/// A planned scope resolves it first, so Microsoft DI disposes it last.
/// </summary>
public sealed class ScopeState : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly HashSet<ScopeState> _openForks = [];
    private ScopeState? _parent;
    private AsyncServiceScope? _owner;
    private Type? _root;
    private bool _ending;

    /// <summary>Seeds and initialized values available in this scope.</summary>
    internal Dictionary<ScopeNode, object> Values { get; } = [];

    internal ScopeResources Resources { get; } = new();

    /// <summary>
    /// Registers this scope as an open fork of <paramref name="parent"/>, before any initializer
    /// runs. Fails once the parent has started ending, since the fork would borrow closed values.
    /// </summary>
    internal void AttachToParent(ScopeState parent, AsyncServiceScope owner, Type root)
    {
        _owner = owner;
        _root = root;
        parent.AddFork(this);
        _parent = parent;
    }

    private void AddFork(ScopeState fork)
    {
        lock (_lock)
        {
            if (_ending)
            {
                throw new ParentScopeEndedException(fork._root!);
            }
            _openForks.Add(fork);
        }
    }

    private void RemoveFork(ScopeState fork)
    {
        lock (_lock)
        {
            _openForks.Remove(fork);
        }
    }

    /// <summary>
    /// Disposes forks left open, then closes the resources. Every step is attempted even when an
    /// earlier one fails; the failures are reported together afterwards.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        ScopeState[] leftOpen;
        lock (_lock)
        {
            if (_ending)
            {
                return;
            }
            _ending = true;
            leftOpen = [.. _openForks];
        }

        var failures = new List<Exception>();
        try
        {
            foreach (var fork in leftOpen)
            {
                await CleanupStep.Run(() => fork._owner!.Value.DisposeAsync(), failures);
            }
            await Resources.CloseAsync(failures);
        }
        finally
        {
            _parent?.RemoveFork(this);
        }

        ImmutableArray<Type> forksLeftOpen = [.. leftOpen.Select(static f => f._root!)];
        if (failures.Count > 0)
        {
            throw new ScopeCleanupException(forksLeftOpen, [.. failures]);
        }
        if (forksLeftOpen.Length > 0)
        {
            throw new ForkLeftOpenException(forksLeftOpen);
        }
    }
}

/// <summary>Runs one cleanup step and records its failure, so the remaining steps still run.</summary>
internal static class CleanupStep
{
    public static async ValueTask Run(Func<ValueTask> step, List<Exception> failures)
    {
        try
        {
            await step();
        }
#pragma warning disable CA1031 // Every failure is collected and rethrown once cleanup is complete.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failures.Add(exception);
        }
    }
}
