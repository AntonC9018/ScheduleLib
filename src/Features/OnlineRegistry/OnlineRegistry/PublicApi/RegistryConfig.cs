using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.AttendanceExcel;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.OnlineRegistry.Impl;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.OnlineRegistry;

public sealed class RegistryConfig :
    INodeData<RegistryConfig>,
    ICredentialsHolder
{
    public static NodeDataKey<RegistryConfig> Key { get; } = NodeDataKey.Registry.Register<RegistryConfig>();
    public CredentialsSource? Credentials { get; set; }
    public ExtraLessonInstanceAction? ExtraLessonInstanceAction { get; set; }
    public IEquationCommandsDerivation? EquationCommandsDerivation { get; set; }
    public CommandProcessingConfig? CommandProcessingConfig { get; set; }
    public List<RegistryGroupMatchRule> GroupMatchRules { get; set; } = [];
    public List<RegistryLabMergeRule> LabMerges { get; set; } = [];
    public List<RegistryAssessmentRule> Assessments { get; set; } = [];
    public TimeSpan? HttpCallDelay { get; set; }
    public Dictionary<string, string>? CourseNames { get; set; }
    public Dictionary<string, string>? StudentNames { get; set; }
    public int? CourseNameMaxDistance { get; set; }
    public AttendanceMode? SkipAttendance { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.SetImmutable<List<DateOnly>>();
        services.SetImmutable<Dictionary<string, string>>();
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();
        services.AddKeyEqualityComparer((RegistryGroupMatchRule rule) => (rule.Faculty, rule.SubGroup));
        services.AddKeyEqualityComparer((RegistryLabMergeRule rule) => (rule.Course, rule.Group));
        services.AddKeyEqualityComparer((RegistryAssessmentRule rule) => (rule.Course, rule.Group,
            Type: rule.LessonType, rule.SubGroup, rule.Specialization, rule.Alternative));
        services.AddConfigProvider(Key);
        services.AddConfigProvider(BuiltRegistryConfig.Key);
        services.AddMapper<RegistryConfigMapper>();
        services.ConfigureNodeDataJsonSerialization(
            CommandProcessingConfigJsonConverter.Register);
    }
}

public sealed class BuiltRegistryConfig
{
    public static NodeDataKey<BuiltRegistryConfig> Key => new(RegistryConfig.Key.Value);

    /// <summary>Minimum spacing between registry HTTP requests. Zero disables throttling for experiments.</summary>
    public static readonly TimeSpan DefaultHttpCallDelay = TimeSpan.FromSeconds(0.5);

    public required CredentialsSource Credentials { get; set; }
    public required ExtraLessonInstanceAction ExtraLessonInstanceAction { get; set; }
    public required IEquationCommandsDerivation EquationCommandsDerivation { get; set; }
    public required CommandProcessingConfig CommandProcessingConfig { get; set; }
    public IReadOnlyList<RegistryGroupMatchRule> GroupMatchRules { get; set; } = [];
    public IReadOnlyList<RegistryLabMergeRule> LabMerges { get; set; } = [];
    public IReadOnlyList<RegistryAssessmentRule> Assessments { get; set; } = [];
    public TimeSpan HttpCallDelay { get; set; } = DefaultHttpCallDelay;
    public IReadOnlyDictionary<string, string> CourseNames { get; set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> StudentNames { get; set; } = new Dictionary<string, string>();
    public int CourseNameMaxDistance { get; set; } = 2;
    public AttendanceMode? SkipAttendance { get; set; }
}

public sealed class RegistryConfigMapper : IDataMapper<RegistryConfig, BuiltRegistryConfig>
{
    public BuiltRegistryConfig Map(RegistryConfig input)
    {
        var delay = input.HttpCallDelay ?? BuiltRegistryConfig.DefaultHttpCallDelay;
        if (delay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("Registry HttpCallDelay must be nonnegative.");
        }

        if (input.CourseNameMaxDistance is < 0)
        {
            throw new InvalidOperationException("Registry CourseNameMaxDistance must be nonnegative.");
        }

        return new()
        {
            GroupMatchRules = input.GroupMatchRules,
            LabMerges = input.LabMerges,
            Assessments = input.Assessments,
            ExtraLessonInstanceAction = input.ExtraLessonInstanceAction ?? ExtraLessonInstanceAction.LeaveAlone,
            CommandProcessingConfig = input.CommandProcessingConfig ?? CommandProcessingConfig.DryRun,
            Credentials = input.Credentials ?? throw new InvalidOperationException("No credentials configured"),
            EquationCommandsDerivation =
                input.EquationCommandsDerivation
                ?? throw new InvalidOperationException("No command derivation configured"),
            HttpCallDelay = delay,
            CourseNames = input.CourseNames ?? new Dictionary<string, string>(),
            StudentNames = input.StudentNames ?? new Dictionary<string, string>(),
            CourseNameMaxDistance = input.CourseNameMaxDistance ?? 2,
            SkipAttendance = input.SkipAttendance,
        };
    }
}

public static class ConfigExtensions
{
    extension(IServiceCollection services)
    {
        public void AddOnlineRegistry()
        {
            RegistryConfig.Register(services);

            {
                var h = services.AddOpenHierarchy<IEquationCommandsDerivation>();
                h.AddDerived<AnyDayDerivation>()
                    .SetImmutable();
                h.AddDerived<SameDayDerivation>()
                    .SetImmutable();
            }
            // TODO: This should function as a provider then? doing work in DI is not good.
            services.AddScoped<IRegistryErrorHandler>(sp =>
            {
                var config = sp.GetRequiredService<DataProvider<BuiltRegistryConfig>>()
                    .Get();
                if (config is null)
                {
                    throw new InvalidOperationException("Registry not enabled.");
                }
                var ret = ActivatorUtilities.CreateInstance<RegistryErrorLogger>(sp);
                ret.ExtraLessonAction = config.ExtraLessonInstanceAction;
                return ret;
            });

            services.AddScoped<RegistrySyncDiagnostics>();
            AttendanceListsExcelParser.Register(services);
        }
    }

    extension(NodeBuilder builder)
    {
        public NodeDataBuilder<RegistryConfig> Registry() => builder.Builder<RegistryConfig>();
    }

    extension(NodeDataBuilder<RegistryConfig> builder)
    {
        public void ExtraLessonAction(ExtraLessonInstanceAction action)
        {
            builder.Value().ExtraLessonInstanceAction = action;
        }

        public void CommandDerivation<T>() where T : IEquationCommandsDerivation, new()
        {
            builder.Value().EquationCommandsDerivation = new T();
        }

        public void ProcessingFlags(CommandProcessingConfig flags)
        {
            builder.Value().CommandProcessingConfig = flags;
        }

        public void HttpCallDelay(TimeSpan delay)
        {
            builder.Value().HttpCallDelay = delay;
        }
    }
}
