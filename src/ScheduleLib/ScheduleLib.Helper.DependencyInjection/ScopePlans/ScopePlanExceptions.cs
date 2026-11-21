using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

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

public sealed class ScopePlanErrorsException(Type root, ImmutableArray<ScopePlanIssue> errors)
    : ScopePlanException($"The plan for {root.Name} has errors: {string.Join("; ", errors)}")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopePlanIssue> Errors { get; } = errors;
}

public sealed class MissingSeedsException(Type root, ImmutableArray<ScopeNode> seeds)
    : ScopePlanException($"{root.Name} needs seeds [{string.Join(", ", seeds)}]; nothing was initialized")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopeNode> Seeds { get; } = seeds;
}

/// <summary>
/// Microsoft DI would dispose a disposable value in every scope that resolves it, including
/// borrowing forks.
/// </summary>
public sealed class DisposableScopeValueException(ScopeNode node)
    : ScopePlanException($"{node} is disposable; register its resource with ScopeResources instead")
{
    public ScopeNode Node { get; } = node;
}

public sealed class ForkLeftOpenException(ImmutableArray<Type> forks)
    : ScopePlanException(
        $"A scope ended with open forks [{string.Join(", ", forks.Select(f => f.Name))}]; dispose each fork you open")
{
    public ImmutableArray<Type> Forks { get; } = forks;
}

/// <summary>Every ending step was still attempted; <see cref="Failures"/> holds each failure.</summary>
public sealed class ScopeCleanupException(ImmutableArray<Type> forksLeftOpen, ImmutableArray<Exception> failures)
    : ScopePlanException(
        $"Ending a scope failed {failures.Length} time(s); forks left open: [{string.Join(", ", forksLeftOpen.Select(f => f.Name))}]",
        new AggregateException(failures))
{
    public ImmutableArray<Type> ForksLeftOpen { get; } = forksLeftOpen;

    public ImmutableArray<Exception> Failures { get; } = failures;
}

public sealed class UnregisteredSeedsException(Type root, ImmutableArray<ScopeNode> seeds)
    : ScopePlanException($"{root.Name} was supplied [{string.Join(", ", seeds)}], which are not registered seeds")
{
    public Type Root { get; } = root;
    public ImmutableArray<ScopeNode> Seeds { get; } = seeds;
}

/// <summary>A cycle component did not lead back to its first member; a bug in scope plans.</summary>
internal sealed class CyclePathNotFoundException(ScopeNode start)
    : ScopePlanException($"The cycle component of {start} does not lead back to it")
{
    public ScopeNode Start { get; } = start;
}

public sealed class ParentScopeEndedException(Type fork)
    : ScopePlanException($"Cannot open a fork for {fork.Name}: its parent scope has started ending")
{
    public Type Fork { get; } = fork;
}

/// <summary>The caller still owns the rejected resource.</summary>
public sealed class ScopeResourcesClosedException(Type resourceType)
    : ScopePlanException($"Cannot add {resourceType.Name}: the scope's resources are already closed")
{
    public Type ResourceType { get; } = resourceType;
}

/// <summary>
/// An operation failed, and cleaning up after it failed too. <see cref="Failure"/> is the
/// operation's own failure, which decides the outcome; <see cref="CleanupExceptions"/> are reported
/// alongside it, in the order they happened.
/// </summary>
public sealed class CleanupAfterFailureException(Exception failure, ImmutableArray<Exception> cleanupExceptions)
    : Exception($"{failure.Message} Cleaning up afterwards failed {cleanupExceptions.Length} time(s).", failure)
{
    public Exception Failure { get; } = failure;

    public ImmutableArray<Exception> CleanupExceptions { get; } = cleanupExceptions;

    /// <summary>
    /// Pairs <paramref name="failure"/> with <paramref name="cleanupExceptions"/>. A failure that
    /// already carries cleanup failures keeps its own failure and gains the new ones after its own.
    /// </summary>
    public static CleanupAfterFailureException Create(Exception failure, ImmutableArray<Exception> cleanupExceptions)
    {
        if (failure is CleanupAfterFailureException paired)
        {
            return new(paired.Failure, [.. paired.CleanupExceptions, .. cleanupExceptions]);
        }

        return new(failure, cleanupExceptions);
    }

    /// <summary>The failure that decides the outcome of <paramref name="error"/>: its own unless it pairs one with cleanup failures.</summary>
    public static Exception FailureOf(Exception error)
    {
        if (error is CleanupAfterFailureException paired)
        {
            return paired.Failure;
        }

        return error;
    }
}

public sealed class DuplicateScopeValueException(ScopeNode value)
    : ScopePlanException($"{value} is already registered as a scope value; remove it with RemoveScopeValue first")
{
    public ScopeNode Value { get; } = value;
}

public sealed class ScopeValueNotRegisteredException(ScopeNode value)
    : ScopePlanException($"{value} is not registered as a scope value")
{
    public ScopeNode Value { get; } = value;
}

/// <summary>A scope value lacks the slot or initializer its use requires; a bug in scope plans.</summary>
internal sealed class MissingScopeValuePartException(ScopeNode value, string part)
    : ScopePlanException($"{value} has no registered {part}")
{
    public ScopeNode Value { get; } = value;
    public string Part { get; } = part;
}
