using ScheduleLib.OnlineRegistry;

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

public static partial class Extensions
{
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
