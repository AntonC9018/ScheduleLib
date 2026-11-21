using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public enum ScopeNodeKind
{
    Seed,
    Derived,
    Initialized,

    Service,

    /// <summary>A singleton cannot depend on scoped values, so the plan does not walk into it.</summary>
    Singleton,

    Collection,

    /// <summary>Scope-plan or container infrastructure that needs nothing from the scope.</summary>
    Infrastructure,

    ServiceLocator,
    Lambda,
    Missing,
}

public sealed record ScopeNodeInfo(ScopeNode Node, ScopeNodeKind Kind, ImmutableArray<ScopeNode> Inputs, ScopeClosure Closure);

/// <summary>
/// One node per (type, key) with a memoized closure, so plans of different workflows share common
/// subgraphs. Assumes scope validation: singletons never depend on scoped services.
/// </summary>
public sealed class ScopePlanGraph
{
    private readonly ServiceIndex _index;
    private readonly Dictionary<ScopeNode, ScopeNodeInfo> _nodes = [];
    private readonly Dictionary<Type, ScopePlan> _plans = [];
    private readonly Lock _lock = new();

    /// <summary>
    /// Indexes <paramref name="services"/> now and does not keep the collection. A value whose
    /// registration was removed or replaced by ordinary collection edits is planned as whatever
    /// now provides it.
    /// </summary>
    internal ScopePlanGraph(
        IServiceCollection services,
        IEnumerable<ScopeValueRegistration> values,
        IEnumerable<HiddenNeed> hiddenNeeds)
    {
        _index = new(services);
        var effective = EffectiveDescriptors(services);
        Values = new(values.Where(v => IsEffective(v, effective)), hiddenNeeds);
    }

    /// <summary>The descriptor Microsoft DI resolves for each service: the last one registered.</summary>
    private static Dictionary<ScopeNode, ServiceDescriptor> EffectiveDescriptors(IServiceCollection services)
    {
        var effective = new Dictionary<ScopeNode, ServiceDescriptor>();
        foreach (var descriptor in services)
        {
            effective[new(descriptor.ServiceType, descriptor.ServiceKey)] = descriptor;
        }
        return effective;
    }

    private static bool IsEffective(
        ScopeValueRegistration value,
        Dictionary<ScopeNode, ServiceDescriptor> effective)
    {
        if (!effective.TryGetValue(value.Node, out var descriptor))
        {
            return false;
        }
        if (value.OwnedDescriptors.Contains(descriptor))
        {
            return true;
        }
        return false;
    }

    internal ScopeValueCatalog Values { get; }

    public ScopePlan GetPlan(Type root)
    {
        lock (_lock)
        {
            if (_plans.TryGetValue(root, out var existing))
            {
                return existing;
            }
            var closure = new Walk(this).VisitRoot(ScopeNode.Unkeyed(root));
            var plan = new ScopePlan(root, closure, Values);
            _plans[root] = plan;
            return plan;
        }
    }

    public ScopeNodeInfo GetNode(ScopeNode node)
    {
        lock (_lock)
        {
            if (!_nodes.ContainsKey(node))
            {
                new Walk(this).Visit(node);
            }
            return _nodes[node];
        }
    }

    private readonly record struct NodeDescription(ScopeNodeKind Kind, ImmutableArray<ScopeNode> Inputs)
    {
        public bool HasLambdaMember { get; init; }
    }

    private sealed class WalkEntry(ScopeNode node, NodeDescription description, int index)
    {
        public ScopeNode Node => node;
        public NodeDescription Description => description;
        public int Index { get; } = index;

        /// <summary>The smallest index reachable from this node among nodes not yet memoized.</summary>
        public int LowLink { get; set; } = index;
    }

    /// <summary>
    /// One depth-first walk (Tarjan's algorithm). Each strongly connected component is memoized
    /// once it is complete, so every node is expanded once and its closure never depends on which
    /// root reached it first. A component with a cycle is one error, and its members share one
    /// closure, because each reaches the others. Members are ordered by registration, so the
    /// reported cycle never depends on which root reached it first.
    /// </summary>
    private sealed class Walk(ScopePlanGraph graph)
    {
        private readonly Dictionary<ScopeNode, WalkEntry> _entries = [];
        private readonly Stack<WalkEntry> _component = new();

