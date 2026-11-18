using System.Collections.Immutable;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

public enum ScopeNodeKind
{
    Seed,
    Derived,
    Initialized,

    /// <summary>A scoped or transient service built from its constructor.</summary>
    Service,

    /// <summary>A singleton: it cannot depend on scoped values, so the plan does not walk into it.</summary>
    Singleton,

    /// <summary><see cref="IEnumerable{T}"/> of every registration of a service.</summary>
    Collection,

    /// <summary>Scope-plan or container infrastructure that needs nothing from the scope.</summary>
    Infrastructure,

    ServiceLocator,
    Lambda,
    Missing,
}

public sealed record ScopeNodeInfo(ScopeNode Node, ScopeNodeKind Kind, ImmutableArray<ScopeNode> Inputs, ScopeClosure Closure);

/// <summary>
/// The dependency graph of one root container, derived from its registrations by walking
/// constructors. Holds one node per (type, key) with a memoized closure, so plans of different
/// workflows share common subgraphs. Nodes and plans are built on first use and kept.
/// Assumes scope validation: singletons never depend on scoped services.
/// </summary>
public sealed class ScopePlanGraph
{
    private readonly IServiceCollection _services;
    private readonly Dictionary<ScopeNode, ScopeNodeInfo> _nodes = [];
    private readonly Dictionary<Type, ScopePlan> _plans = [];
    private readonly Lock _lock = new();
    private ServiceIndex? _index;

    internal ScopePlanGraph(IServiceCollection services, ScopeRegistrations registrations)
    {
        _services = services;
        Registrations = registrations;
    }

    internal ScopeRegistrations Registrations { get; }

    private ServiceIndex Index => _index ??= new(_services);

    public ScopePlan GetPlan(Type root)
    {
        lock (_lock)
        {
            if (_plans.TryGetValue(root, out var existing))
            {
                return existing;
            }
            var closure = new Walk(this, root).Visit(ScopeNode.Unkeyed(root));
            var plan = new ScopePlan(root, closure, Registrations);
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
                new Walk(this, root: null).Visit(node);
            }
            return _nodes[node];
        }
    }

    private readonly record struct NodeDescription(ScopeNodeKind Kind, ImmutableArray<ScopeNode> Inputs)
    {
        public bool HasLambdaMember { get; init; }
    }

    private readonly record struct PathEntry(ScopeNode Node, List<ScopePlanIssue> CycleIssues);

    /// <summary>One depth-first walk; finished nodes are memoized in the graph.</summary>
    private sealed class Walk(ScopePlanGraph graph, Type? root)
    {
        private readonly List<PathEntry> _path = [];

        public ScopeClosure Visit(ScopeNode node)
        {
            if (graph._nodes.TryGetValue(node, out var done))
            {
                return done.Closure;
            }
            int cycleStart = _path.FindIndex(p => p.Node == node);
            if (cycleStart >= 0)
            {
                ReportCycle(cycleStart, node);
                return ScopeClosure.Empty;
            }

            var entry = new PathEntry(node, []);
            _path.Add(entry);
            var description = Describe(node);
            var builder = new ScopeClosureBuilder();
            foreach (var input in description.Inputs)
            {
                builder.AddInput(Visit(input));
                ReportInput(consumer: node, input: input, builder: builder);
            }
            AddOwn(node, description, builder);
            foreach (var cycle in entry.CycleIssues)
            {
                builder.AddIssue(cycle);
            }
            _path.RemoveAt(_path.Count - 1);

            var info = new ScopeNodeInfo(node, description.Kind, description.Inputs, builder.Build());
            graph._nodes[node] = info;
            return info.Closure;
        }

        private void ReportCycle(int cycleStart, ScopeNode repeated)
        {
            var cycleEntries = _path.Skip(cycleStart).ToList();
            ImmutableArray<ScopeNode> cyclePath = [.. cycleEntries.Select(p => p.Node), repeated];
            var issue = new ScopePlanIssue(ScopePlanIssueKind.Cycle, repeated, cyclePath);
            foreach (var cycleEntry in cycleEntries)
            {
                cycleEntry.CycleIssues.Add(issue);
            }
        }

        private NodeDescription Describe(ScopeNode node)
        {
            var registrations = graph.Registrations;
            var type = node.Type;
            if (registrations.Seeds.ContainsKey(node))
            {
                return new(ScopeNodeKind.Seed, []);
            }
            if (IsInfrastructure(type))
            {
                return new(ScopeNodeKind.Infrastructure, []);
            }
            if (IsServiceLocator(type))
            {
                return new(ScopeNodeKind.ServiceLocator, []);
            }
            if (node.Key is null)
            {
                if (registrations.Derived.TryGetValue(key: type, value: out var factory))
                {
                    return new(ScopeNodeKind.Derived, ImplementationInputs(factory, key: null));
                }
                if (registrations.Initialized.TryGetValue(type, out var initialized))
                {
                    return new(ScopeNodeKind.Initialized, ImplementationInputs(initialized.Initializer, key: null));
                }
            }
            if (TryGetCollectionElement(node: node, element: out var element))
            {
                return DescribeCollection(element);
            }
            return DescribeService(node);
        }

        private NodeDescription DescribeService(ScopeNode node)
        {
            var recipe = graph.Index.Find(node);
            switch (recipe.Source)
            {
                case ServiceSource.Missing when node.Type == root:
                    // Workflow roots may be concrete types that are not registered.
                    return new(ScopeNodeKind.Service, ImplementationInputs(node.Type, key: null));
                case ServiceSource.Missing:
                    return new(ScopeNodeKind.Missing, []);
                case ServiceSource.Singleton:
                    return new(ScopeNodeKind.Singleton, []);
                case ServiceSource.Lambda:
                    return new(ScopeNodeKind.Lambda, []);
                default:
                    var inputs = ImplementationInputs(recipe.Implementation!, node.Key);
                    return new(ScopeNodeKind.Service, inputs);
            }
        }

        private NodeDescription DescribeCollection(ScopeNode element)
        {
            var inputs = new List<ScopeNode>();
            bool hasLambdaMember = false;
            foreach (var recipe in graph.Index.FindEach(element))
            {
                if (recipe.Source == ServiceSource.Lambda)
                {
                    hasLambdaMember = true;
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
            if (!graph._nodes.TryGetValue(input, out var info))
            {
                return; // part of a cycle, already reported
            }
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
            var hiddenNeeds = graph.Registrations.HiddenNeeds.GetValueOrDefault(implementation) ?? [];
            return [.. ConstructorInputs(implementation, key), .. hiddenNeeds];
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
            var registrations = graph.Registrations;
            if (registrations.Seeds.ContainsKey(node))
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
            if (IsUnkeyedValue(node))
            {
                return true;
            }
            if (TryGetCollectionElement(node: node, element: out _))
            {
                return true;
            }
            if (graph.Index.Find(node).Source == ServiceSource.Missing)
            {
                return false;
            }
            return true;
        }

        private bool IsUnkeyedValue(ScopeNode node)
        {
            if (node.Key is not null)
            {
                return false;
            }
            var registrations = graph.Registrations;
            if (registrations.Derived.ContainsKey(node.Type))
            {
                return true;
            }
            if (registrations.Initialized.ContainsKey(node.Type))
            {
                return true;
            }
            return false;
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
