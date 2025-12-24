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
        public ConfigBuilder<RegistryConfig> Registry() => builder.CreateConfigBuilder<RegistryConfig>();
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => builder.CreateConfigBuilder<LessonTopicsConfig>();
        public ConfigBuilder<MoodleConfig> Moodle() => builder.CreateConfigBuilder<MoodleConfig>();
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => builder.CreateConfigBuilder<LessonAttendanceConfig>();
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
