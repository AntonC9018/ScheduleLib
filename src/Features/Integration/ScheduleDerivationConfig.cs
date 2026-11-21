using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Application.Config;

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

public sealed class ScheduleDerivationOptions
{
    public bool AutoMergeLabSubGroups { get; init; }
}
