using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using MainCli.Topics;
using Microsoft.Extensions.DependencyInjection;
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
    public List<LessonTopicSourceDefinition> Sources { get; set; } = new();
    public List<LessonNameProviderConfig> FallbackProviders { get; set; } = new();
}

public sealed class LessonNameProviderConfig
{
    public required LessonType LessonType { get; set; }
    public ILessonNameProvider Provider { get; set; } = null!;
}

public sealed class MoodleConfig : IConfig<MoodleConfig>, ICredentialsConfig
{
    public static LayerConfigKey<MoodleConfig> Key { get; } = LayerConfigKey.Registry.Register<MoodleConfig>();
    public CredentialsSource? Credentials { get; set; }
}

public sealed class ManifestSource : ILessonTopicSource
{
    private readonly ManifestFileSource _fileSource;

    public ManifestSource(ManifestFileSource fileSource)
    {
        _fileSource = fileSource;
    }

    public async ValueTask Configure(
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken)
    {
        var manifest = await _fileSource.Read(cancellationToken);
    }
}

public sealed class ManifestSourceBuilder
{
    private readonly ManifestLessonTopicSourceDefinition Definition = new();

    public ManifestSource Create()
    {
        return null!;
    }
}

public interface ILessonTopicSource
{
    public ValueTask Configure(
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken);
}

public sealed class ManifestFileSource
{
    private readonly string _path;

    public ManifestFileSource(string path)
    {
        _path = path;
    }

    public async Task<Manifest> Read(CancellationToken cancellationToken)
    {
        await using var inputFile = File.OpenRead(_path);
        var manifest = await ManifestSerializer.Deserialize(inputFile, cancellationToken);
        return manifest;
    }
}

public interface LessonTopicSourceDefinition
{
    ILessonTopicSource Create(IServiceProvider sp);
}

public sealed class LessonTopicsSourceDefinitionBasicOperations : IBasicOperations<LessonTopicSourceDefinition>
{
    private readonly IServiceProvider _serviceProvider;

    public LessonTopicsSourceDefinitionBasicOperations(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    public LessonTopicSourceDefinition? Empty() => null;
    public LessonTopicSourceDefinition? Reset(LessonTopicSourceDefinition? item) => null;

    public LessonTopicSourceDefinition Copy(LessonTopicSourceDefinition from)
    {
        var ret = CallCopyHelper.CopyUsingService(_serviceProvider, from);
        return (LessonTopicSourceDefinition) ret;
    }
}

// TODO: Separate this from the runtime factory.
public sealed class ManifestLessonTopicSourceDefinition : LessonTopicSourceDefinition
{
    public string? Path { get; set; }
    public ILessonTopicSource Create(IServiceProvider sp)
    {
        return null!;
    }
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
    public static readonly Layer TeacherLayerKey = Layer.Registry.Register("Teacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<RegistryConfig> Registry() => new(builder.Layer);
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => new(builder.Layer);
        public ConfigBuilder<MoodleConfig> Moodle() => new(builder.Layer);
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => new(builder.Layer);
        public void Drive() => builder.CreateConfigBuilder<GoogleDriveConfig>().Enable();

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

    extension (ConfigBuilder<LessonTopicsConfig> builder)
    {
        public void Manifest(Action<ManifestSourceBuilder>? configure = null)
        {
            // This should also have the path.
            var x = new ManifestSourceBuilder();
            configure?.Invoke(x);

            var sources = builder.Enable().Value.Sources;
            var source = new ManifestLessonTopicSourceDefinition();
            sources.Add(source);
        }

        public void FallbackProvider(LessonType lessonType, ILessonNameProvider provider)
        {
            var sources = builder.Enable().Value.FallbackProviders;
            var x = sources.Find(x => x.LessonType == lessonType);
            if (x == null)
            {
                x = new LessonNameProviderConfig
                {
                    LessonType = lessonType,
                };
                sources.Add(x);
            }
            x.Provider = provider;
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