        /// <summary>
        /// Plans a workflow root. A root may be a concrete type that is not registered: it is then
        /// activated from its constructor, but that node is not memoized, because for every other
        /// consumer the type stays missing. The result therefore never depends on planning order.
        /// </summary>
        public ScopeClosure VisitRoot(ScopeNode root)
        {
            var description = Describe(root);
            if (description.Kind != ScopeNodeKind.Missing)
            {
                if (!graph._nodes.ContainsKey(root))
                {
                    Connect(root, description);
                }
                return graph._nodes[root].Closure;
            }
            var activated = new NodeDescription(ScopeNodeKind.Service, ImplementationInputs(root.Type, key: null));
            foreach (var input in activated.Inputs)
            {
                Visit(input);
            }
            var builder = new ScopeClosureBuilder();
            AddExpansion(root, activated, builder, members: []);
            return builder.Build();
        }

        public ScopeClosure Visit(ScopeNode node)
        {
            if (!graph._nodes.ContainsKey(node))
            {
                Connect(node, Describe(node));
            }
            return graph._nodes[node].Closure;
        }

        private WalkEntry Connect(ScopeNode node, NodeDescription description)
        {
            var entry = new WalkEntry(node, description, _entries.Count);
            _entries[node] = entry;
            _component.Push(entry);
            foreach (var input in entry.Description.Inputs)
            {
                if (graph._nodes.ContainsKey(input))
                {
                    continue;
                }
                // A visited node that is not memoized is still on the component stack.
                var reached = _entries.TryGetValue(input, out var visited)
                    ? visited
                    : Connect(input, Describe(input));
                entry.LowLink = Math.Min(entry.LowLink, reached.LowLink);
            }
            if (entry.LowLink == entry.Index)
            {
                MemoizeComponent(PopComponent(entry));
            }
            return entry;
        }

        private List<WalkEntry> PopComponent(WalkEntry root)
        {
            var members = new List<WalkEntry>();
            WalkEntry member;
            do
            {
                member = _component.Pop();
                members.Add(member);
            }
            while (member != root);
            return members;
        }

        private void MemoizeComponent(List<WalkEntry> component)
        {
            var members = component.Select(m => m.Node).ToHashSet();
            var only = component[0];
            var isCycle = component.Count > 1 || only.Description.Inputs.Contains(only.Node);
            var ordered = component
                .OrderBy(m => graph._index.PositionOf(m.Node))
                .ThenBy(m => CanonicalName(m.Node), StringComparer.Ordinal)
                .ToList();
            var builder = new ScopeClosureBuilder();
            foreach (var member in ordered)
            {
                AddExpansion(member.Node, member.Description, builder, members);
            }
            if (isCycle)
            {
                var cycle = CycleIssue(ordered[0].Node, members);
                builder.AddIssue(cycle);
            }
            var closure = builder.Build();
            foreach (var member in component)
            {
                var description = member.Description;
                graph._nodes[member.Node] = new(member.Node, description.Kind, description.Inputs, closure);
            }
        }

        /// <summary>Adds what <paramref name="node"/> needs from outside its component, then its own part.</summary>
        private void AddExpansion(
            ScopeNode node,
            NodeDescription description,
            ScopeClosureBuilder builder,
            HashSet<ScopeNode> members)
        {
            foreach (var input in description.Inputs)
            {
                if (members.Contains(input))
                {
                    continue;
                }
                builder.AddInput(graph._nodes[input].Closure);
                ReportInput(consumer: node, input: input, builder: builder);
            }
            AddOwn(node, description, builder);
        }

        private ScopePlanIssue CycleIssue(ScopeNode start, HashSet<ScopeNode> members)
        {
            var previous = new Dictionary<ScopeNode, ScopeNode>();
            var queue = new Queue<ScopeNode>();
            queue.Enqueue(start);
            while (queue.TryDequeue(out var current))
            {
                foreach (var input in _entries[current].Description.Inputs)
                {
                    if (input == start)
                    {
                        var path = PathTo(last: current, start: start, previous: previous);
                        return new(ScopePlanIssueKind.Cycle, start, path);
                    }
                    if (members.Contains(input) && previous.TryAdd(input, current))
                    {
                        queue.Enqueue(input);
                    }
                }
            }
            throw new CyclePathNotFoundException(start);
        }

        private static ImmutableArray<ScopeNode> PathTo(
            ScopeNode last,
            ScopeNode start,
            Dictionary<ScopeNode, ScopeNode> previous)
        {
            var reversed = new List<ScopeNode> { start, last };
            for (var node = last; node != start; node = previous[node])
            {
                reversed.Add(previous[node]);
            }
            reversed.Reverse();
            return [.. reversed];
        }

