using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public sealed record ScopePlanStep(Type Value, Type Initializer);

/// <summary>What one workflow needs: the seeds it takes and the initializers it runs, in order.</summary>
public sealed class ScopePlan
{
    internal ScopePlan(Type root, ScopeClosure closure, ScopeValueCatalog values)
    {
        Root = root;
        Closure = closure;
        Steps = [.. closure.InitializedValues.Select(v => StepOf(v, values))];
        Errors = [.. closure.Issues.Where(static i => i.IsError)];
        Warnings = [.. closure.Issues.Where(static i => !i.IsError)];
    }

    public Type Root { get; }
    public ScopeClosure Closure { get; }
    public ImmutableArray<ScopeNode> Seeds => Closure.Seeds;

    /// <summary>Initializers in run order: dependencies first, then constructor order.</summary>
    public ImmutableArray<ScopePlanStep> Steps { get; }

    public ImmutableArray<ScopePlanIssue> Errors { get; }
    public ImmutableArray<ScopePlanIssue> Warnings { get; }

    private static ScopePlanStep StepOf(Type value, ScopeValueCatalog values)
    {
        var initializer = values.InitializerOf(ScopeNode.Unkeyed(value));
        return new(Value: value, Initializer: initializer);
    }
}

/// <summary>The plan of the workflow rooted at <typeparamref name="THandler"/>, built on first use.</summary>
public sealed class ScopePlan<THandler>(ScopePlanGraph graph)
{
    private ScopePlan? _plan;

    public ScopePlan Plan => _plan ??= graph.GetPlan(typeof(THandler));
}
