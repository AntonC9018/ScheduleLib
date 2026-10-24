using System.Text.Json;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

internal sealed record ConfigEditRequest(
    string Verb,
    string Key,
    string? Value,
    ConfigScopeArguments Scope,
    SettingsArguments Settings,
    ResultArguments Output,
    CancellationToken CancellationToken,
    string? Item = null);

internal static class CliConfigExecution
{
    private static readonly JsonSerializerOptions HumanJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    internal static CliFailure ClassifyRead(Exception error)
    {
        return error switch
        {
            OperationCanceledException => new(CliExit.Cancelled, ["Cancelled."]),
            JsonException e => new(CliExit.InvalidConfiguration, [e.Message]),
            DirectoryNotFoundException e => new(CliExit.InvalidConfiguration, [e.Message]),
            IOException e => new(CliExit.IoFailure, [e.Message]),
            UnauthorizedAccessException e => new(CliExit.IoFailure, [e.Message]),
            _ => new(CliExit.Unexpected, [error.Message]),
        };
    }

    internal static CliFailure ClassifyWrite(Exception error)
    {
        return error switch
        {
            OperationCanceledException => new(CliExit.Cancelled, ["Cancelled."]),
            JsonException e => new(CliExit.InvalidConfiguration, [e.Message]),
            DirectoryNotFoundException e => new(CliExit.InvalidConfiguration, [e.Message]),
            LocalOperationBusyException e => new(CliExit.Busy, [e.Message]),
            IOException e => new(CliExit.IoFailure, [e.Message]),
            UnauthorizedAccessException e => new(CliExit.IoFailure, [e.Message]),
            _ => new(CliExit.Unexpected, [error.Message]),
        };
    }

    internal static int Finish<T>(
        CliCommandContext context,
        int exit,
        string[] errors,
        T data,
        string verb)
    {
        var cli = context.Output;
        var status = CliCommandContext.Status(exit);
        var jsonStatus = CliCommandContext.Status(exit, cancellation: false);

        if (exit == CliExit.Success && verb == "profiles" && data is IReadOnlyList<string> profiles)
        {
            for (var index = 0; index < profiles.Count; index++)
            {
                cli.EmitItem(
                    id: index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    status: "completed",
                    new { profile = profiles[index] });
            }

            return context.Finish(new CliCompletion<T>(
                exit,
                errors,
                data)
            {
                Summary = new { profileCount = profiles.Count },
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = _ =>
                {
                    foreach (var profile in profiles)
                    {
                        cli.WriteHuman(profile);
                    }

                    cli.WriteHuman($"succeeded: {profiles.Count} profiles.");
                },
            });
        }

        return context.Finish(new CliCompletion<T>(
            exit,
            errors,
            data)
        {
            Summary = data,
            Status = status,
            JsonStatus = jsonStatus,
            RenderHuman = _ =>
            {
                if (data is not null)
                {
                    cli.WriteHuman(JsonSerializer.Serialize(data, HumanJson));
                }
            },
        });
    }
}
