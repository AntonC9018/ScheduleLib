using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

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
        bool CheckCondition()
        {
            if (!(effects))
            {
                return false;
            }

            if (!(exit != Success))
            {
                return false;
            }

            if (!(exit != Cancelled))
            {
                return false;
            }

            return true;
        }

        if (CheckCondition())
        {
            return Partial;
        }
        return exit;
    }
}

public sealed record CliFailure(int ExitCode, string[] Errors);

/// <summary>Owns one invocation's output, cancellation, warnings and published artifacts.</summary>
public sealed class CliCommandContext : IDisposable
{
    private readonly ConsoleCancelEventHandler _cancel;
    private readonly CliOutput? _previousOutput;
    private int _streamed;
    private bool _started;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public CliCommandContext(
        string command,
        ResultArguments arguments,
        CancellationToken token)
    {
        var runId = Guid.NewGuid().ToString("N");
        Output = new CliOutput(
            command: command,
            runId: runId,
            arguments);
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        _previousOutput = CliAmbient.Current;
        _cancel = (_, e) =>
        {
            e.Cancel = true;
            Cancellation.Cancel();
        };
    }

    public CliOutput Output { get; }

    public CancellationTokenSource Cancellation { get; }

    public List<string> Warnings { get; } = [];

    public RunOutput? Artifacts { get; set; }

    public string Command => Output.Command;
    public string RunId => Output.RunId;

    public bool Start()
    {
        bool conditionResult;
        if (Output.Json)
        {
            conditionResult = Output.Jsonl;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
            return false;
        }
        Output.EmitStart();
        Console.CancelKeyPress += _cancel;
        _started = true;
        return true;
    }

    public static string Status(
        int exit,
        string success = "succeeded",
        bool partial = false,
        bool cancellation = true)
    {
        bool conditionResult;
        if (cancellation)
        {
            conditionResult = exit == CliExit.Cancelled;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            return "cancelled";
        }
        if (exit == CliExit.Success)
        {
            return success;
        }
        bool conditionResult2;
        if (partial)
        {
            conditionResult2 = true;
        }
        else
        {
            conditionResult2 = exit == CliExit.Partial;
        }

        if (conditionResult2)
        {
            return "partial";
        }
        return "failed";
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
        var status = completion.Status;
        var jsonStatus = completion.JsonStatus;
        var human = completion.RenderHuman;
        var warningsFirst = completion.WarningsFirst;
        var shouldWriteWarning = completion.ShouldWriteWarning;
        warnings ??= Warnings.ToArray();
        status ??= Status(exit);
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
            human?.Invoke(status);
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
        SourceArguments source,
        ResolvedSettings settings,
        Action<IServiceCollection> configure)
    {
        var services = CliRuntime.CreateServices(
            source,
            configure,
            settings.ProjectDirectory);
        settings.ConfigureServices(services);
        if (Output.Jsonl)
        {
            CliLogging.ReplaceWithJsonl(services, Output);
            CliAmbient.Current = Output;
        }
        else
        {
            CliAmbient.Current = null;
        }
        return services;
    }

    public void StreamUpdate((string Id, string Status, object? Item, string Human) update)
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
        string status,
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
            global::System.Collections.Generic.List<string> list = Artifacts.PublishedPaths;
            int index = _streamed++;
            var path = list[index];
            Output.WriteHuman(path);
            global::ScheduleLib.Cli.CliOutput cliOutput = Output;
            string text = $"artifact:{path}";
            string id2 = (string) text;
            string status2 = "completed";
            object item = new { path };
            cliOutput.EmitItem(
                id: id2, status: status2, item);
        }
    }

    public async Task<string[]> CompleteArtifacts(int exit)
    {
        if (exit != CliExit.Success
            && Artifacts is { } artifacts
            && artifacts.PublishedPaths.Count > 0
            && !artifacts.PublishedPaths.Contains(artifacts.ManifestPath))
        {
            try
            {
                await artifacts.Complete("partial", CancellationToken.None);
            }
            catch (Exception error)
            {
                global::System.Collections.Generic.List<string> list = Warnings;
                string item = $"Could not publish partial output manifest: {error.Message}";
                list.Add(item);
            }
            StreamArtifacts();
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
        CliAmbient.Current = _previousOutput;
        Artifacts?.Dispose();
        Cancellation.Dispose();
    }
}
