using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public enum ScopePlanIssueKind
{
    /// <summary>Error: a constructor parameter has no registration.</summary>
    MissingRegistration,

    /// <summary>Error: the dependency graph has a cycle.</summary>
    Cycle,

    /// <summary>Error: a seed, derived or initialized value type is disposable.</summary>
    DisposableValue,

    /// <summary>Warning: a service injects a service locator; what it resolves is invisible.</summary>
    ServiceLocator,

    /// <summary>Warning: a service is built by a lambda registration; its inputs are invisible.</summary>
    LambdaRegistration,
}

/// <summary>
/// A problem found while building a scope plan. Errors reject the scope before any work;
/// warnings mark spots where a missing value can only fail at run time.
/// </summary>
/// <param name="Subject">The node that has the problem, usually the consumer.</param>
/// <param name="Related">The dependency involved, or the cycle path.</param>
public sealed record ScopePlanIssue(ScopePlanIssueKind Kind, ScopeNode Subject, ImmutableArray<ScopeNode> Related)
{
    public bool IsError
    {
        get
        {
            return Kind is ScopePlanIssueKind.MissingRegistration
                or ScopePlanIssueKind.Cycle
                or ScopePlanIssueKind.DisposableValue;
        }
    }

    public override string ToString()
    {
        return Kind switch
        {
            ScopePlanIssueKind.MissingRegistration => $"{Subject} needs {Related[0]}, which is not registered",
            ScopePlanIssueKind.Cycle => $"cycle: {string.Join(" -> ", Related)}",
            ScopePlanIssueKind.DisposableValue =>
                $"{Subject} is disposable; register its resource with ScopeResources instead",
            ScopePlanIssueKind.ServiceLocator =>
                $"{Subject} injects {Related[0]}: what it resolves is invisible to the plan",
            ScopePlanIssueKind.LambdaRegistration =>
                $"{Subject} needs {Related[0]}, which a lambda registration builds",
            _ => throw new ArgumentOutOfRangeException(paramName: nameof(Kind), actualValue: Kind, message: null),
        };
    }
}