        /// <summary>Breaks ties between nodes registered together, such as closed generics of one registration.</summary>
        private static string CanonicalName(ScopeNode node)
        {
            return $"{node.Type.AssemblyQualifiedName}|{node.Key?.GetType().AssemblyQualifiedName}|{node.Key}";
        }

        private NodeDescription Describe(ScopeNode node)
        {
            if (graph.Values.Find(node) is { } value)
            {
                return DescribeValue(value);
            }
            if (IsInfrastructure(node.Type))
            {
                return new(ScopeNodeKind.Infrastructure, []);
            }
            if (IsServiceLocator(node.Type))
            {
                return new(ScopeNodeKind.ServiceLocator, []);
            }
            if (TryGetCollectionElement(node: node, element: out var element))
            {
                return DescribeCollection(element);
            }
            return DescribeService(node);
        }

        private NodeDescription DescribeValue(ScopeValueRegistration value)
        {
            var kind = value.Kind switch
            {
                ScopeValueKind.Seed => ScopeNodeKind.Seed,
                ScopeValueKind.Derived => ScopeNodeKind.Derived,
                _ => ScopeNodeKind.Initialized,
            };
            ImmutableArray<ScopeNode> inputs = value.FactoryOrInitializer is { } producer
                ? ImplementationInputs(producer, key: null)
                : [];
            return new(kind, inputs);
        }

        private NodeDescription DescribeService(ScopeNode node)
        {
            var recipe = graph._index.Find(node);
            switch (recipe.Source)
            {
                case ServiceSource.Missing:
                    return new(ScopeNodeKind.Missing, []);
                case ServiceSource.Singleton:
                    return new(ScopeNodeKind.Singleton, []);
                case ServiceSource.Lambda:
                    // A lambda has no implementation type, so its hidden needs are keyed by the service.
                    return new(ScopeNodeKind.Lambda, HiddenNeeds(node.Type));
                default:
                    var inputs = ImplementationInputs(recipe.Implementation!, node.Key);
                    return new(ScopeNodeKind.Service, inputs);
            }
        }

        private NodeDescription DescribeCollection(ScopeNode element)
        {
            var inputs = new List<ScopeNode>();
            bool hasLambdaMember = false;
            foreach (var recipe in graph._index.FindEach(element))
            {
                if (recipe.Source == ServiceSource.Lambda)
                {
                    hasLambdaMember = true;
                    inputs.AddRange(HiddenNeeds(element.Type));
                }
                else if (recipe.Source == ServiceSource.Type)
                {
                    var memberInputs = ImplementationInputs(recipe.Implementation!, element.Key);
                    inputs.AddRange(memberInputs);
                }
            }
            ImmutableArray<ScopeNode> distinctInputs = [.. inputs.Distinct()];
            return new(ScopeNodeKind.Collection, distinctInputs)
            {
                HasLambdaMember = hasLambdaMember,
            };
        }

        // Problems are reported by the consumer, so every consumer of a shared missing or
        // opaque node is named.
        private void ReportInput(ScopeNode consumer, ScopeNode input, ScopeClosureBuilder builder)
        {
            var info = graph._nodes[input];
            var kind = info.Kind switch
            {
                ScopeNodeKind.Missing => ScopePlanIssueKind.MissingRegistration,
                ScopeNodeKind.ServiceLocator => ScopePlanIssueKind.ServiceLocator,
                ScopeNodeKind.Lambda => ScopePlanIssueKind.LambdaRegistration,
                _ => (ScopePlanIssueKind?) null,
            };
            if (kind is { } issueKind)
            {
                builder.AddIssue(new(issueKind, consumer, [input]));
            }
        }

        private static void AddOwn(ScopeNode node, NodeDescription description, ScopeClosureBuilder builder)
        {
            switch (description.Kind)
            {
                case ScopeNodeKind.Seed:
                    RejectDisposableType(node, builder);
                    builder.AddSeed(node);
                    break;
                case ScopeNodeKind.Derived:
                    RejectDisposableType(node, builder);
                    break;
                case ScopeNodeKind.Initialized:
                    RejectDisposableType(node, builder);
                    builder.AddInitializedValue(node.Type); // after its inputs
                    break;
                case ScopeNodeKind.Collection when description.HasLambdaMember:
                    var element = ScopeNode.Unkeyed(node.Type.GenericTypeArguments[0]);
                    builder.AddIssue(new(ScopePlanIssueKind.LambdaRegistration, node, [element]));
                    break;
            }
        }

