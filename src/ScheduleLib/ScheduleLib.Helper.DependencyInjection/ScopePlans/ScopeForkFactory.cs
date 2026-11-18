using System.Collections.Immutable;
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
    /// Checks the plan and the seeds before any work, fills the seeds, runs the initializers the
    /// fork does not inherit, and only then returns the scope.
    /// </summary>
    public async ValueTask<PlannedScope<THandler>> OpenAsync(
        Action<ScopeInputs>? supply,
        CancellationToken cancellationToken)
    {
        var scopePlan = plan.Plan;
        if (scopePlan.Errors.Length > 0)
        {
            throw new ScopePlanErrorsException(scopePlan.Root, scopePlan.Errors);
        }
        var inputs = new ScopeInputs();
        supply?.Invoke(inputs);
        ImmutableArray<ScopeNode> missing = [.. scopePlan.Seeds.Where(n => !IsSupplied(inputs, n))];
        if (missing.Length > 0)
        {
            throw new MissingSeedsException(scopePlan.Root, missing);
        }

        var scope = scopes.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<ScopeState>();
        var planned = new PlannedScope<THandler>(scope, state);
        try
        {
            state.AttachToParent(current, scopePlan.Root, planned.DisposeServicesAsync);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }

        try
        {
            await FillAsync(scope.ServiceProvider, state, scopePlan, inputs, cancellationToken);
            state.MarkOpenSettled();
            return planned;
        }
        catch (Exception openFailure)
        {
            var ending = state.EndAsync();
            state.MarkOpenSettled();
            if (await ending.Completion is not { } cleanupFailure)
            {
                throw;
            }
            throw new AggregateException(openFailure, cleanupFailure);
        }
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
