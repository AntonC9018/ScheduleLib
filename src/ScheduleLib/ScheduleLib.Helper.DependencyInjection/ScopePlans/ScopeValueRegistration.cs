using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.DependencyInjection.ScopePlans;

internal enum ScopeValueKind
{
    Seed,
    Derived,
    Initialized,
}

/// <summary>
/// Scope-plan metadata of one seed, derived or initialized value, kept in the service collection
/// as a singleton instance next to the registrations it owns.
/// </summary>
/// <param name="FactoryOrInitializer">What builds a derived or initialized value; null for a seed.</param>
/// <param name="SlotType">The slot a seed or initialized value is filled into; null for a derived value.</param>
/// <param name="OwnedDescriptors">The service registrations this value added, removed with it.</param>
internal sealed record ScopeValueRegistration(
    ScopeNode Node,
    ScopeValueKind Kind,
    Type? FactoryOrInitializer,
    Type? SlotType,
    ImmutableArray<ServiceDescriptor> OwnedDescriptors);

/// <summary>What an opaque service, factory or initializer resolves at run time.</summary>
/// <param name="Service">The implementation type, or the service type of a lambda registration.</param>
internal sealed record HiddenNeed(Type Service, ScopeNode Need);

/// <summary>The scope-value and hidden-need records of one container, indexed for planning.</summary>
internal sealed class ScopeValueCatalog
{
    private readonly Dictionary<ScopeNode, ScopeValueRegistration> _values = [];
    private readonly Dictionary<Type, List<ScopeNode>> _hiddenNeeds = [];

    public ScopeValueCatalog(IEnumerable<ScopeValueRegistration> values, IEnumerable<HiddenNeed> hiddenNeeds)
    {
        foreach (var value in values)
        {
            if (!_values.TryAdd(value.Node, value))
            {
                throw new DuplicateScopeValueException(value.Node);
            }
        }
        foreach (var hiddenNeed in hiddenNeeds)
        {
            if (!_hiddenNeeds.TryGetValue(hiddenNeed.Service, out var needs))
            {
                needs = [];
                _hiddenNeeds[hiddenNeed.Service] = needs;
            }
            needs.Add(hiddenNeed.Need);
        }
    }

    public ScopeValueRegistration? Find(ScopeNode node)
    {
        return _values.GetValueOrDefault(node);
    }

    public bool IsSeed(ScopeNode node)
    {
        return Find(node)?.Kind == ScopeValueKind.Seed;
    }

    public Type SlotTypeOf(ScopeNode node)
    {
        return Find(node)?.SlotType ?? throw new MissingScopeValuePartException(node, "slot");
    }

    public Type InitializerOf(ScopeNode node)
    {
        var value = Find(node);
        if (value is { Kind: ScopeValueKind.Initialized, FactoryOrInitializer: { } initializer })
        {
            return initializer;
        }
        throw new MissingScopeValuePartException(node, "initializer");
    }

    public ImmutableArray<ScopeNode> HiddenNeedsOf(Type service)
    {
        if (_hiddenNeeds.TryGetValue(service, out var needs))
        {
            return [.. needs];
        }
        return [];
    }
}
