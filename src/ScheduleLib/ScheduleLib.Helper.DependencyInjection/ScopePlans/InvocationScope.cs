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

    public InvocationScope(IServiceProvider root)
    {
        _scope = root.CreateAsyncScope();
        _state = _scope.ServiceProvider.GetRequiredService<ScopeState>();
        _state.UseServicesTeardown(failures => failures.Run(_scope.DisposeAsync));
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public async ValueTask DisposeAsync()
    {
        if (await _state.EndAsync().Completion is { } failure)
        {
            throw failure;
        }
    }
}
