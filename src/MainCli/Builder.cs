using System.Collections.Concurrent;
using MainCli.Topics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

public static class Builder
{
    public static void Test(IServiceProvider services)
    {
        // Builder + dynamic object so that it could be configured from a UI.
        // Allows to get immutable config for specific things on demand
        // (so that running tasks are never affected).
        var b = services.GetRequiredService<ApplicationConfigBuilder>();

        // allows to configure the defaults at this level
        // they will take effect if later they are not overriden,
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry.Configure(x =>
            {
                x.ExtraLessonAction(ExtraLessonInstanceAction.LeaveAlone);
                x.CommandDerivation<AnyDayDerivation>();
                x.ProcessingFlags(CommandProcessingConfig.Process
                    .WithLog(LessonEquationCommandTypes.All));
            });

            // Adds default source.
            // b.LessonTopics.AddSource<ManifestLessonTopicSource>(topics => ...)
            defaults.LessonTopics.Manifest(topics =>
            {
                // If someone else tries using topics.Manifest()
                // in their source config, it would bind to this one.
            });

            defaults.Registry.Credentials.FromConfig();
            defaults.Moodle.Credentials.FromConfig(isRequired: true);

            // this is allowed too
            // t.Registry.Credentials.FromConfig(isRequired: true);
            // defaults.Registry.Configure(x =>
            // {
            //     x.Credentials.FromConfig(isRequired: true);
            // });
        });

        // As an idea (ignore for now)
        // var scope = defaults.Scope(scope =>
        // {
        //     // This will always be added to all, even if not overriddent.
        //     scope.LessonTopics.Manifest();
        // });

        b.Defaults.Teacher("Anton Curmanschii", t =>
        {
            t.Drive();

            t.LessonAttendance.Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                // a.Format(...) allows to reset the format.
                // each format has specific configurations like header configs for excel.
                // attendance.SetPath();
            });
            t.LessonTopics.Manifest(topics =>
            {
                // If this is set, it won't register the default source.
                // topics.SetPath();

                topics.FallbackProvider(LessonType.Lab, new NoNameProvider());

                // to configure for all, use
                // topics.FallbackProvider(new NoNameProvider);

                // also allowed:
                // topics.FallbackProvider<NoNameProvider>(LessonType.Lab);
            });

            t.Moodle.Credentials.FromConfig(isRequired: true);
            // t.Moodle.Configure(moodle =>
            // {
            //     moodle.Credentials.FromConfig(isRequired: true);
            // });
        });

        b.Defaults.Teacher("Tamara Iatasina", t =>
        {
            t.Moodle.NoInherit();
            t.LessonTopics.NoInherit();
        });

    }
}


public sealed class ApplicationConfigBuilder
{
    private readonly MutableLayer _baseLayer = new();

    public ApplicationConfigLayerBuilder Defaults
    {
        get
        {
            return new(_baseLayer);
        }
    }

    public ApplicationConfigLayerBuilder AddLayer(Layer layer)
    {
        return Defaults.AddLayer(layer);
    }
}

public readonly struct ApplicationConfigLayerBuilder
{
    public readonly MutableLayer Layer { get; }

    public ApplicationConfigLayerBuilder(MutableLayer layer)
    {
        Layer = layer;
    }

    public ApplicationConfigLayerBuilder AddLayer(Layer layer)
    {
        var model = new MutableLayer();
        Layer.ChildLayers.Add(new(layer, model));
        return new(model);
    }

    public void Configure(Action<ApplicationConfigLayerBuilder> configure)
    {
        configure(this);
    }
}

public interface ICreateFromString<T>
{
    static abstract T Create(string val);
}

public sealed class NameRegistry<T>
    where T : ICreateFromString<T>
{
    private readonly HashSet<string> _registered = new();

    public T Register(string name)
    {
        lock (_registered)
        {
            if (!_registered.Add(name))
            {
                throw new InvalidOperationException($"{typeof(T).Name} with name '{name}' has already been registered.");
            }
            return T.Create(name);
        }
    }
}

public sealed class ConfigKeyRegistry
{
    private readonly NameRegistry<LayerConfigKey> _impl = new();

    public LayerConfigKey<T> Register<T>() where T : class
    {
        return Register<T>(typeof(T).Name);
    }
    public LayerConfigKey<T> Register<T>(string name) where T : class
    {
        var ret = _impl.Register(name);
        return new(ret);
    }
}

public readonly record struct NamedLayer(Layer Name, MutableLayer Model);
public readonly record struct Layer(string Value) : ICreateFromString<Layer>
{
    public static readonly NameRegistry<Layer> Registry = new();
    public static readonly Layer DefaultLayer = Registry.Register("Default");
    public static Layer Unnamed => new("");
    public static Layer Create(string v) => new(v);
}

public readonly record struct LayerConfigKey<T>(LayerConfigKey Value) where T : class;
public readonly record struct LayerConfigKey(string Value) : ICreateFromString<LayerConfigKey>
{
    public static readonly ConfigKeyRegistry Registry = new();
    public static LayerConfigKey Create(string val) => new(val);
}

