using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

/// <summary>
/// Runs a command's work in a workflow scope that ends before the command reports its result,
/// so a failure to end the scope belongs to that result instead of following it.
/// </summary>
internal static class WorkflowRun
{
    /// <summary>
    /// Opens a workflow scope, runs <paramref name="work"/> on its handler and ends the scope.
    /// When opening or the work fails and cleaning up then fails too, both are thrown as one
    /// <see cref="CleanupAfterFailureException"/> that keeps the primary failure.
    /// </summary>
    public static async Task<T> RunAsync<THandler, T>(
        this ScopeForkFactory<THandler> workflows,
        Action<ScopeInputs> inputs,
        Func<THandler, T> work,
        CancellationToken cancellationToken)
        where THandler : notnull
    {
        var scope = await workflows.OpenAsync(inputs, cancellationToken);
        T result;
        try
        {
            result = work(scope.Handler);
        }
        catch (Exception failure)
        {
            await EndAfterFailure(scope, failure);
            throw;
        }

        await scope.DisposeAsync();
        return result;
    }

    /// <inheritdoc cref="RunAsync{THandler, T}"/>
    public static Task RunAsync<THandler>(
        this ScopeForkFactory<THandler> workflows,
        Action<ScopeInputs> inputs,
        Action<THandler> work,
        CancellationToken cancellationToken)
        where THandler : notnull
    {
        bool Run(THandler handler)
        {
            work(handler);
            return true;
        }

        return workflows.RunAsync(inputs, Run, cancellationToken);
    }

    private static async ValueTask EndAfterFailure(IAsyncDisposable scope, Exception failure)
    {
        try
        {
            await scope.DisposeAsync();
        }
        catch (Exception teardown)
        {
            throw CleanupAfterFailureException.Create(failure, [teardown]);
        }
    }
}
