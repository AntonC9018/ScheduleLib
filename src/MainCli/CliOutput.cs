using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
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
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly object _gate = new();
    private int _stdoutSeq;
    private int _stderrSeq;

    public string Command
    {
        get;
    }

    public string RunId
    {
        get;
    }

    public bool Json
    {
        get;
    }

    public bool Jsonl
    {
        get;
    }

    public CliOutput(
        string command,
        string runId,
        ResultArguments result)
        : this(
            command,
            runId,
            result.Json,
            GetJsonl(result))
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

    public static string? ExclusiveError(ResultArguments result) =>
        result.Json && result.Jsonl ? ConflictingResultFormatsMessage : null;

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
        string status,
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
        string status,
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

    public void WriteWarning(string message)
    {
        if (Jsonl)
        {
            WriteStderrLog("Warning", message);
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
            WriteStderrLog("Error", message);
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
            message,
            exception,
            null,
            null,
            null,
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
            level.ToString(),
            message,
            exception,
            properties,
            scopes,
            category,
            timestamp);
    }

    private void WriteStderrLog(
        string level,
        string message,
        string? exception = null)
    {
        WriteStderrLog(
            level,
            message,
            exception,
            null,
            null,
            null,
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
public class CliOutputStreamingException : Exception
{
    public CliOutputStreamingException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

public sealed class CliJsonlLoggerProvider(CliOutput output) : ILoggerProvider, ISupportExternalScope
{
    private IExternalScopeProvider _scopes = new LoggerExternalScopeProvider();
    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) => new Logger(
        output,
        categoryName,
        () => _scopes);

    public void Dispose()
    {
    }

    private sealed class Logger(CliOutput output, string category, Func<IExternalScopeProvider> scopeProvider)
        : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => scopeProvider().Push(state);

        // Matches the original Serilog console configuration, whose default
        // minimum is Information. Debug/Trace never reached stderr before and
        // must not appear as JSONL either; Information/Warning/Error/Critical
        // preserve their existing content, levels and occurrence.
        public bool IsEnabled(LogLevel logLevel) =>
            logLevel is >= LogLevel.Information and <= LogLevel.Critical;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            var properties = ExtractProperties(state)?.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal) ??
                new Dictionary<string, object?>(StringComparer.Ordinal);
            var scopes = new List<string>();
            scopeProvider().ForEachScope(
                (scope, collected) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> named)
                    {
                        foreach (var pair in named)
                        {
                            properties.TryAdd(pair.Key, pair.Value);
                        }
                    }

                    collected.Add(scope?.ToString() ?? string.Empty);
                },
                scopes);
            output.WriteStructuredLog(
                logLevel,
                message,
                exception?.ToString(),
                properties.Count > 0 ? properties : null,
                scopes.Count > 0 ? scopes : null,
                category,
                DateTimeOffset.UtcNow);
        }

        private static IReadOnlyDictionary<string, object?>? ExtractProperties<TState>(TState state)
        {
            Dictionary<string, object?>? collected = null;
            if (state is IEnumerable<KeyValuePair<string, object?>> named)
            {
                foreach (var (key, value) in named)
                {
                    // Mirrors the original "{Properties:j}" shape: structured values
                    // without the message template itself. EventId/SourceContext were
                    // stripped by the original Serilog enrichers, so they stay out.
                    if (key == "{OriginalFormat}")
                    {
                        continue;
                    }

                    collected ??= new(StringComparer.Ordinal);
                    collected[key] = value;
                }
            }
            return collected;
        }
    }
}

public static class CliLogging
{
    public static void ReplaceWithJsonl(IServiceCollection services, CliOutput output)
    {
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new CliJsonlLoggerProvider(output));
        });
    }
}

public static class CliAmbient
{
    private static readonly AsyncLocal<CliOutput?> CurrentValue = new();

    public static CliOutput? Current
    {
        get => CurrentValue.Value;
        set => CurrentValue.Value = value;
    }
}
