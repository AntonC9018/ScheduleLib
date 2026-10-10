using System.Text.Json;
using CommandDotNet;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public ConfigCommands Config { get; set; } = new();
}

public sealed class ConfigKeyArguments : IArgumentModel
{
    [Operand("key", Description = "Registered block name followed by optional camelCase member path, e.g. GoogleCalendarConfig.calendarName.")]
    public string Key { get; set; } = null!;
}

[Command("config", Description = "Inspect or edit layered settings without loading schedule sources or contacting providers.")]
public sealed partial class ConfigCommands
{
    [Command("show", Description = "Show resolved typed settings and contributing sources; secrets are redacted.")]
    public Task<int> Show(SettingsArguments settings, ConfigOverrideArguments overrides, ResultArguments output, CancellationToken cancellationToken = default) =>
        Execute("show", settings, overrides, output, resolved => new { resolved.ProjectDirectory, resolved.Profile, Settings = resolved.Inspect() }, cancellationToken);

    [Command("get", Description = "Read a resolved setting with its contributing sources.")]
    public Task<int> Get(ConfigKeyArguments key, SettingsArguments settings, ConfigOverrideArguments overrides, ResultArguments output, CancellationToken cancellationToken = default) =>
        Execute("get", settings, overrides, output, resolved => resolved.Inspect(key.Key), cancellationToken);

    [Command("validate", Description = "Validate both files and all profile overlays; no schedule or provider prerequisites are needed.")]
    public Task<int> Validate(SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) =>
        Execute("validate", settings, new(), output, resolved => new { Valid = true, resolved.ProjectDirectory, resolved.Profile, resolved.Sources }, cancellationToken);

    [Command("profiles", Description = "List code-defined teacher profiles; JSON adds overlays to these identities.")]
    public Task<int> Profiles(SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) =>
        Execute("profiles", settings, new(), output, resolved => resolved.Profiles, cancellationToken);

    private static async Task<int> Execute(string verb, SettingsArguments settings, ConfigOverrideArguments overrides, ResultArguments outputArgs,
        Func<ResolvedSettings, object> inspect, CancellationToken cancellationToken)
    {
        var command = "config " + verb;
        var runId = Guid.NewGuid().ToString("N");
        if (CliOutput.ExclusiveError(outputArgs) is { } exclusive)
        {
            Console.Error.WriteLine(exclusive);
            return 2;
        }
        var output = new CliOutput(command, runId, outputArgs);
        output.EmitStart();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token, overrides: overrides);
            return Finish(0, inspect(resolved), []);
        }
        catch (OperationCanceledException) { return Finish(130, null, ["Cancelled."]); }
        catch (JsonException e) { return Finish(3, null, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, null, [e.Message]); }
        catch (IOException e) { return Finish(5, null, [e.Message]); }
        catch (UnauthorizedAccessException e) { return Finish(5, null, [e.Message]); }
        catch (Exception e) { return Finish(1, null, [e.Message]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, object? data, string[] errors)
        {
            foreach (var error in errors) output.WriteError(error);
            var jsonStatus = exit == 0 ? "succeeded" : "failed";
            var jsonlStatus = exit == 0 ? "succeeded" : exit == 130 ? "cancelled" : "failed";
            var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = !outputArgs.Json };
            if (outputArgs.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<object?>(1, command, runId,
                    jsonStatus, exit, [], [], [], errors, data), json));
            else if (output.Jsonl)
            {
                if (exit == 0 && verb == "profiles" && data is IReadOnlyList<string> profiles)
                {
                    for (var index = 0; index < profiles.Count; index++)
                    {
                        output.EmitItem(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "completed", new { profile = profiles[index] });
                    }
                    output.EmitFinal(jsonlStatus, exit, [], errors, new { profileCount = profiles.Count });
                }
                else
                {
                    output.EmitFinal(jsonlStatus, exit, [], errors, data);
                }
            }
            else if (exit == 0 && verb == "profiles" && data is IReadOnlyList<string> profiles)
            {
                foreach (var profile in profiles) output.WriteHuman(profile);
                output.WriteHuman($"succeeded: {profiles.Count} profiles.");
            }
            else if (data is not null) output.WriteHuman(JsonSerializer.Serialize(data, json));
            return exit;
        }
    }
}
