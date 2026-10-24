using System.Text.Json;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

internal static class CliAuthenticationExecution
{
    internal static CliFailure ClassifyGoogle(Exception error)
    {
        return error switch
        {
            OperationCanceledException => new(CliExit.Cancelled, ["Cancelled."]),
            AuthenticationRequiredException e => new(CliExit.AuthenticationRequired, [e.Message]),
            LocalOperationBusyException => new(
                CliExit.Busy,
                ["Another authentication operation owns this teacher's local state."]),
            JsonException e => new(CliExit.InvalidConfiguration, [e.Message]),
            DirectoryNotFoundException e => new(CliExit.InvalidConfiguration, [e.Message]),
            IOException => new(CliExit.IoFailure, ["Could not read or update local authorization state."]),
            UnauthorizedAccessException => new(CliExit.IoFailure, ["Local authorization state is inaccessible."]),
            _ => new(
                CliExit.Unexpected,
                ["Authentication operation failed. Check the configured Google client credentials."]),
        };
    }

    internal static CliFailure ClassifyMicrosoft(Exception error, bool cancellationRequested)
    {
        return error switch
        {
            OperationCanceledException when cancellationRequested => new(CliExit.Cancelled, ["Cancelled."]),
            AuthenticationRequiredException e => new(CliExit.AuthenticationRequired, [e.Message]),
            LocalOperationBusyException => new(
                CliExit.Busy,
                ["Another Microsoft authentication operation owns this profile's local state."]),
            JsonException e => new(CliExit.InvalidConfiguration, [e.Message]),
            DirectoryNotFoundException e => new(CliExit.InvalidConfiguration, [e.Message]),
            IOException => new(CliExit.IoFailure, ["Could not read or update local Microsoft authorization state."]),
            UnauthorizedAccessException => new(CliExit.IoFailure, ["Local Microsoft authorization state is inaccessible."]),
            _ => new(CliExit.Unexpected, ["Microsoft authentication operation failed unexpectedly."]),
        };
    }

    internal static int Finish(
        CliCommandContext context,
        int exit,
        string[] errors,
        AuthStatus? data,
        string providerDisplay)
    {
        var cli = context.Output;
        var status = CliCommandContext.Status(exit);

        if (data is null)
        {
            return context.Finish(new CliCompletion<AuthStatus?>(
                exit,
                errors,
                data)
            {
                Summary = data,
                Status = status,
                JsonStatus = status,
            });
        }

        for (var index = 0; index < data.Accounts.Count; index++)
        {
            var account = data.Accounts[index];
            cli.EmitItem(
                id: index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                status: "completed",
                account);
        }

        return context.Finish(new CliCompletion<AuthStatus?>(
            exit,
            errors,
            data)
        {
            Summary = new
            {
                data.Provider,
                data.Profile,
                accountCount = data.Accounts.Count,
                provisioned = data.Accounts.Count != 0,
            },
            Status = status,
            JsonStatus = status,
            RenderHuman = _ =>
            {
                foreach (var account in data.Accounts)
                {
                    cli.WriteHuman($"{account.AccountId}: {account.State}");
                }

                var provisioned = "provisioned";
                if (data.Accounts.Count == 0)
                {
                    provisioned = "not provisioned";
                }
                cli.WriteHuman($"{providerDisplay} authorization for {data.Profile}: {provisioned}");
            },
        });
    }
}
