using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public enum ScopePlanIssueKind
{
    MissingRegistration,
    Cycle,
    DisposableValue,
    ServiceLocator,
    LambdaRegistration,
}

/// <summary>
/// Errors reject the scope before any work; warnings mark spots where a missing value can only
/// fail at run time.
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

    /// <summary>Issues are equal by content, so the same problem found twice is reported once.</summary>
    public bool Equals(ScopePlanIssue? other)
    {
        if (other is null)
        {
            return false;
        }
        if (Kind != other.Kind || Subject != other.Subject)
        {
            return false;
        }
        if (!Related.SequenceEqual(other.Related))
        {
            return false;
        }
        return true;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(Subject);
        foreach (var node in Related)
        {
            hash.Add(node);
        }
        return hash.ToHashCode();
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
