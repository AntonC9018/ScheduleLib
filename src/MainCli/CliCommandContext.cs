using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

public static class CliExit
{
    public const int Success = 0;
    public const int Unexpected = 1;
    public const int InvalidArguments = 2;
    public const int InvalidConfiguration = 3;
    public const int AuthenticationRequired = 4;
    public const int IoFailure = 5;
    public const int Partial = 6;
    public const int Busy = 7;
    public const int Unsupported = 8;
    public const int Cancelled = 130;

    public static int AfterEffects(int exit, bool effects)
    {
        if (effects && exit != Success && exit != Cancelled)
        {
            return Partial;
        }
        return exit;
    }
}

public sealed record CliFailure(int ExitCode, string[] Errors);

/// <summary>One streamed progress update, with pre-rendered text for human output.</summary>
public readonly record struct StreamedUpdate(
    string Id,
    ActionState Status,
    object? Item,
    string Human);

/// <summary>Owns one invocation's output, cancellation, warnings and published artifacts.</summary>
public sealed class CliCommandContext : IDisposable
{
    private readonly ConsoleCancelEventHandler _cancel;
    private readonly CliRun? _run;
    private IDisposable? _logging;
    private int _streamed;
    private bool _started;

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

    public CliCommandContext(
        string command,
        ResultArguments arguments,
        CancellationToken token)
        : this(command, arguments, token, run: null)
    {
    }

    /// <summary>A context created through <paramref name="run"/>, whose result the run reports.</summary>
    /// <param name="command">The command name of the output's records.</param>
    /// <param name="arguments">The requested result format.</param>
    /// <param name="token">The invocation's cancellation, linked to Ctrl+C once started.</param>
    /// <param name="run">The host run, or none for a context created directly.</param>
    internal CliCommandContext(
        string command,
        ResultArguments arguments,
        CancellationToken token,
        CliRun? run)
    {
        _run = run;
        var runId = Guid.NewGuid()
            .ToString("N");
        Output = new CliOutput(
            command: command,
            runId: runId,
            arguments);
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _cancel = (_, e) =>
        {
            e.Cancel = true;
            Cancellation.Cancel();
        };
    }

    public CliOutput Output { get; }

    /// <summary>A logger writing to this command's output, for diagnostics outside a service provider.</summary>
    public ILogger CreateLogger(string category) =>
        Output.Jsonl ? CliLogging.CreateLogger(category) : NullLogger.Instance;

    public CancellationTokenSource Cancellation { get; }

    public List<string> Warnings { get; } = [];

    public RunOutput? Artifacts { get; set; }

    public string Command => Output.Command;
    public string RunId => Output.RunId;

    public bool Start()
    {
        if (Output.Json && Output.Jsonl)
        {
            Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
            return false;
        }
        _logging = CliLogging.Use(Output);
        _run?.Record(Output);
        Output.EmitStart();
        Console.CancelKeyPress += _cancel;
        _started = true;
        return true;
    }

    public static RunStatus Status(
        int exit,
        RunStatus success = RunStatus.Succeeded,
        bool partial = false,
        bool cancellation = true)
    {
        if (cancellation && exit == CliExit.Cancelled)
        {
            return RunStatus.Cancelled;
        }
        if (exit == CliExit.Success)
        {
            return success;
        }

        if (partial || exit == CliExit.Partial)
        {
            return RunStatus.Partial;
        }

        return RunStatus.Failed;
    }

    public int Finish<T>(CliCompletion<T> completion)
    {
        var exit = completion.ExitCode;
        var errors = completion.Errors;
        var data = completion.Data;
        var summary = completion.Summary;
        var paths = completion.Paths;
        var actions = completion.Actions;
        var warnings = completion.Warnings;
        var status = completion.Status ?? Status(exit);
        var jsonStatus = completion.JsonStatus;
        var human = completion.RenderHuman;
        var warningsFirst = completion.WarningsFirst;
        var shouldWriteWarning = completion.ShouldWriteWarning;
        warnings ??= Warnings.ToArray();
        if (warningsFirst)
        {
            WriteWarnings();
        }
        foreach (var error in errors)
        {
            Output.WriteError(error);
        }
        if (!warningsFirst)
        {
            WriteWarnings();
        }
        if (Output.Json)
        {
            if (completion.ActionMessages is { } messages)
            {
                actions = messages.Select(message => message.Format(System.Globalization.CultureInfo.CurrentCulture))
                    .ToArray();
            }
            var result = new CommandResult<T>(
                SchemaVersion: 1,
                Command: Command,
                RunId: RunId,
                Status: jsonStatus ?? status,
                ExitCode: exit,
                Outputs: paths ?? [],
                Actions: actions ?? [],
                Warnings: warnings,
                Errors: errors,
                data);
            string value = JsonSerializer.Serialize(result, JsonOptions);
            Console.WriteLine(value);
        }
        else if (Output.Jsonl)
        {
            Output.EmitFinal(
                status,
                exit,
                warnings: warnings,
                errors: errors,
                summary);
        }
        else
        {
            human?.Invoke(status.ToWire());
        }
        return exit;

        void WriteWarnings()
        {
            foreach (var warning in warnings)
            {
                if (shouldWriteWarning is null || shouldWriteWarning(warning))
                {
                    Output.WriteWarning(warning);
                }
            }
        }
    }

