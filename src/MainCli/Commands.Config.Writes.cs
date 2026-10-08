using System.Text.Json;
using CommandDotNet;
using ScheduleLib.Application.Core;

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
    [Command("set", Description = "Persist a typed JSON override in an explicit scope; optionally overlay an existing teacher.")]
    public Task<int> Set(ConfigSetArguments edit, ConfigScopeArguments scope, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) =>
        Write("set", edit.Key, edit.Value, scope, settings, output, cancellationToken);

    [Command("unset", Description = "Remove the chosen scope's override and restore inheritance.")]
    public Task<int> Unset(ConfigKeyArguments key, ConfigScopeArguments scope, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) =>
        Write("unset", key.Key, null, scope, settings, output, cancellationToken);

    private static async Task<int> Write(string verb, string key, string? value, ConfigScopeArguments scope, SettingsArguments settings,
        ResultArguments output, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var destination = scope.Scope switch { "user" => SettingsScope.User, "project" => SettingsScope.Project, _ => (SettingsScope?)null };
            if (destination is null)
                return Finish(2, null, ["--scope user|project is required."]);
            var result = await CliSettings.Edit(settings, destination.Value, key, value, verb == "unset", cancellation.Token);
            return Finish(0, result, []);
        }
        catch (OperationCanceledException) { return Finish(130, null, ["Cancelled."]); }
        catch (JsonException e) { return Finish(3, null, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, null, [e.Message]); }
        catch (LocalOperationBusyException e) { return Finish(7, null, [e.Message]); }
        catch (IOException e) { return Finish(5, null, [e.Message]); }
        catch (UnauthorizedAccessException e) { return Finish(5, null, [e.Message]); }
        catch (Exception e) { return Finish(1, null, [e.Message]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, SettingsEdit? data, string[] errors)
        {
            foreach (var error in errors) Console.Error.WriteLine(error);
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = !output.Json };
            if (output.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<SettingsEdit?>(1, "config " + verb, runId,
                    exit == 0 ? "succeeded" : "failed", exit, [], [], [], errors, data), json));
            else if (data is not null) Console.WriteLine(JsonSerializer.Serialize(data, json));
            return exit;
        }
    }
}
