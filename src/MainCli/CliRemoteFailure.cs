using System.Text.Json;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

/// <summary>Provider-specific public messages; exception details stay private.</summary>
internal sealed record GoogleFailureMessages
{
    public required string Timeout { get; init; }

    public required string Network { get; init; }

    public required string Busy { get; init; }

    public required string Io { get; init; }

    public required string Access { get; init; }

    public required string Rejected { get; init; }

    public required string Unexpected { get; init; }

    public bool IncludeIoDetails { get; init; }
}

internal static class CliRemoteFailure
{
    public static CliFailure ClassifyGoogle(
        Exception error,
        CliCommandContext context,
        string? profile,
        GoogleFailureMessages messages,
        bool partialBundle = false)
    {
        if (context.Cancellation.IsCancellationRequested
            && error is OperationCanceledException or GooglePreSendTransportException)
        {
            return new(CliExit.Cancelled, ["Cancelled."]);
        }
        if (error is AuthenticationRequiredException authentication)
        {
            return new(CliExit.AuthenticationRequired, [authentication.Message]);
        }
        if (error is Google.Apis.Auth.OAuth2.Responses.TokenResponseException
            || error is Google.GoogleApiException { HttpStatusCode: System.Net.HttpStatusCode.Unauthorized })
        {
            var authenticationError = new AuthenticationRequiredException(provider: "google", teacher: profile ?? "");
            return new(CliExit.AuthenticationRequired, [authenticationError.Message]);
        }
        var ioExit = CliExit.IoFailure;
        var unexpectedExit = CliExit.Unexpected;
        if (partialBundle)
        {
            ioExit = CliExit.Partial;
            unexpectedExit = CliExit.Partial;
        }
        return error switch
        {
            CliOutputStreamingException => new(CliExit.Unexpected, ["Output streaming failed."]),
            OperationCanceledException => new(CliExit.IoFailure, [messages.Timeout]),
            HttpRequestException => new(CliExit.IoFailure, [messages.Network]),
            LocalOperationBusyException => new(CliExit.Busy, [messages.Busy]),
            JsonException
                or ArgumentException
                or DirectoryNotFoundException
                or FileNotFoundException
                or InvalidScheduleSourceException
                or ScheduleBuildException => new(CliExit.InvalidConfiguration, [error.Message]),
            PlatformNotSupportedException => new(CliExit.Unsupported, [error.Message]),
            Google.GoogleApiException => new(CliExit.IoFailure, [messages.Rejected]),
            UnauthorizedAccessException => new(ioExit, [messages.Access]),
            IOException when messages.IncludeIoDetails => new(ioExit, [error.Message]),
            IOException => new(ioExit, [messages.Io]),
            _ => new(unexpectedExit, [messages.Unexpected]),
        };
    }
}
