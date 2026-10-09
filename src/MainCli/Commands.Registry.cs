using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public RegistryCommands Registry { get; set; } = new();
}
