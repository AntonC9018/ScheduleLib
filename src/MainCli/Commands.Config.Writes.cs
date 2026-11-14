using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed class ConfigScopeArguments : IArgumentModel
{
    [Option("scope", Description = "Required destination: user or project.")]
    public string? Scope { get; set; }
}

public sealed class ConfigSetArguments : IArgumentModel
{
    [Operand("key", Description = "Registered block and optional camelCase member path.")]
    public string Key { get; set; } = null!;

    [Operand("value", Description = "Typed JSON value, including JSON quotes for strings.")]
    public string Value { get; set; } = null!;
}

public sealed partial class ConfigCommands
{
    [Command(
        "set",
        Description = "Persist a typed JSON override in an explicit scope; optionally overlay an existing teacher.")]
    public Task<int> Set(
        ConfigSetArguments edit,
        ConfigScopeArguments scope,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        ConfigEditRequest request = new(
            Verb: "set",
            Key: edit.Key,
            Value: edit.Value,
            scope,
            settings,
            output,
            cancellationToken);
        var write = Write(request);
        return write;
    }

    [Command("unset", Description = "Remove the chosen scope's override and restore inheritance.")]
    public Task<int> Unset(
        ConfigKeyArguments key,
        ConfigScopeArguments scope,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        ConfigEditRequest request = new(
            Verb: "unset",
            Key: key.Key,
            Value: null,
            scope,
            settings,
            output,
            cancellationToken);
        var write = Write(request);
        return write;
    }

    internal static async Task<int> Write(ConfigEditRequest request)
    {
        var command = $"config {request.Verb}";
        using var context = new CliCommandContext(
            command,
            request.Output,
            request.CancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cancellation = context.Cancellation;
        try
        {
            var destination = request.Scope.Scope switch
            {
                "user" => SettingsScope.User,
                "project" => SettingsScope.Project,
                _ => (SettingsScope?) null,
            };
            if (destination is null)
            {
                return CliConfigExecution.Finish(new ConfigFinishRequest<SettingsEdit?>(
                    Context: context,
                    Exit: CliExit.InvalidArguments,
                    Errors: ["--scope user|project is required."],
                    Data: null,
                    Verb: request.Verb));
            }

            string? operation = request.Verb is "remove" or "clear" ? request.Verb : null;

            SettingsEditRequest editRequest = new(
                Arguments: request.Settings,
                Scope: destination.Value,
                Key: request.Key,
                Value: request.Value,
                Unset: request.Verb == "unset",
                CancellationToken: cancellation.Token,
                Operation: operation,
                Item: request.Item);
            var result = await CliSettings.Edit(editRequest);
            return CliConfigExecution.Finish(new ConfigFinishRequest<SettingsEdit?>(
                Context: context,
                Exit: CliExit.Success,
                Errors: [],
                Data: result,
                Verb: request.Verb));
        }
        catch (Exception error)
        {
            var failure = CliConfigExecution.ClassifyWrite(error);
            return CliConfigExecution.Finish(new ConfigFinishRequest<SettingsEdit?>(
                Context: context,
                Exit: failure.ExitCode,
                Errors: failure.Errors,
                Data: null,
                Verb: request.Verb));
        }
    }
}
