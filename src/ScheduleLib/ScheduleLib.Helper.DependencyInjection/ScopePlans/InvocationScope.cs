using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// A plain scope that workflows are opened from, such as the CLI's per-invocation scope.
/// Ending it ends the forks opened from it, then its services, then its resources, even when
/// disposing one of its services fails.
/// </summary>
public sealed class InvocationScope : IAsyncDisposable
{
    private readonly AsyncServiceScope _scope;
    private readonly ScopeState _state;

    private InvocationScope(AsyncServiceScope scope, ScopeState state)
    {
        _scope = scope;
        _state = state;
        _state.UseServicesTeardown(failures => failures.Run(_scope.DisposeAsync));
    }

    public static async ValueTask<InvocationScope> OpenAsync(IServiceProvider root)
    {
        var scope = root.CreateAsyncScope();
        try
        {
            var state = scope.ServiceProvider.GetRequiredService<ScopeState>();
            return new InvocationScope(scope, state);
        }
        catch (Exception openFailure)
        {
            var failures = new CleanupFailures();
            await failures.Run(scope.DisposeAsync);
            failures.ThrowIfAnyAlongside(openFailure);
            throw;
        }
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    /// <summary>Ends the scope; only the first call reports its failure.</summary>
    public async ValueTask DisposeAsync()
    {
        await _state.EndAsync().ThrowIfStartedHereAndFailed();
    }
}
