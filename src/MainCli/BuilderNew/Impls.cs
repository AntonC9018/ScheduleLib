using MainCli.Topics;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace MainCli.BuilderNew.Impl;

public sealed class RegistryConfig :
    IConfig<RegistryConfig>,
    ICredentialsConfig
{
    public static LayerConfigKey<RegistryConfig> Key { get; } = LayerConfigKey.Registry.Register<RegistryConfig>();
    public CredentialsSource? Credentials { get; set; }
    public ExtraLessonInstanceAction? ExtraLessonInstanceAction { get; set; }
    public IEquationCommandsDerivation? EquationCommandsDerivation { get; set; }
    public CommandProcessingConfig? CommandProcessingConfig { get; set; }
}

public sealed class LessonTopicsConfig : IConfig<LessonTopicsConfig>
{
    public static LayerConfigKey<LessonTopicsConfig> Key { get; } = LayerConfigKey.Registry.Register<LessonTopicsConfig>();
    public readonly List<LessonTopicSourceDefinition> Sources = new();

    public void AddSource<T>()
    {
        Sources.Add(new()
        {
            Type = typeof(T),
        });
    }

    public void AddSource<T>(ILessonTopicSourceFactory factory)
        where T : class
    {
        Sources.Add(new()
        {
            Factory = factory,
        });
    }
}

public sealed class MoodleConfig : IConfig<MoodleConfig>, ICredentialsConfig
{
    public static LayerConfigKey<MoodleConfig> Key { get; } = LayerConfigKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }
}

public sealed class ManifestSource
{
}

public sealed class ManifestSourceBuilder : ILessonTopicSourceFactory
{
    public void FallbackProvider(LessonType lessonType, ILessonNameProvider provider)
    {
        _ = lessonType;
        _ = provider;
    }

    public ILessonTopicSource Create(IServiceProvider sp)
    {
        return null!;
    }
}

public interface ILessonTopicSource
{
}

public interface ILessonTopicSourceFactory
{
    ILessonTopicSource Create(IServiceProvider sp);
}

public sealed class LessonTopicSourceDefinition
{
    public Type? Type { get; init; }
    public ILessonTopicSourceFactory? Factory { get; init; }
}

public sealed class TeacherLayerConfig : IConfig<TeacherLayerConfig>
{
    public static LayerConfigKey<TeacherLayerConfig> Key { get; } = LayerConfigKey.Registry.Register<TeacherLayerConfig>();
    public Name TeacherName = null!;
}

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

public sealed class GoogleDriveConfig : IConfig<GoogleDriveConfig>
{
    public static LayerConfigKey<GoogleDriveConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleDriveConfig>();
}


public static class Extensions
{
    public static readonly Layer TeacherLayer = Layer.Registry.Register("Teacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<RegistryConfig> Registry() => new(builder.Layer);
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => new(builder.Layer);
        public ConfigBuilder<MoodleConfig> Moodle() => new(builder.Layer);
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => new(builder.Layer);
        public void Drive() => builder.CreateConfigBuilder<GoogleDriveConfig>().Enable();

        public ApplicationConfigLayerBuilder Teacher(
            string nameStr,
            Action<ApplicationConfigLayerBuilder>? configure = null)
        {
            var name = NameHelper.Parse(nameStr);
            var layerBuilder = builder.AddLayer(TeacherLayer);
            var teacherBuilder = layerBuilder.CreateConfigBuilder<TeacherLayerConfig>();
            teacherBuilder.Enable().Value.TeacherName = name;
            configure?.Invoke(layerBuilder);
            return layerBuilder;
        }
    }

    extension (ConfigBuilder<LessonTopicsConfig> builder)
    {
        public void Manifest(Action<ManifestSourceBuilder> configure)
        {
            var x = new ManifestSourceBuilder();
            configure(x);

            builder.Enable().Value.AddSource<ManifestSource>();
        }
    }

    extension (ConfigBuilder<RegistryConfig> builder)
    {
        public void ExtraLessonAction(ExtraLessonInstanceAction action)
        {
            builder.Enable().Value.ExtraLessonInstanceAction = action;
        }

        public void CommandDerivation<T>() where T : IEquationCommandsDerivation, new()
        {
            builder.Enable().Value.EquationCommandsDerivation = new T();
        }

        public void ProcessingFlags(CommandProcessingConfig flags)
        {
            builder.Enable().Value.CommandProcessingConfig = flags;
        }
    }

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
                config.Sources.Add(other);
            }
            configure(other);
        }
    }
}
