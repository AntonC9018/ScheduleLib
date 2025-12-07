using ScheduleLib.Parsing;

namespace MainCli.BuilderNew.Impl;

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
            var teacherBuilder = layerBuilder.CreateConfigBuilder<TeacherLayerConfig>();
            teacherBuilder.Enable().Value.TeacherName = name;
            configure?.Invoke(layerBuilder);
            return layerBuilder;
        }
    }
}
