using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Config;

/// <summary>Global derivation settings shared by every teacher and output.</summary>
public sealed class ScheduleDerivationConfig : INodeData<ScheduleDerivationConfig>
{
    public static NodeDataKey<ScheduleDerivationConfig> Key { get; } = NodeDataKey.Registry.Register<ScheduleDerivationConfig>();
    public bool? AutoMergeLabSubGroups { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddConfigProvider(Key);
        services.RegisterBasicOperationsAndMergers<ScheduleDerivationConfig>();
        services.AddOptions<ScheduleDerivationOptions>()
            .Configure<TreeBuilder, IServiceProvider>((options, tree, provider) =>
            options.AutoMergeLabSubGroups = new NodePath([tree.BaseNode])
                .ConstructValue(Key, provider)?.AutoMergeLabSubGroups ?? false);
    }
}

public sealed class ScheduleDerivationOptions
{
    public bool AutoMergeLabSubGroups { get; set; }
}
