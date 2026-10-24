using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed class ConfigItemArguments : IArgumentModel
{
    [Option(
        "item",
        Description =
            "Typed JSON item identifying the registered collection key (for example {\"lessonType\":\"Lab\"}).")]
    public string? Item { get; set; }
}

public sealed partial class ConfigCommands
{
    [Command("remove", Description = "Suppress a whole block or a keyed collection item in an explicit scope.")]
    public Task<int> Remove(
        ConfigKeyArguments key,
        ConfigItemArguments item,
        ConfigScopeArguments scope,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        global::ScheduleLib.Cli.ConfigEditRequest request = new(
            Verb: "remove",
            Key: key.Key,
            Value: null,
            scope,
            settings,
            output,
            cancellationToken,
            Item: item.Item);
        global::System.Threading.Tasks.Task<int> write = Write(request);
        return write;
    }

    [Command("clear", Description = "Explicitly empty an inherited collection in an explicit scope.")]
    public Task<int> Clear(
        ConfigKeyArguments key,
        ConfigScopeArguments scope,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        global::ScheduleLib.Cli.ConfigEditRequest request = new(
            Verb: "clear",
            Key: key.Key,
            Value: null,
            scope,
            settings,
            output,
            cancellationToken);
        global::System.Threading.Tasks.Task<int> write = Write(request);
        return write;
    }
}
