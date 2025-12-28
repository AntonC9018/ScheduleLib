using Anton.LayeredConfig;

namespace MainCli.BuilderNew.Impl;

public sealed class GoogleDriveConfig : IConfig<GoogleDriveConfig>
{
    public static LayerConfigKey<GoogleDriveConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleDriveConfig>();
}

public static partial class Extensions
{
    public static readonly Layer TeacherLayerKey = Layer.Registry.Register("Teacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => builder.Builder<LessonTopicsConfig>();
        public ConfigBuilder<MoodleConfig> Moodle() => builder.Builder<MoodleConfig>();
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => builder.Builder<LessonAttendanceConfig>();
        public ConfigBuilder<GoogleDriveConfig> Drive()
        {
            var t = ConfigBuilder.Create(builder.Layer, GoogleDriveConfig.Key);
            t.Enable();
            return t;
        }
        public ConfigBuilder<DeadlinesExcelConfig> DeadlinesExcel()
        {
            return new(
                builder.Layer,
                configKey: new(DeadlinesExcelConfig.Key.Value));
        }
    }
}
