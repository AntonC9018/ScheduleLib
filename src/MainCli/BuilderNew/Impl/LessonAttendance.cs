namespace MainCli.BuilderNew.Impl;

public sealed class LessonAttendanceSource
{
    public string? FilePath { get; set; }
}

public sealed class LessonAttendanceConfig : IConfig<LessonAttendanceConfig>
{
    public static LayerConfigKey<LessonAttendanceConfig> Key { get; } = LayerConfigKey.Registry.Register<LessonAttendanceConfig>();
    // Need a way to allow to remove an item by key.
    public List<LessonAttendanceSource> Sources = new();
}

public partial class Extensions
{
    extension (ConfigBuilder<LessonAttendanceConfig> builder)
    {
        public void Source(Action<LessonAttendanceSource> configure)
        {
            builder.Source(null, configure);
        }

        public void Source(string? filePath, Action<LessonAttendanceSource> configure)
        {
            var config = builder.Enable().Value;
            // for now, use the file path as the id.
            var other = config.Sources.Find(other => other.FilePath == filePath);
            if (other is null)
            {
                other = new();
            }
            configure(other);
        }
    }
}