public struct LayerConfigFlags
{
    public bool Remove;
    public bool Clean;
}

public sealed class LayerConfigContainer
{
    public object? Value;
    public LayerConfigFlags Flags = new();
}

public readonly struct LayerConfigContainer<T>
{
    private readonly LayerConfigContainer _impl;

    public LayerConfigContainer(LayerConfigContainer impl)
    {
        _impl = impl;
    }

    public readonly T Value
    {
        get => (T) _impl.Value!;
        set => _impl.Value = value;
    }
    public readonly ref LayerConfigFlags Flags => ref _impl.Flags;
}

public readonly struct MaybeLayerConfigContainer<T>
{
    private readonly LayerConfigContainer? _impl;

    public MaybeLayerConfigContainer(LayerConfigContainer? impl)
    {
        _impl = impl;
    }

    public bool Exists => _impl != null;
    public LayerConfigContainer<T> Value
    {
        get
        {
            if (!Exists)
            {
                throw new InvalidOperationException("Does not exist!");
            }
            return new(_impl!);
        }
    }
}

public sealed class MutableLayer
{
    private readonly ConcurrentDictionary<LayerConfigKey, LayerConfigContainer> _configs = new();
    internal readonly List<NamedLayer> ChildLayers = new();

    public MaybeLayerConfigContainer<T> Get<T>(LayerConfigKey<T> key) where T : class
    {
        var container = _configs.GetValueOrDefault(key.Value, null!);
        return new(container);
    }

    public LayerConfigContainer<T> GetOrAdd<T>(
        LayerConfigKey<T> key,
        Func<T> factory) where T : class
    {
        var container = _configs.GetOrAdd(key.Value, _ => new()
        {
            Value = factory(),
        });
        return new(container);
    }
}

public interface IConfig<T> where T : class
{
    public static abstract LayerConfigKey<T> Key { get; }
}

public interface ICredentialsConfig
{
    public CredentialsSource? Credentials { get; set; }
}

public readonly struct ConfigBuilder<T>
    where T : class, IConfig<T>
{
    internal readonly MutableLayer _layer;

    public ConfigBuilder(MutableLayer layer)
    {
        _layer = layer;
    }

    public T GetConfig()
    {
        var val = _layer.Get(T.Key);
        return val.Value.Value;
    }
}

public static class BaseExtensions
{
    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<T> CreateConfigBuilder<T>()
            where T : class, IConfig<T>
        {
            return new(builder.Layer);
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class, IConfig<T>
    {
        public LayerConfigContainer<T> Enable(Func<T> factory)
        {
            return builder._layer.GetOrAdd(T.Key, factory);
        }
    }

    extension (MutableLayer layer)
    {
        public LayerConfigContainer<T> GetOrAdd<T>(
            LayerConfigKey<T> key) where T : class, new()
        {
            return layer.GetOrAdd(key, () => new T());
        }
    }

    extension<T> (ConfigBuilder<T> builder)
        where T : class, IConfig<T>, new()
    {
        public LayerConfigContainer<T> Enable()
        {
            return builder._layer.GetOrAdd(T.Key);
        }

        public void Remove()
        {
            builder.Enable().Flags.Remove = true;
        }
        public void NoInherit()
        {
            builder.Enable().Flags.Clean = true;
        }
        public void ConfigureValue(Action<T> configure)
        {
            configure(builder.Enable().Value);
        }
        public void Configure(Action<ConfigBuilder<T>> configure)
        {
            configure(builder);
        }
    }

}

public sealed class RegistryConfig :
    IConfig<RegistryConfig>,
    ICredentialsConfig
{
    public static LayerConfigKey<RegistryConfig> Key { get; } = LayerConfigKey.Registry.Register<RegistryConfig>();
    public CredentialsSource? Credentials { get; set; }
}

public sealed class CredentialsSource
{
}

public readonly struct CredentialsSourceBuilder
{
    private readonly CredentialsSource _source;

    public CredentialsSourceBuilder(CredentialsSource source)
    {
        _source = source;
    }
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
    public static LayerConfigKey<TeacherLayerConfig> Key { get; } = LayerConfigKey.Registry.Register<TeacherLayerConfig>("TeacherName");
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
        public ConfigBuilder<RegistryConfig> Registry => new(builder.Layer);
        public ConfigBuilder<LessonTopicsConfig> LessonTopics => new(builder.Layer);
        public ConfigBuilder<MoodleConfig> Moodle => new(builder.Layer);
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance => new(builder.Layer);
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
        }

        public void CommandDerivation<T>() where T : IEquationCommandsDerivation
        {
        }

        public void ProcessingFlags(CommandProcessingConfig flags)
        {
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

    extension<T> (ConfigBuilder<T> builder)
        where T : class, ICredentialsConfig, IConfig<T>, new()
    {
        public CredentialsSourceBuilder Credentials
        {
            get
            {
                var t = new CredentialsSource();
                builder.Enable().Value.Credentials = t;
                return new(t);
            }
        }
    }

    extension (CredentialsSourceBuilder builder)
    {
        public void FromConfig(bool isRequired = false)
        {
        }
    }
}
