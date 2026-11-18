using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>Base type of failures raised while opening or closing planned scopes.</summary>
public abstract class ScopePlanException(string message) : InvalidOperationException(message);

/// <summary>A seed or initialized value was resolved in a scope that was not opened through its plan.</summary>
public sealed class ScopeValueNotAvailableException(Type valueType)
    : ScopePlanException($"{valueType.Name} is not available: open the scope through ScopeForkFactory")
{
    public Type ValueType { get; } = valueType;
}

public sealed class ScopeValueAlreadyFilledException(Type valueType)
    : ScopePlanException($"{valueType.Name} is already filled in this scope")
{
    public Type ValueType { get; } = valueType;
}

/// <summary>The workflow's plan has errors; nothing was initialized.</summary>
public sealed class ScopePlanErrorsException(Type root, ImmutableArray<ScopePlanIssue> errors)
    : ScopePlanException($"The plan for {root.Name} has errors: {string.Join("; ", errors)}")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopePlanIssue> Errors { get; } = errors;
}

/// <summary>Neither the caller nor the parent scope supplied these seeds; nothing was initialized.</summary>
public sealed class MissingSeedsException(Type root, ImmutableArray<ScopeNode> seeds)
    : ScopePlanException($"{root.Name} needs seeds [{string.Join(", ", seeds)}]; nothing was initialized")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopeNode> Seeds { get; } = seeds;
}

/// <summary>
/// A seed, derived or initialized value was disposable at run time.
/// Microsoft DI would dispose it in every scope that resolves it, including borrowing forks.
/// </summary>
public sealed class DisposableScopeValueException(ScopeNode node)
    : ScopePlanException($"{node} is disposable; register its resource with ScopeResources instead")
{
    public ScopeNode Node { get; } = node;
}

/// <summary>A scope ended while forks it opened were still open. They were disposed first.</summary>
public sealed class ForkLeftOpenException(ImmutableArray<Type> forks)
    : ScopePlanException(
        $"A scope ended with open forks [{string.Join(", ", forks.Select(f => f.Name))}]; dispose each fork you open")
{
    public ImmutableArray<Type> Forks { get; } = forks;
}
