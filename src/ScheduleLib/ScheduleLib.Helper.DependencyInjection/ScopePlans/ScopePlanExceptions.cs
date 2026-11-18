using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>Base type of failures raised while opening or closing planned scopes.</summary>
public abstract class ScopePlanException : InvalidOperationException
{
    protected ScopePlanException(string message)
        : base(message)
    {
    }

    protected ScopePlanException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

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

/// <summary>
/// Ending a scope failed: ending a fork left open, disposing a handler the scope created or its
/// container services, or closing a resource. Every step was still attempted;
/// <see cref="Failures"/> holds each failure, in the order it happened.
/// </summary>
public sealed class ScopeCleanupException(ImmutableArray<Type> forksLeftOpen, ImmutableArray<Exception> failures)
    : ScopePlanException(
        $"Ending a scope failed {failures.Length} time(s); forks left open: [{string.Join(", ", forksLeftOpen.Select(f => f.Name))}]",
        new AggregateException(failures))
{
    /// <summary>Forks that were still open when the scope ended, and were disposed by it.</summary>
    public ImmutableArray<Type> ForksLeftOpen { get; } = forksLeftOpen;

    public ImmutableArray<Exception> Failures { get; } = failures;
}

/// <summary>The caller supplied values that are not registered as seeds; nothing was initialized.</summary>
public sealed class UnregisteredSeedsException(Type root, ImmutableArray<ScopeNode> seeds)
    : ScopePlanException($"{root.Name} was supplied [{string.Join(", ", seeds)}], which are not registered seeds")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopeNode> Seeds { get; } = seeds;
}

/// <summary>A fork was opened from a scope that had already started ending.</summary>
public sealed class ParentScopeEndedException(Type fork)
    : ScopePlanException($"Cannot open a fork for {fork.Name}: its parent scope has started ending")
{
    public Type Fork { get; } = fork;
}

/// <summary>A resource was added after its scope had started ending; the caller still owns it.</summary>
public sealed class ScopeResourcesClosedException(Type resourceType)
    : ScopePlanException($"Cannot add {resourceType.Name}: the scope's resources are already closed")
{
    public Type ResourceType { get; } = resourceType;
}
