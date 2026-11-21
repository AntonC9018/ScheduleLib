using System.Runtime.ExceptionServices;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Runs cleanup steps that must all be attempted, collecting their failures instead of stopping
/// at the first one.
/// </summary>
public sealed class CleanupFailures
{
    private readonly List<Exception> _failures = [];

    public IReadOnlyList<Exception> Failures => _failures;

    public void Add(Exception failure)
    {
        _failures.Add(failure);
    }

    public async ValueTask Run(Func<ValueTask> step)
    {
        try
        {
            await step();
        }
#pragma warning disable CA1031 // Every failure is collected and reported once cleanup is complete.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            _failures.Add(failure);
        }
    }

    /// <summary>
    /// After a failure, <paramref name="primary"/>, led to cleanup: throws a
    /// <see cref="CleanupAfterFailureException"/> pairing it with the cleanup failures, if any.
    /// Otherwise returns, and the caller rethrows the primary failure.
    /// </summary>
    public void ThrowIfAnyAlongside(Exception primary)
    {
        if (_failures.Count > 0)
        {
            throw CleanupAfterFailureException.Create(primary, [.. _failures]);
        }
    }

    public void ThrowIfAny()
    {
        if (_failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(_failures[0]);
        }
        if (_failures.Count > 1)
        {
            throw new AggregateException(_failures);
        }
    }
}
