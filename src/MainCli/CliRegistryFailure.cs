using System.Text.Json;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

internal static class CliRegistryFailure
{
    public static CliFailure Classify(
        Exception error,
        bool attempted,
        bool grades)
    {
        var ioExit = CliExit.IoFailure;
        var unexpectedExit = CliExit.IoFailure;
        var streamingExit = CliExit.Unexpected;
        if (grades)
        {
            unexpectedExit = CliExit.Unexpected;
        }
        if (attempted)
        {
            ioExit = CliExit.Partial;
            unexpectedExit = CliExit.Partial;
            streamingExit = CliExit.Partial;
        }
        return error switch
        {
            CliOutputStreamingException => new(
                streamingExit,
                ["Output streaming failed. Completed actions remain applied; inspect the registry before retrying."]),
            OperationCanceledException => new(CliExit.Cancelled, ["Cancelled."]),
            RegistryAuthenticationException => new(CliExit.AuthenticationRequired, [error.Message]),
            LocalOperationBusyException => new(CliExit.Busy, [error.Message]),
            JsonException
                or DirectoryNotFoundException
                or FileNotFoundException
                or InvalidScheduleSourceException
                or ScheduleBuildException => new(CliExit.InvalidConfiguration, [error.Message]),
            ArgumentException => new(CliExit.InvalidArguments, [error.Message]),
            PlatformNotSupportedException => new(CliExit.Unsupported, [error.Message]),
            IOException when grades => new(
                ioExit,
                [WithCause("Registry input/output operation failed. No automatic retry was performed.", error)]),
            HttpRequestException when grades => new(
                ioExit,
                [WithCause("Registry/Moodle remote read failed. No automatic retry was performed.", error)]),
            InvalidOperationException when grades => new(
                ioExit,
                [WithCause("Registry/Moodle required form or report markup was unavailable. No automatic retry was performed.", error)]),
            _ when grades => new(
                unexpectedExit,
                [WithCause("Unexpected registry operation failure. No automatic retry was performed.", error)]),
            _ => new(unexpectedExit, [WithCause("Registry operation failed. No automatic retry was performed.", error)]),
        };
    }

    /// <summary>
    /// Appends the underlying exception so unclassified failures remain diagnosable.
    /// Inner exceptions are included because wrappers often carry no useful message.
    /// </summary>
    private static string WithCause(string summary, Exception error)
    {
        var causes = new List<string>();
        for (var current = error; current is not null; current = current.InnerException)
        {
            causes.Add($"{current.GetType().FullName}: {current.Message}");
        }
        var cause = string.Join(" ---> ", causes);
        return $"{summary} Cause: {cause}";
    }
}
