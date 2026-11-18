using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>How a request to end a scope relates to the one ending operation it shares.</summary>
/// <param name="Completion">The scope's failure to end cleanly, or null.</param>
/// <param name="StartedHere">Whether this request started the ending; later requests only wait.</param>
internal readonly record struct ScopeEnding(Task<Exception?> Completion, bool StartedHere)
{
    /// <summary>Waits for the ending; only the request that started it reports its failure.</summary>
    public async ValueTask ThrowIfStartedHereAndFailed()
    {
        var failure = await Completion;
        if (StartedHere && failure is not null)
        {
            throw failure;
        }
    }
}

/// <summary>
/// Per-scope bookkeeping: the values a fork can inherit, open forks and resources.
/// A planned scope ends through <see cref="EndAsync"/>, run once for its owner and its parent
/// alike, and so does an <see cref="InvocationScope"/>. A raw scope ends when Microsoft DI
/// disposes it, which a failing service can prevent.
/// </summary>
public sealed class ScopeState : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly HashSet<ScopeState> _openForks = [];
    private readonly TaskCompletionSource _openSettled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScopeState? _parent;
    private Type? _root;
    private Func<CleanupFailures, ValueTask>? _disposeServices;
    private TaskCompletionSource<Exception?>? _ending;

    /// <summary>Seeds and initialized values available in this scope.</summary>
    internal Dictionary<ScopeNode, object> Values { get; } = [];

    internal ScopeResources Resources { get; } = new();

    /// <summary>
    /// Makes the scope's owner end it through <see cref="EndAsync"/>, which disposes the services
    /// with <paramref name="disposeServices"/> after the forks and before the resources.
    /// Microsoft DI disposing the scope then no longer ends it.
    /// </summary>
    internal void UseServicesTeardown(Func<CleanupFailures, ValueTask> disposeServices)
    {
        _disposeServices = disposeServices;
    }

    /// <summary>
    /// Creates a fork with <paramref name="createFork"/> and registers it as open, before any
    /// initializer runs. Both happen under this scope's lock, so this scope cannot start ending,
    /// and its container cannot be disposed, between the check and the registration.
    /// Fails once this scope has started ending.
    /// </summary>
    internal ScopeState AttachFork(Type root, Func<ScopeState> createFork)
    {
        lock (_lock)
        {
            if (_ending is not null)
            {
                throw new ParentScopeEndedException(root);
            }
            var fork = createFork();
            fork._parent = this;
            fork._root = root;
            _openForks.Add(fork);
            return fork;
        }
    }

    /// <summary>
    /// Settles a fork that will not be returned, and tells whether its parent had started ending
    /// by then. The opener first starts ending the fork, so a parent waiting for it joins that
    /// ending and never reports the fork as left open.
    /// </summary>
    internal bool SettleUnpublished()
    {
        var parent = _parent!;
        TaskCompletionSource<Exception?>? parentEnding;
        lock (parent._lock)
        {
            _openSettled.TrySetResult();
            parentEnding = parent._ending;
        }
        if (parentEnding is null)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Settles this fork as returned to its opener, unless the parent has started ending.
    /// Checking and settling under the parent's lock means a parent that starts ending either
    /// sees the fork returned, and ends it as left open, or makes this fail.
    /// </summary>
    internal bool TryPublish()
    {
        var parent = _parent!;
        lock (parent._lock)
        {
            if (parent._ending is not null)
            {
                return false;
            }
            _openSettled.TrySetResult();
            return true;
        }
    }

    private void RemoveFork(ScopeState fork)
    {
        lock (_lock)
        {
            _openForks.Remove(fork);
        }
    }

    /// <summary>Starts ending the scope, or joins the ending already under way.</summary>
    internal ScopeEnding EndAsync()
    {
        TaskCompletionSource<Exception?> ending;
        ScopeState[] forks;
        lock (_lock)
        {
            if (_ending is not null)
            {
                return new(_ending.Task, StartedHere: false);
            }
            ending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _ending = ending;
            forks = [.. _openForks];
        }
        return new(EndAndPublishAsync(forks, ending), StartedHere: true);
    }

    private async Task<Exception?> EndAndPublishAsync(ScopeState[] forks, TaskCompletionSource<Exception?> ending)
    {
        try
        {
            var failure = await RunEndingAsync(forks);
            ending.SetResult(failure);
            return failure;
        }
        catch (Exception unexpected)
        {
            ending.SetException(unexpected);
            throw;
        }
    }

    /// <summary>
    /// Ends forks left open, waiting for any still opening, then disposes the services, then
    /// closes the resources. Every step is attempted; the failures are reported together.
    /// </summary>
    private async Task<Exception?> RunEndingAsync(ScopeState[] forks)
    {
        var failures = new CleanupFailures();
        var leftOpen = new List<Type>();
        try
        {
            foreach (var fork in forks)
            {
                await fork._openSettled.Task;
                // Whoever started a fork's ending reports its failure; joining only waits.
                var ending = fork.EndAsync();
                var forkFailure = await ending.Completion;
                if (!ending.StartedHere)
                {
                    continue;
                }
                leftOpen.Add(fork._root!);
                if (forkFailure is not null)
                {
                    failures.Add(forkFailure);
                }
            }
            if (_disposeServices is not null)
            {
                await _disposeServices(failures);
            }
            await Resources.CloseAsync(failures);
        }
        finally
        {
            _parent?.RemoveFork(this);
        }

        ImmutableArray<Type> forksLeftOpen = [.. leftOpen];
        if (failures.Failures.Count > 0)
        {
            return new ScopeCleanupException(forksLeftOpen, [.. failures.Failures]);
        }
        if (forksLeftOpen.Length > 0)
        {
            return new ForkLeftOpenException(forksLeftOpen);
        }
        return null;
    }

    /// <summary>
    /// Microsoft DI disposing the scope. A planned or invocation scope is already ending through
    /// its owner, which disposes the services as one of its steps, so only a raw scope ends here.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposeServices is not null)
        {
            return;
        }
        await EndAsync().ThrowIfStartedHereAndFailed();
    }
}