    public IServiceCollection CreateServices(
        ResolvedSettings settings,
        Action<IServiceCollection> configure)
    {
        var services = CliRuntime.CreateServices(configure);
        settings.ConfigureServices(services);
        return services;
    }

    public void StreamUpdate(StreamedUpdate update)
    {
        StreamItem(
            id: update.Id,
            status: update.Status,
            item: update.Item,
            renderHuman: () => Output.WriteHuman(update.Human));
    }

    public void StreamUpdate(CalendarReport update)
    {
        StreamItem(
            id: update.Id,
            status: update.Status,
            item: update.Item,
            renderHuman: () => Output.WriteHuman(update.Human));
    }

    public void StreamItem(
        string id,
        ActionState status,
        object? item,
        Action renderHuman)
    {
        try
        {
            renderHuman();
            Output.EmitItem(
                id: id,
                status: status,
                item);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            throw new GeneralCliOutputStreamingException("Output streaming failed.", error);
        }
    }

    public void StreamArtifacts()
    {
        if (Artifacts is null)
        {
            return;
        }
        while (_streamed < Artifacts.PublishedPaths.Count)
        {
            var index = _streamed++;
            var path = Artifacts.PublishedPaths[index];
            Output.WriteHuman(path);
            var artifactId = $"artifact:{path}";
            Output.EmitItem(
                id: artifactId,
                status: ActionState.Completed,
                new { path });
        }
    }

    public async Task<string[]> CompleteArtifacts(int exit)
    {
        if (exit != CliExit.Success)
        {
            if (Artifacts is { } artifacts)
            {
                if (artifacts.PublishedPaths.Count > 0)
                {
                    if (!artifacts.PublishedPaths.Contains(artifacts.ManifestPath))
                    {
                        try
                        {
                            await artifacts.Complete(RunStatus.Partial, CancellationToken.None);
                        }
                        catch (Exception error)
                        {
                            Warnings.Add($"Could not publish partial output manifest: {error.Message}");
                        }
                        StreamArtifacts();
                    }
                }
            }
        }
        return Artifacts?.PublishedPaths.ToArray() ?? [];
    }

    public CliFailure ClassifyInputFailure(
        Exception error,
        bool export = false,
        bool originalTimeoutMessage = false)
    {
        var partial = export && Artifacts?.PublishedPaths.Count > 0;
        var ioExit = CliExit.IoFailure;
        var unexpectedExit = CliExit.Unexpected;
        if (partial)
        {
            ioExit = CliExit.Partial;
            unexpectedExit = CliExit.Partial;
        }
        if (error is OperationCanceledException)
        {
            if (Cancellation.IsCancellationRequested)
            {
                return new(CliExit.Cancelled, ["Cancelled."]);
            }
            var message = "Schedule provider request timed out.";
            if (originalTimeoutMessage)
            {
                message = error.Message;
            }
            return new(ioExit, [message]);
        }
        if (error is CleanupAfterFailureException paired)
        {
            // The primary failure decides the outcome; the cleanup failures are reported with it.
            var primary = ClassifyInputFailure(
                paired.Failure,
                export: export,
                originalTimeoutMessage: originalTimeoutMessage);
            var cleanup = paired.CleanupExceptions.Select(x => $"Cleaning up after the failure failed: {x.Message}");
            return primary with
            {
                Errors = [.. primary.Errors, .. cleanup],
            };
        }
        var exit = error switch
        {
            CliOutputStreamingException => unexpectedExit,
            AuthenticationRequiredException => CliExit.AuthenticationRequired,
            ArgumentException => CliExit.InvalidArguments,
            LocalOperationBusyException => CliExit.Busy,
            PlatformNotSupportedException => CliExit.Unsupported,
            JsonException
                or DirectoryNotFoundException
                or FileNotFoundException
                or InvalidScheduleSourceException
                or ScheduleBuildException => CliExit.InvalidConfiguration,
            HttpRequestException or IOException or UnauthorizedAccessException => ioExit,
            _ => unexpectedExit,
        };
        return new(exit, [error.Message]);
    }

    public void Dispose()
    {
        if (_started)
        {
            Console.CancelKeyPress -= _cancel;
        }
        _logging?.Dispose();
        Artifacts?.Dispose();
        Cancellation.Dispose();
    }
}
