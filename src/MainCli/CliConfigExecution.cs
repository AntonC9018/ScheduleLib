using System.Diagnostics.CodeAnalysis;
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

internal readonly record struct ConfigFinishRequest<T>(
    CliCommandContext Context,
    int Exit,
    string[] Errors,
    T Data,
    string Verb);

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

    private static bool TryGetProfileList<T>(
        ConfigFinishRequest<T> request,
        [NotNullWhen(true)] out IReadOnlyList<string>? profiles)
    {
        profiles = null;
        if (request.Exit != CliExit.Success)
        {
            return false;
        }

        if (request.Verb != "profiles")
        {
            return false;
        }

        if (request.Data is IReadOnlyList<string> list)
        {
            profiles = list;
            return true;
        }

        return false;
    }

    internal static int Finish<T>(ConfigFinishRequest<T> request)
    {
        var context = request.Context;
        var exit = request.Exit;
        var errors = request.Errors;
        var data = request.Data;
        var cli = context.Output;
        var status = CliCommandContext.Status(exit);
        var jsonStatus = CliCommandContext.Status(exit, cancellation: false);

        if (TryGetProfileList(request, out var profiles))
        {
            for (var index = 0; index < profiles.Count; index++)
            {
                var profileId = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                cli.EmitItem(
                    id: profileId,
                    status: ActionState.Completed,
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

                    string line = $"succeeded: {profiles.Count} profiles.";
                    cli.WriteHuman(line);
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
                    string line = JsonSerializer.Serialize(data, HumanJson);
                    cli.WriteHuman(line);
                }
            },
        });
    }
}
