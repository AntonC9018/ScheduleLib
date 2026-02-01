using Anton.LayeredConfig;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core.Topics;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

public sealed class LessonTopicsConfig : IConfig<LessonTopicsConfig>
{
    public static LayerConfigKey<LessonTopicsConfig> Key { get; } = LayerConfigKey.Registry.Register<LessonTopicsConfig>();
    public List<LessonTopicSourceDefinition> Sources { get; set; } = new();
    public List<LessonNameProviderConfig> FallbackProviders { get; set; } = new();

    public static void Register(IServiceCollection services)
    {
        services.AddBasicOperations<LessonTopicsSourceDefinitionBasicOperations>();
        services.RegisterBasicOperationsAndMergers<LessonTopicsConfig>();
        services.AddKeyEqualityComparer((LessonNameProviderConfig c) => c.LessonType);
        {
            var hierarchy = services.AddOpenHierarchy<ILessonNameProvider>();
            hierarchy
                .AddDerived<LabAutoNumberingNameProvider>()
                .SetImmutable();
            hierarchy
                .AddDerived<NoNameProvider>()
                .SetImmutable();
            hierarchy
                .AddDerived<ListLessonNameProvider>()
                .SetImmutable();
        }

        {
            var hierarchy = services.AddOpenHierarchy<LessonTopicSourceDefinition>();
            hierarchy
                .AddDerived<ManifestLessonTopicSourceDefinition>()
                .AddKeyEqualityComparer(x => x.Path);
        }

        services.RegisterBasicOperationsAndMergers<ManifestLessonTopicSourceDefinition>();
    }
}

public sealed class LessonNameProviderConfig
{
    public required LessonType LessonType { get; set; }
    public ILessonNameProvider Provider { get; set; } = null!;
}

public interface ILessonTopicSource
{
    public ValueTask Configure(
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken);
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

public interface LessonTopicSourceDefinition
{
    ILessonTopicSource Create(IServiceProvider sp);
}

