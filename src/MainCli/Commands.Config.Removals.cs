using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed class ConfigItemArguments : IArgumentModel
{
    [Option("item", Description = "Typed JSON item identifying the registered collection key (for example {\"lessonType\":\"Lab\"}).")]
    public string? Item { get; set; }
}

public sealed partial class ConfigCommands
{
    [Command("remove", Description = "Suppress a whole block or a keyed collection item in an explicit scope.")]
    public Task<int> Remove(ConfigKeyArguments key, ConfigItemArguments item, ConfigScopeArguments scope, SettingsArguments settings,
        ResultArguments output, CancellationToken cancellationToken = default) =>
        Write("remove", key.Key, null, scope, settings, output, cancellationToken, item.Item);

    [Command("clear", Description = "Explicitly empty an inherited collection in an explicit scope.")]
    public Task<int> Clear(ConfigKeyArguments key, ConfigScopeArguments scope, SettingsArguments settings,
        ResultArguments output, CancellationToken cancellationToken = default) =>
        Write("clear", key.Key, null, scope, settings, output, cancellationToken);
}
