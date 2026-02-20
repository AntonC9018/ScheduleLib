using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.JsonConverters;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Config;

public sealed record class TeacherLayerConfig : INodeData<TeacherLayerConfig>
{
    public static NodeDataKey<TeacherLayerConfig> Key { get; } = NodeDataKey.Registry.Register<TeacherLayerConfig>();
    public Name TeacherName { get; set; } = null!;

    public static void Register(IServiceCollection services)
    {
        services.SetImmutable<Name>();
        services.RegisterBasicOperationsAndMergers<TeacherLayerConfig>();
        services.AddConfigProvider(TeacherLayerConfig.Key);
        services.ConfigureNodeDataJsonSerialization(opts =>
        {
            opts.Converters.Add(new NameJsonConverter());
        });
    }
}

public partial class Extensions
{
    extension (NodeBuilder builder)
    {
        public NodeBuilder TeacherLayer(
            string nameStr,
            Action<NodeBuilder>? configure = null)
        {
            var name = NameHelper.Parse(nameStr);
            var layerBuilder = builder.AddLayer(TeacherLayerKey);
            var teacherBuilder = layerBuilder.Builder<TeacherLayerConfig>();
            teacherBuilder.Value().TeacherName = name;
            configure?.Invoke(layerBuilder);
            return layerBuilder;
        }
    }
}
