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
    /// A failure to end it after the work failed is thrown as a <see cref="WorkflowTeardownException"/>
    /// that keeps the work's failure.
    /// </summary>
    public static async Task<T> RunAsync<THandler, T>(
        this ScopeForkFactory<THandler> workflows,
        Action<ScopeInputs> inputs,
        Func<THandler, T> work,
        CancellationToken cancellationToken)
        where THandler : notnull
    {
        // Ends the scope only when the work throws and ending it then fails as well.
        await using var scope = await workflows.OpenAsync(inputs, cancellationToken);
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
            throw new WorkflowTeardownException(failure: failure, teardown: teardown);
        }
    }
}

/// <summary>A workflow failed, and ending its scope afterwards failed too.</summary>
public sealed class WorkflowTeardownException(Exception failure, Exception teardown)
    : Exception($"Ending the workflow failed: {teardown.Message}", failure)
{
    /// <summary>The workflow's own failure, which decides the command's outcome.</summary>
    public Exception Failure { get; } = failure;

    public Exception Teardown { get; } = teardown;

    /// <summary>The failure that decides a command's outcome: the workflow's own, if this wraps it.</summary>
    public static Exception FailureOf(Exception error)
    {
        if (error is WorkflowTeardownException teardown)
        {
            return teardown.Failure;
        }

        return error;
    }
}
