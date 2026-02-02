using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.JsonConverters;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Config;

public sealed record class TeacherLayerConfig : IConfig<TeacherLayerConfig>
{
    public static LayerConfigKey<TeacherLayerConfig> Key { get; } = LayerConfigKey.Registry.Register<TeacherLayerConfig>();
    public Name TeacherName { get; set; } = null!;

    public static void Register(IServiceCollection services)
    {
        services.SetImmutable<Name>();
        services.RegisterBasicOperationsAndMergers<TeacherLayerConfig>();
        services.AddConfigProvider(TeacherLayerConfig.Key);
        services.ConfigureConfigJsonSerialization(opts =>
        {
            opts.Converters.Add(new NameJsonConverter());
        });
    }
}

public partial class Extensions
{
    extension (ApplicationConfigLayerBuilder builder)
    {
        public ApplicationConfigLayerBuilder TeacherLayer(
            string nameStr,
            Action<ApplicationConfigLayerBuilder>? configure = null)
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
