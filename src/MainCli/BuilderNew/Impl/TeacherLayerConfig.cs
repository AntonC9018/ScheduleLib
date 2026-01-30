using Anton.LayeredConfig;
using ScheduleLib.Parsing;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

public sealed class TeacherLayerConfig : IConfig<TeacherLayerConfig>
{
    public static LayerConfigKey<TeacherLayerConfig> Key { get; } = LayerConfigKey.Registry.Register<TeacherLayerConfig>();
    public Name TeacherName = null!;
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
