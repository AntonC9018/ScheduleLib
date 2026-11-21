using System.Collections.Immutable;

namespace ScheduleLib.DependencyInjection.ScopePlans;

/// <summary>
/// Nodes share closures: a node that adds nothing to its first contributing input reuses that
/// input's instance.
/// </summary>
public sealed class ScopeClosure
{
    public static readonly ScopeClosure Empty = new([], [], [], isOpaque: false);

    internal ScopeClosure(
        ImmutableArray<ScopeNode> seeds,
        ImmutableArray<Type> initializedValues,
        ImmutableArray<ScopePlanIssue> issues,
        bool isOpaque)
    {
        Seeds = seeds;
        InitializedValues = initializedValues;
        Issues = issues;
        IsOpaque = isOpaque;
    }

    public ImmutableArray<ScopeNode> Seeds { get; }

    /// <summary>Initialized value types in run order: dependencies first, then constructor order.</summary>
    public ImmutableArray<Type> InitializedValues { get; }

    public ImmutableArray<ScopePlanIssue> Issues { get; }

    /// <summary>Something below this node is invisible to the plan.</summary>
    public bool IsOpaque { get; }

    internal int Size => Seeds.Length + InitializedValues.Length + Issues.Length;
}

internal sealed class ScopeClosureBuilder
{
    private readonly List<ScopeNode> _seeds = [];
    private readonly List<Type> _initializedValues = [];
    private readonly List<ScopePlanIssue> _issues = [];
    private readonly HashSet<object> _seen = [];
    private ScopeClosure? _firstInput;
    private bool _isOpaque;
    private bool _hasOwnAdditions;

    public void AddInput(ScopeClosure input)
    {
        bool contributesNothing = input.Size == 0 && !input.IsOpaque;
        if (contributesNothing)
        {
            return;
        }
        _firstInput ??= input;
        _isOpaque |= input.IsOpaque;
        AddAll(_seeds, input.Seeds);
        AddAll(_initializedValues, input.InitializedValues);
        AddAll(_issues, input.Issues);
    }

    public void AddSeed(ScopeNode seed)
    {
        _hasOwnAdditions = true;
        Add(_seeds, seed);
    }

    public void AddInitializedValue(Type value)
    {
        _hasOwnAdditions = true;
        Add(_initializedValues, value);
    }

    public void AddIssue(ScopePlanIssue issue)
    {
        _hasOwnAdditions = true;
        _isOpaque |= issue.Kind is ScopePlanIssueKind.ServiceLocator or ScopePlanIssueKind.LambdaRegistration;
        Add(_issues, issue);
    }

    public ScopeClosure Build()
    {
        int size = _seeds.Count + _initializedValues.Count + _issues.Count;
        if (CanReuseFirstInput(size))
        {
            return _firstInput!;
        }
        bool isEmpty = size == 0 && !_isOpaque;
        if (isEmpty)
        {
            return ScopeClosure.Empty;
        }
        return new([.. _seeds], [.. _initializedValues], [.. _issues], _isOpaque);
    }

    private bool CanReuseFirstInput(int size)
    {
        if (_hasOwnAdditions || _firstInput is null)
        {
            return false;
        }
        if (_firstInput.Size != size)
        {
            return false;
        }
        if (_firstInput.IsOpaque != _isOpaque)
        {
            return false;
        }
        return true;
    }

    private void AddAll<T>(List<T> list, ImmutableArray<T> items)
        where T : notnull
    {
        foreach (var item in items)
        {
            Add(list, item);
        }
    }

    private void Add<T>(List<T> list, T item)
        where T : notnull
    {
        if (_seen.Add(item))
        {
            list.Add(item);
        }
    }
}
