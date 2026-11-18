using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ScheduleLib.Cli;

/// <summary>
/// Shared stdout/stderr contract for all CLI commands.
/// Human output remains the default. <c>--json</c> keeps the existing single
/// complete versioned envelope on stdout with plain-text stderr. <c>--jsonl</c>
/// streams newline-delimited JSON records on stdout (start, items as they become
/// available, final summary) and renders every stderr warning/error/log as JSONL.
///
/// Sequence scope: stdout <c>seq</c> orders records within the stdout stream
/// starting at 0 (start is 0, final is last); stderr <c>seq</c> independently
/// orders records within the stderr stream starting at 0. Both streams share
/// <c>command</c> and <c>runId</c> for correlation. A missing final record means
/// the run did not complete (crash/kill); normal cancellation still emits a
/// cancelled final when possible.
/// </summary>
public sealed class CliOutput
{
    public const string ConflictingResultFormatsMessage = "Only one of --json and --jsonl may be specified.";

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    private readonly object _gate = new();
    private int _stdoutSeq;
    private int _stderrSeq;

    public string Command { get; }

    public string RunId { get; }

    public bool Json { get; }

    public bool Jsonl { get; }

    public CliOutput(
        string command,
        string runId,
        ResultArguments result)
        : this(
            command: command,
            runId: runId,
            json: result.Json,
            jsonl: GetJsonl(result))
    {
    }

    public CliOutput(
        string command,
        string runId,
        bool json,
        bool jsonl)
    {
        Command = command;
        RunId = runId;
        Json = json;
        Jsonl = jsonl;
    }

    public static bool GetJsonl(ResultArguments result) => result.Jsonl;

    public static string? ExclusiveError(ResultArguments result)
    {
        if (!result.Json)
        {
            return null;
        }
        if (!result.Jsonl)
        {
            return null;
        }
        return ConflictingResultFormatsMessage;
    }

    public void EmitStart()
    {
        if (!Jsonl)
        {
            return;
        }

        WriteStdoutRecord((seq) => JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                command = Command,
                runId = RunId,
                seq,
                @event = "start",
            },
            JsonOptions));
    }

    public void EmitItem(
        string id,
        ActionState status,
        object? item)
    {
        if (!Jsonl)
        {
            return;
        }

        WriteStdoutRecord((seq) => JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                command = Command,
                runId = RunId,
                seq,
                @event = "item",
                id,
                status,
                item,
            },
            JsonOptions));
    }

    public void EmitFinal(
        RunStatus status,
        int exitCode,
        string[] warnings,
        string[] errors,
        object? data)
    {
        if (!Jsonl)
        {
            return;
        }

        WriteStdoutRecord((seq) => JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                command = Command,
                runId = RunId,
                seq,
                @event = "final",
                status,
                exitCode,
                warnings,
                errors,
                data,
            },
            JsonOptions));
    }

    public void WriteHuman(string line)
    {
        if (Json || Jsonl)
        {
            return;
        }

        lock (_gate)
        {
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    public void WriteHuman<TMessage>(TMessage message) where TMessage : ICliMessage
    {
        if (Json || Jsonl)
        {
            return;
        }

        string line = message.Format(System.Globalization.CultureInfo.CurrentCulture);
        WriteHuman(line);
    }

    public void WriteWarning(string message)
    {
        if (Jsonl)
        {
            WriteStderrLog(level: "Warning", message: message);
        }
        else
        {
            lock (_gate)
            {
                Console.Error.WriteLine(message);
                Console.Error.Flush();
            }
        }
    }

    public void WriteError(string message)
    {
        if (Jsonl)
        {
            WriteStderrLog(level: "Error", message: message);
        }
        else
        {
            lock (_gate)
            {
                Console.Error.WriteLine(message);
                Console.Error.Flush();
            }
        }
    }

    public void WriteLog(
        LogLevel level,
        string message,
        string? exception = null)
    {
        if (!Jsonl)
        {
            return;
        }

        WriteStructuredLog(
            level,
            message: message,
            exception: exception,
            null,
            null,
            category: null,
            DateTimeOffset.UtcNow);
    }

    public void WriteStructuredLog(
        LogLevel level,
        string message,
        string? exception,
        IReadOnlyDictionary<string, object?>? properties,
        IReadOnlyList<string>? scopes,
        string? category,
        DateTimeOffset timestamp)
    {
        if (!Jsonl)
        {
            return;
        }

        WriteStderrLog(
            level: level.ToString(),
            message: message,
            exception: exception,
            properties,
            scopes,
            category: category,
            timestamp);
    }

    private void WriteStderrLog(
        string level,
        string message,
        string? exception = null)
    {
        WriteStderrLog(
            level: level,
            message: message,
            exception: exception,
            null,
            null,
            category: null,
            DateTimeOffset.UtcNow);
    }

    private void WriteStderrLog(
        string level,
        string message,
        string? exception,
        IReadOnlyDictionary<string, object?>? properties,
        IReadOnlyList<string>? scopes,
        string? category,
        DateTimeOffset timestamp)
    {
        WriteStderrRecord((seq) => JsonSerializer.Serialize(
            new
            {
                schemaVersion = 1,
                command = Command,
                runId = RunId,
                seq,
                @event = "log",
                level,
                message,
                timestamp = timestamp.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
                properties,
                scopes,
                category,
                exception,
            },
            JsonOptions));
    }

    private void WriteStdoutRecord(Func<int, string> format)
    {
        lock (_gate)
        {
            var seq = _stdoutSeq++;
            var line = format(seq);
            Console.Out.WriteLine(line);
            Console.Out.Flush();
        }
    }

    private void WriteStderrRecord(Func<int, string> format)
    {
        lock (_gate)
        {
            var seq = _stderrSeq++;
            var line = format(seq);
            Console.Error.WriteLine(line);
            Console.Error.Flush();
        }
    }
}

/// <summary>
/// Marks a failure to render/stream CLI output (console write, JSONL serialization).
/// Remote-operation classifiers must not interpret this as a remote failure or
/// uncertainty: the remote operation already completed and must not be retried
/// or duplicated. Callers stop further work when this escapes an output callback.
/// </summary>
public abstract class CliOutputStreamingException : Exception
{
    protected CliOutputStreamingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class GeneralCliOutputStreamingException : CliOutputStreamingException
{
    public GeneralCliOutputStreamingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