        private static void RejectDisposableType(ScopeNode node, ScopeClosureBuilder builder)
        {
            bool isDisposable = typeof(IDisposable).IsAssignableFrom(node.Type)
                || typeof(IAsyncDisposable).IsAssignableFrom(node.Type);
            if (isDisposable)
            {
                builder.AddIssue(new(ScopePlanIssueKind.DisposableValue, node, []));
            }
        }

        private ImmutableArray<ScopeNode> ImplementationInputs(Type implementation, object? key)
        {
            return [.. ConstructorInputs(implementation, key), .. HiddenNeeds(implementation)];
        }

        private ImmutableArray<ScopeNode> HiddenNeeds(Type service)
        {
            return graph.Values.HiddenNeedsOf(service);
        }

        // Mirrors Microsoft DI: the constructor with the most parameters it can satisfy.
        // When none can be satisfied, the longest one is walked so the plan reports what is missing.
        private IEnumerable<ScopeNode> ConstructorInputs(Type implementation, object? key)
        {
            var constructors = implementation
                .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .ToList();
            if (constructors.Count == 0)
            {
                return [];
            }
            var chosen = constructors.FirstOrDefault(c => CanSatisfyAll(c, key)) ?? constructors[0];
            return chosen.GetParameters()
                .Where(p => IsRequired(p, key))
                .Select(p => ParameterNode(p, key))
                .ToList();
        }

        private bool CanSatisfyAll(ConstructorInfo constructor, object? parentKey)
        {
            foreach (var parameter in constructor.GetParameters())
            {
                bool isRequired = IsRequired(parameter, parentKey);
                if (isRequired && !CanResolve(ParameterNode(parameter, parentKey)))
                {
                    return false;
                }
            }
            return true;
        }

        private static ScopeNode ParameterNode(ParameterInfo parameter, object? parentKey)
        {
            var key = ParameterKey(parameter, parentKey);
            return new(parameter.ParameterType, key);
        }

        // Optional parameters that cannot be resolved get their default value; [ServiceKey]
        // parameters receive the key itself.
        private bool IsRequired(ParameterInfo parameter, object? parentKey)
        {
            if (parameter.IsDefined(typeof(ServiceKeyAttribute)))
            {
                return false;
            }
            if (!parameter.HasDefaultValue)
            {
                return true;
            }
            if (CanResolve(ParameterNode(parameter, parentKey)))
            {
                return true;
            }
            return false;
        }

        private bool CanResolve(ScopeNode node)
        {
            if (graph.Values.Find(node) is not null)
            {
                return true;
            }
            if (IsInfrastructure(node.Type))
            {
                return true;
            }
            if (IsServiceLocator(node.Type))
            {
                return true;
            }
            if (TryGetCollectionElement(node: node, element: out _))
            {
                return true;
            }
            if (graph._index.Find(node).Source == ServiceSource.Missing)
            {
                return false;
            }
            return true;
        }

        // Mirrors how Microsoft DI reads [FromKeyedServices] on a constructor parameter.
        private static object? ParameterKey(ParameterInfo parameter, object? parentKey)
        {
            var attribute = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            return attribute?.LookupMode switch
            {
                null or ServiceKeyLookupMode.NullKey => null,
                ServiceKeyLookupMode.InheritKey => parentKey,
                _ => attribute.Key,
            };
        }

        private static bool TryGetCollectionElement(ScopeNode node, out ScopeNode element)
        {
            var type = node.Type;
            bool isCollection = type.IsConstructedGenericType
                && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
            if (!isCollection)
            {
                element = default;
                return false;
            }
            element = new(type.GenericTypeArguments[0], node.Key);
            return true;
        }

        private static bool IsInfrastructure(Type type)
        {
            if (type == typeof(ScopeResources) || type == typeof(IServiceProviderIsService))
            {
                return true;
            }
            if (!type.IsConstructedGenericType)
            {
                return false;
            }
            if (type.GetGenericTypeDefinition() != typeof(ScopeForkFactory<>))
            {
                return false;
            }
            return true;
        }

        private static bool IsServiceLocator(Type type)
        {
            if (type == typeof(IServiceProvider))
            {
                return true;
            }
            if (type == typeof(IServiceScopeFactory))
            {
                return true;
            }
            return false;
        }
    }
}
