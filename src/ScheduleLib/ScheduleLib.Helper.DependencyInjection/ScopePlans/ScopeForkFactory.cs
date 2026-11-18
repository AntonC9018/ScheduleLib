using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Opens a child scope of the scope that resolved it, for the workflow rooted at
/// <typeparamref name="THandler"/>. Resolved from a plain scope, there is nothing to inherit.
/// </summary>
/// <remarks>
/// A fork inherits the parent's seeds and initialized values by reference. Seeds the caller
/// supplies replace inherited ones; an initialized value is rebuilt when one of its seeds was
/// replaced, or when it is built through dependencies the plan cannot see.
/// </remarks>
public sealed class ScopeForkFactory<THandler>(
    ScopePlan<THandler> plan,
    ScopePlanGraph graph,
    ScopeState current,
    IServiceScopeFactory scopes)
    where THandler : notnull
{
    public ScopePlan Plan => plan.Plan;

    /// <summary>
    /// Registers the fork with its parent, checks the plan and the seeds before any initializer
    /// runs, fills the seeds, runs the initializers the fork does not inherit, and only then
    /// returns the scope. A parent that starts ending meanwhile waits for the fork to settle.
    /// </summary>
    public async ValueTask<PlannedScope<THandler>> OpenAsync(
        Action<ScopeInputs>? supply,
        CancellationToken cancellationToken)
    {
        var (planned, state) = await AttachAsync();
        Exception? openFailure = null;
        try
        {
            var inputs = CheckInputs(supply);
            await FillAsync(planned.Services, state, plan.Plan, inputs, cancellationToken);
            planned.ResolveHandler();
            if (state.TryPublish())
            {
                return planned;
            }
        }
        catch (Exception failure)
        {
            openFailure = failure;
        }

        var failures = await EndUnpublishedAsync(state, openFailure);
        if (failures is [var only])
        {
            ExceptionDispatchInfo.Throw(only);
        }
        throw new AggregateException(failures);
    }

    /// <summary>
    /// Creates the fork's container scope and registers it as an open fork of the parent, under
    /// the parent's lock, so the parent waits for it from here on. A failure disposes the scope.
    /// </summary>
    private async ValueTask<(PlannedScope<THandler> Planned, ScopeState State)> AttachAsync()
    {
        AsyncServiceScope? scope = null;
        PlannedScope<THandler>? planned = null;
        try
        {
            var state = current.AttachFork(typeof(THandler), () =>
            {
                var created = scopes.CreateAsyncScope();
                scope = created;
                var forkState = created.ServiceProvider.GetRequiredService<ScopeState>();
                planned = new PlannedScope<THandler>(created, forkState);
                forkState.UseServicesTeardown(planned.DisposeServicesAsync);
                return forkState;
            });
            return (planned!, state);
        }
        catch (Exception attachFailure)
        {
            if (scope is not { } created)
            {
                throw;
            }
            var failures = new CleanupFailures();
            await failures.Run(created.DisposeAsync);
            failures.ThrowIfAnyAlongside(attachFailure);
            throw;
        }
    }

    /// <summary>Rejects plan errors, values that are not seeds and missing seeds, before any initializer runs.</summary>
    private ScopeInputs CheckInputs(Action<ScopeInputs>? supply)
    {
        var scopePlan = plan.Plan;
        if (scopePlan.Errors.Length > 0)
        {
            throw new ScopePlanErrorsException(scopePlan.Root, scopePlan.Errors);
        }
        var inputs = new ScopeInputs();
        supply?.Invoke(inputs);
        var seeds = graph.Registrations.Seeds;
        ImmutableArray<ScopeNode> unregistered = [.. inputs.Seeds.Keys.Where(n => !seeds.ContainsKey(n))];
        if (unregistered.Length > 0)
        {
            throw new UnregisteredSeedsException(scopePlan.Root, unregistered);
        }
        ImmutableArray<ScopeNode> missing = [.. scopePlan.Seeds.Where(n => !IsSupplied(inputs, n))];
        if (missing.Length > 0)
        {
            throw new MissingSeedsException(scopePlan.Root, missing);
        }
        return inputs;
    }

    /// <summary>
    /// Ends a fork that is never returned to the caller, then lets a waiting parent continue.
    /// Returns what to report: that the parent ended, when it started ending before the fork
    /// settled, then <paramref name="openFailure"/>, then any failure to end the fork.
    /// </summary>
    private static async ValueTask<Exception[]> EndUnpublishedAsync(ScopeState state, Exception? openFailure)
    {
        var ending = state.EndAsync();
        Debug.Assert(ending.StartedHere, "Nothing else ends a fork before it is returned or settled.");
        bool parentEnded = state.SettleUnpublished();
        Debug.Assert(parentEnded || openFailure is not null, "A fork is only rejected by a parent that is ending.");
        var cleanupFailure = await ending.Completion;

        var failures = new List<Exception>();
        if (parentEnded)
        {
            failures.Add(new ParentScopeEndedException(typeof(THandler)));
        }
        if (openFailure is not null)
        {
            failures.Add(openFailure);
        }
        if (cleanupFailure is not null)
        {
            failures.Add(cleanupFailure);
        }
        return [.. failures];
    }

    private bool IsSupplied(ScopeInputs inputs, ScopeNode seed)
    {
        return inputs.Seeds.ContainsKey(seed) || current.Values.ContainsKey(seed);
    }

    private async ValueTask FillAsync(
        IServiceProvider services,
        ScopeState state,
        ScopePlan scopePlan,
        ScopeInputs inputs,
        CancellationToken cancellationToken)
    {
        foreach (var (node, value) in Inherited(inputs))
        {
            Fill(services, state, node, value);
        }
        foreach (var (node, value) in inputs.Seeds)
        {
            Fill(services, state, node, value);
        }
        foreach (var step in scopePlan.Steps)
        {
            var node = ScopeNode.Unkeyed(step.Value);
            if (state.Values.ContainsKey(node))
            {
                continue;
            }
            var initializer = (IInitializer) services.GetRequiredService(step.Initializer);
            var value = await initializer.InitializeUntypedAsync(cancellationToken);
            ScopeValueGuard.RejectDisposable(node, value);
            Fill(services, state, node, value);
        }
    }

    // Values the fork's own plan does not need are carried too, for grandchildren.
    private IEnumerable<KeyValuePair<ScopeNode, object>> Inherited(ScopeInputs inputs)
    {
        var replaced = inputs.Seeds.Keys.Where(current.Values.ContainsKey).ToHashSet();
        return current.Values.Where(entry => CanInherit(entry.Key, replaced));
    }

    private bool CanInherit(ScopeNode node, HashSet<ScopeNode> replacedSeeds)
    {
        if (graph.Registrations.Seeds.ContainsKey(node))
        {
            if (replacedSeeds.Contains(node))
            {
                return false;
            }
            return true;
        }
        var closure = graph.GetNode(node).Closure;
        if (closure.IsOpaque)
        {
            return false;
        }
        if (closure.Seeds.Any(replacedSeeds.Contains))
        {
            return false;
        }
        return true;
    }

    private void Fill(IServiceProvider services, ScopeState state, ScopeNode node, object value)
    {
        var slotType = graph.Registrations.SlotTypeOf(node);
        var slot = node.Key is null
            ? services.GetRequiredService(slotType)
            : services.GetRequiredKeyedService(slotType, node.Key);
        ((ISlot) slot).Fill(value);
        state.Values[node] = value;
    }
}
