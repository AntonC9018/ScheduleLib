namespace MainCli.BuilderNew.Impl;

public sealed class MoodleConfig : IConfig<MoodleConfig>, ICredentialsConfig
{
    public static LayerConfigKey<MoodleConfig> Key { get; } = LayerConfigKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }
}

public sealed class GoogleDriveConfig : IConfig<GoogleDriveConfig>
{
    public static LayerConfigKey<GoogleDriveConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleDriveConfig>();
}

public static partial class Extensions
{
    public static readonly Layer TeacherLayerKey = Layer.Registry.Register("Teacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<RegistryConfig> Registry() => new(builder.Layer);
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => new(builder.Layer);
        public ConfigBuilder<MoodleConfig> Moodle() => new(builder.Layer);
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => new(builder.Layer);
        public ConfigBuilder<GoogleDriveConfig> Drive()
        {
            var t = new ConfigBuilder<GoogleDriveConfig>(builder.Layer);
            t.Enable();
            return t;
        }
    }
}
