using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Application.Config;

/// <summary>Global derivation settings shared by every teacher and output.</summary>
public sealed class ScheduleDerivationConfig : INodeData<ScheduleDerivationConfig>
{
    public static NodeDataKey<ScheduleDerivationConfig> Key { get; } =
        NodeDataKey.Registry.Register<ScheduleDerivationConfig>();

    public bool? AutoMergeLabSubGroups { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddConfigProvider(Key);
        services.RegisterBasicOperationsAndMergers<ScheduleDerivationConfig>();
        services.AddDerived<ScheduleDerivationOptions, CodeScheduleDerivationFactory>();
    }
}

/// <summary>The derivation options of the code-defined defaults, for hosts without settings files.</summary>
public sealed class CodeScheduleDerivationFactory(TreeBuilder tree) : IFactory<ScheduleDerivationOptions>
{
    public ScheduleDerivationOptions Create()
    {
        var defaults = new NodePath([tree.BaseNode]);
        var config = defaults.ConstructValue(ScheduleDerivationConfig.Key, tree.SingletonServiceProvider);
        return new()
        {
            AutoMergeLabSubGroups = config?.AutoMergeLabSubGroups ?? false,
        };
    }
}

/// <summary>How a loaded schedule is derived from its sources. A scope value.</summary>
public sealed class ScheduleDerivationOptions
{
    public bool AutoMergeLabSubGroups { get; init; }
}
