using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Core;

public sealed class DeadlinesExcelConfig : INodeData<DeadlinesExcelConfig>
{
    public static NodeDataKey<DeadlinesExcelConfig> Key { get; } = NodeDataKey.Registry.Register<DeadlinesExcelConfig>();

    public System.Drawing.Color? GoodColor { get; set; }
    public System.Drawing.Color? BadColor { get; set; }
    public int LessonDelayLimit { get; set; } = -1;
    public int MaxTaskRows { get; set; } = -1;
    public float ColumnWidth { get; set; } = float.NegativeInfinity;

    public static void Register(IServiceCollection services)
    {
        services.AddMapper<DeadlinesConfigMapper>();
        services.AddMerger<DeadlinesExcelConfigMerger>();
        services.RegisterBasicOperationsAndMergers<DeadlinesExcelConfig>();
        services.AddConfigProvider(DeadlinesExcelBuiltConfig.Key);
    }
}

public sealed class DeadlinesExcelBuiltConfig
{
    public static NodeDataKey<DeadlinesExcelBuiltConfig> Key => new(DeadlinesExcelConfig.Key.Value);

    public required System.Drawing.Color GoodColor { get; init; }
    public required System.Drawing.Color BadColor { get; init; }
    public required int LessonDelayLimit { get; init; }
    public required int MaxTaskRows { get; init; }
    public required float ColumnWidth { get; init; }
}
