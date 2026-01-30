using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib.Helper;

public sealed class LimitedTaskRunnerProvider : IDisposable
{
    private readonly SemaphoreSlim _semaphore;

    public LimitedTaskRunnerProvider(int maxConcurrentTasks)
    {
        _semaphore = new(
            initialCount: maxConcurrentTasks,
            maxCount: maxConcurrentTasks);
    }

    public LimitedTaskRunner Create(CancellationToken cancellationToken)
    {
        return new(
            _semaphore,
            cancellationToken);
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

public sealed class LimitedTaskRunner : IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly List<Task> _tasks;
    private readonly CancellationTokenSource _cts;
    public CancellationToken CancellationToken => _cts.Token;

    public LimitedTaskRunner(
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _semaphore = semaphore;
        _tasks = new();
    }

    public Task WhenDone()
    {
        return Task.WhenAll(_tasks);
    }

    public void Add(Func<CancellationToken, Task> taskFactory)
    {
        var t = Task.Run([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async () =>
        {
            await _semaphore.WaitAsync(CancellationToken);
            try
            {
                await taskFactory(CancellationToken);
            }
            finally
            {
                _semaphore.Release();
            }
        }, CancellationToken);
        _tasks.Add(t);
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}
