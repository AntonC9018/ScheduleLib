using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.AttendanceExcel;
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

    public static void Register(IServiceCollection services)
    {
        services.RegisterBasicOperationsAndMergers<RegistryConfig>();
        services.AddConfigProvider(BuiltRegistryConfig.Key);
        services.AddMapper<RegistryConfigMapper>();
        services.ConfigureNodeDataJsonSerialization(
            CommandProcessingConfigJsonConverter.Register);
    }
}

public sealed class BuiltRegistryConfig
{
    public static NodeDataKey<BuiltRegistryConfig> Key => new(RegistryConfig.Key.Value);

    public required CredentialsSource Credentials { get; set; }
    public required ExtraLessonInstanceAction ExtraLessonInstanceAction { get; set; }
    public required IEquationCommandsDerivation EquationCommandsDerivation { get; set; }
    public required CommandProcessingConfig CommandProcessingConfig { get; set; }
}

public sealed class RegistryConfigMapper : IDataMapper<RegistryConfig, BuiltRegistryConfig>
{
    public BuiltRegistryConfig Map(RegistryConfig input)
    {
        return new()
        {
            ExtraLessonInstanceAction = input.ExtraLessonInstanceAction ?? ExtraLessonInstanceAction.LeaveAlone,
            CommandProcessingConfig = input.CommandProcessingConfig ?? CommandProcessingConfig.DryRun,
            Credentials = input.Credentials ?? throw new InvalidOperationException("No credentials configured"),
            EquationCommandsDerivation = input.EquationCommandsDerivation ?? throw new InvalidOperationException("No command derivation configured"),
        };
    }
}

public static class ConfigExtensions
{
    extension (IServiceCollection services)
    {
        public void AddOnlineRegistry()
        {
            RegistryConfig.Register(services);

            // TODO: This should function as a provider then? doing work in DI is not good.
            services.AddScoped<IRegistryErrorHandler>(sp =>
            {
                var config = sp.GetRequiredService<DataProvider<BuiltRegistryConfig>>().Get();
                if (config is null)
                {
                    throw new InvalidOperationException("Registry not enabled.");
                }
                var ret = ActivatorUtilities.CreateInstance<RegistryErrorLogger>(sp);
                ret.ExtraLessonAction = config.ExtraLessonInstanceAction;
                return ret;
            });

            AttendanceListsExcelParser.Register(services);
        }
    }
    extension (NodeBuilder builder)
    {
        public NodeDataBuilder<RegistryConfig> Registry() => builder.Builder<RegistryConfig>();
    }
    extension (NodeDataBuilder<RegistryConfig> builder)
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
    }
}
