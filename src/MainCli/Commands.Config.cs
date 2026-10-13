using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public ConfigCommands Config
    {
        get;
        set;
    } = new();
}

public sealed class ConfigKeyArguments : IArgumentModel
{
    [Operand(
        "key",
        Description =
            "Registered block name followed by optional camelCase member path, e.g. GoogleCalendarConfig.calendarName.")]
    public string Key
    {
        get;
        set;
    } = null!;
}

[Command(
    "config",
    Description = "Inspect or edit layered settings without loading schedule sources or contacting providers.")]
public sealed partial class ConfigCommands
{
    [Command("show", Description = "Show resolved typed settings and contributing sources; secrets are redacted.")]
    public Task<int> Show(
        SettingsArguments settings,
        ConfigOverrideArguments overrides,
        ResultArguments output,
        CancellationToken cancellationToken = default) =>
        Execute(
            "show",
            settings,
            overrides,
            output,
            resolved => new { resolved.ProjectDirectory, resolved.Profile, Settings = resolved.Inspect() },
            cancellationToken);

    [Command("get", Description = "Read a resolved setting with its contributing sources.")]
    public Task<int> Get(
        ConfigKeyArguments key,
        SettingsArguments settings,
        ConfigOverrideArguments overrides,
        ResultArguments output,
        CancellationToken cancellationToken = default) =>
        Execute(
            "get",
            settings,
            overrides,
            output,
            resolved => resolved.Inspect(key.Key),
            cancellationToken);

    [Command(
        "validate",
        Description =
            "Validate both files and all profile overlays; no schedule or provider prerequisites are needed.")]
    public Task<int> Validate(
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default) =>
        Execute(
            "validate",
            settings,
            new(),
            output,
            resolved => new { Valid = true, resolved.ProjectDirectory, resolved.Profile, resolved.Sources },
            cancellationToken);

    [Command("profiles", Description = "List code-defined teacher profiles; JSON adds overlays to these identities.")]
    public Task<int> Profiles(
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default) =>
        Execute(
            "profiles",
            settings,
            new(),
            output,
            resolved => resolved.Profiles,
            cancellationToken);

    private static async Task<int> Execute(
        string verb,
        SettingsArguments settings,
        ConfigOverrideArguments overrides,
        ResultArguments outputArgs,
        Func<ResolvedSettings, object> inspect,
        CancellationToken cancellationToken)
    {
        var command = "config " + verb;
        using var context = new CliCommandContext(
            command,
            outputArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            using var resolved = await CliSettings.Load(
                settings,
                cancellationToken: cancellation.Token,
                overrides: overrides);
            return CliConfigExecution.Finish(
                context,
                CliExit.Success,
                [],
                inspect(resolved),
                verb);
        }
        catch (Exception error)
        {
            var failure = CliConfigExecution.ClassifyRead(error);
            return CliConfigExecution.Finish<object?>(
                context,
                failure.ExitCode,
                failure.Errors,
                null,
                verb);
        }
    }
}
