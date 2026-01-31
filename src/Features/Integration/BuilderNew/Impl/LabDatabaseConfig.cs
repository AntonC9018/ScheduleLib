using Anton.LayeredConfig;
using Anton.LayeredConfig.Attributes;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

public sealed class LabTasksDatabaseConfig : IConfig<LabTasksDatabaseConfig>
{
    public static LayerConfigKey<LabTasksDatabaseConfig> Key { get; } = LayerConfigKey.Registry.Register<LabTasksDatabaseConfig>();
    public List<LabTasksSource> Sources { get; set; } = new();

    [RegisterMethod]
    public static void Register(IServiceCollection services)
    {
        services.AddKeyEqualityComparer((LabTasksSource c) => c.Key);
        services.AddOpenHierarchy<LabTasksSource>();
        services.AddKeyEqualityComparer((LabTask t) => t.Name);
        services.RegisterBasicOperationsAndMergers<ManualLabTaskSource>();
        services.RegisterBasicOperationsAndMergers<LabTasksDatabaseConfig>();
        services.AddConfigProvider(LabTasksDatabaseConfig.Key);
    }
}

public readonly record struct Option(Language? Language, string? Type = null)
{
    public static Option Default => default;
}
public readonly record struct SetKey(string CourseName, Option Option);

public abstract class LabTasksSource
{
    [Key] public SetKey Key { get; set; }

    public abstract ValueTask<List<LabTask>> GetTasks(IServiceProvider sp);
}

public sealed class LabTask
{
    [Key] public string? Name { get; set; }
    public string? Url { get; set; }
    public double? Difficulty { get; set; }
}

public sealed class ManualLabTaskSource : LabTasksSource
{
    public List<LabTask> Tasks { get; set; } = new();

    public override ValueTask<List<LabTask>> GetTasks(IServiceProvider sp) => ValueTask.FromResult(Tasks);
}

public readonly record struct CourseOptionLabTasksBuilder(
    CourseLabTasksBuilder Builder,
    Option Option);

public readonly record struct CourseLabTasksBuilder(
    LabTasksDatabaseConfig Config,
    string CourseName);


public readonly ref struct OptionBuilder(ref Option value)
{
    private readonly ref Option _value = ref value;
    public void Language(Language lang) => _value = _value with { Language = lang };
    public void Type(string type) => _value = _value with { Type = type };
}

public static class LabTasksBuilderExtensions
{
    extension (CourseLabTasksBuilder builder)
    {
        public CourseOptionLabTasksBuilder Option(Action<OptionBuilder> configure)
        {
            var opt = new Option();
            var optBuilder = new OptionBuilder(ref opt);
            configure(optBuilder);
            return new(builder, opt);
        }
        public CourseOptionLabTasksBuilder Default()
        {
            return builder.Option(x =>
            {
                _ = x;
            });
        }
    }
    extension (CourseOptionLabTasksBuilder c)
    {
        public void AddSource(LabTasksSource source)
        {
            // NOTE TO SELF:
            // The issue here is that it might get duplicate keys.
            // Resolving keys is only possible with a service provider.
            source.Key = new(c.Builder.CourseName, c.Option);
            c.Builder.Config.Sources.Add(source);
        }

        public CourseOptionLabTasksBuilder ManualSource(Action<List<LabTask>> configure)
        {
            var source = new ManualLabTaskSource();
            c.AddSource(source);
            configure(source.Tasks);
            return c;
        }
    }
    extension (LabTasksDatabaseConfig c)
    {
        public CourseLabTasksBuilder Course(string name)
        {
            return new(c, name);
        }
        public CourseOptionLabTasksBuilder CourseDefault(string name)
        {
            return c.Course(name).Default();
        }
    }
}
