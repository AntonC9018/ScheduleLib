using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.JsonConverters;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Config;

/// <summary>
/// The teacher marker: which teacher's layer a scope's settings come from. Immutable; a scope that
/// selects another teacher receives another marker.
/// </summary>
public sealed record class TeacherLayerConfig : INodeData<TeacherLayerConfig>
{
    public static NodeDataKey<TeacherLayerConfig> Key { get; } = NodeDataKey.Registry.Register<TeacherLayerConfig>();
    public Name TeacherName { get; init; } = null!;

    public static void Register(IServiceCollection services)
    {
        services.SetImmutable<Name>();
        services.RegisterBasicOperationsAndMergers<TeacherLayerConfig>();
        services.AddConfigProvider(Key);
        services.ConfigureNodeDataJsonSerialization(opts =>
        {
            opts.Converters.Add(new NameJsonConverter());
        });
    }
}

public partial class Extensions
{
    extension(NodeBuilder builder)
    {
        public NodeBuilder TeacherLayer(
            string nameStr,
            Action<NodeBuilder>? configure = null)
        {
            var name = NameHelper.Parse(nameStr);
            var layerBuilder = builder.AddLayer(LayerKeys.TeacherLayerKey);
            var marker = new TeacherLayerConfig
            {
                TeacherName = name,
            };
            layerBuilder.Builder<TeacherLayerConfig>()
                .Enable()
                .SetValue(marker);
            configure?.Invoke(layerBuilder);
            return layerBuilder;
        }
    }
}
