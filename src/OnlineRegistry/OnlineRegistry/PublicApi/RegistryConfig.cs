using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.OnlineRegistry;

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

public sealed class BuiltRegistryConfig
{
    public static LayerConfigKey<BuiltRegistryConfig> Key => new(RegistryConfig.Key.Value);

    public required CredentialsSource Credentials { get; set; }
    public required ExtraLessonInstanceAction ExtraLessonInstanceAction { get; set; }
    public required IEquationCommandsDerivation EquationCommandsDerivation { get; set; }
    public required CommandProcessingConfig CommandProcessingConfig { get; set; }
}

public sealed class RegistryConfigMapper : IConfigMapper<RegistryConfig, BuiltRegistryConfig>
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
    extension (ServiceCollection services)
    {
        public void AddOnlineRegistry()
        {
            services.RegisterBasicOperationsAndMergers<RegistryConfig>();
            services.AddConfigProvider(BuiltRegistryConfig.Key);
            services.AddMapper<RegistryConfigMapper>();

            // TODO: This should function as a provider then? doing work in DI is not good.
            services.AddScoped<IRegistryErrorHandler>(sp =>
            {
                var config = sp.GetRequiredService<ConfigProvider<BuiltRegistryConfig>>().Get();
                if (config is null)
                {
                    throw new InvalidOperationException("Registry not enabled.");
                }
                var ret = ActivatorUtilities.CreateInstance<RegistryErrorLogger>(sp);
                if (config.ExtraLessonInstanceAction is { } action)
                {
                    ret.ExtraLessonAction = action;
                }
                return ret;
            });
        }
    }
    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<RegistryConfig> Registry() => builder.Builder<RegistryConfig>();
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
}
