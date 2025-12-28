using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;

namespace MainCli.BuilderNew.Impl;


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

public static class LabTasksBuilderExtensions
{
    extension (CourseLabTasksBuilder builder)
    {
        public CourseOptionLabTasksBuilder Option(Option option)
        {
            return new(builder, option);
        }
        public CourseOptionLabTasksBuilder Default()
        {
            return builder.Option(Impl.Option.Default);
        }
    }
    extension (CourseOptionLabTasksBuilder c)
    {
        public CourseOptionLabTasksBuilder ManualSource(Action<List<LabTask>> configure)
        {
            var source = new ManualLabTaskSource();
            // NOTE TO SELF:
            // The issue here is that it might get duplicate keys.
            // Resolving keys is only possible with a service provider.
            c.Builder.Config.Sources.Add(source);
            source.Key = new(c.Builder.CourseName, c.Option);
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
