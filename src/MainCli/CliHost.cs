using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;

[assembly: InternalsVisibleTo("MainCli.Tests")]

namespace ScheduleLib.Cli;

public static class CliHost
{
    public static Task<int> Run(string[] args) => RunCore(args, null);

    internal static async Task<int> RunCore(string[] args, CliHostOverrides? overrides)
    {
        // Disposed last, after the root container, so services log to the result's output as they end.
        using var run = new CliRun();
        await using var rootServices = new CliRootServices(run, overrides?.Services);
        var invoked = false;
        var startupRequest = StartupRequest.None;
        string? parseDetail = null;
        var runner = new AppRunner<Commands>(new AppSettings
        {
            Help =
            {
                PrintHelpOption = true,
                UsageAppName = "schedulelib",
            },
            DisableDirectives = true,
        });
        new InvocationScopeResolver(rootServices, run).Register(runner);
        CapturingConsole? capture = null;
        runner.Configure(builder =>
        {
            var appConfigBuilder = builder;
            appConfigBuilder.UseMiddleware(
                (context, next) =>
                {
                    var parseResult = context.ParseResult;
                    startupRequest = StartupRequest.FromParsedInput(parseResult);

                    // Capture parser diagnostics for JSONL or a format conflict. Help
                    // remains human-readable; conflicts produce one dedicated error.
                    if (startupRequest.CapturesDiagnostics)
                    {
                        capture = new();
                        context.Console = capture;
                    }

                    return next(context);
                },
                MiddlewareSteps.ParseInput + 1);
        });
        runner.Configure(builder =>
        {
            builder.UseMiddleware(
                (context, next) =>
                {
                    invoked = true;
                    return next(context);
                },
                MiddlewareSteps.InvokeCommand - 1);
        });
        runner.Configure(builder =>
        {
            builder.UseMiddleware(
                async (context, next) =>
                {
                    var exit = await next(context);
                    if (!invoked)
                    {
                        if (context.ParseResult?.ParseError is { } parseError)
                        {
                            parseDetail = parseError.ToString();
                        }
                    }

                    if (startupRequest.ShouldSuppressFailureHelp(invoked, exit))
                    {
                        context.ShowHelpOnExit = false;
                    }

                    return exit;
                },
                MiddlewareSteps.Help.PrintHelpOnExit + 1);
        });
        var commandExit = await RunCommand();
        return await CliTeardown.EndAfterResult(
            rootServices,
            description: "the CLI's services",
            commandExit,
            run.ResultOutput(startupRequest));

        async Task<int> RunCommand()
        {
            try
            {
                overrides?.Runner?.Invoke(runner);
                var invocationArguments = args.Length == 0 ? ["--help"] : args;
                var result = await runner.RunAsync(invocationArguments);
                if (result == CliExit.Success)
                {
                    return result;
                }

                if (invoked)
                {
                    return result;
                }

                if (startupRequest.RejectsFormatConflict)
                {
                    Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                    return CliExit.InvalidArguments;
                }
                var inputFailure = IsStartupInputFailure(result, parseDetail);
                var exit = inputFailure ? CliExit.InvalidArguments : CliExit.Unexpected;
                // Preserve actionable parser diagnostics on stderr without dumping argv
                // or credentials: CommandDotNet's captured human error plus ParseError.
                var details = new List<string>();
                if (capture is not null)
                {
                    foreach (var line in capture.GetError()
                                 .Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0)
                        {
                            details.Add(trimmed);
                        }
                    }
                    foreach (var line in capture.GetOut()
                                 .Split('\n'))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length > 0)
                        {
                            details.Add(trimmed);
                        }
                    }
                }
                if (parseDetail is { Length: > 0 } parsed)
                {
                    if (!details.Contains(parsed))
                    {
                        details.Add(parsed);
                    }
                }

                var summary = inputFailure
                    ? "Invalid command or arguments. See stderr for details."
                    : "Unexpected CLI startup failure. See stderr for details.";
                if (details.Count == 0)
                {
                    details.Add(summary);
                }

                WriteStartupResult(
                    exit,
                    summary,
                    details);
                return exit;
            }
            catch (OperationCanceledException)
            {
                if (startupRequest.RejectsFormatConflict)
                {
                    Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                    return CliExit.InvalidArguments;
                }
                WriteStartupResult(
                    CliExit.Cancelled,
                    "Cancelled.",
                    [],
                    RunStatus.Cancelled);
                if (!startupRequest.UsesJsonlOutput)
                {
                    Console.Error.WriteLine("Cancelled.");
                }
                return CliExit.Cancelled;
            }
            catch (Exception e)
            {
                if (startupRequest.RejectsFormatConflict)
                {
                    Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                    return CliExit.InvalidArguments;
                }
                WriteStartupResult(
                    CliExit.Unexpected,
                    "Unexpected CLI startup failure. See stderr for details.",
                    [e.Message]);
                if (!startupRequest.UsesJsonlOutput)
                {
                    Console.Error.WriteLine(e.Message);
                }
                return CliExit.Unexpected;
            }
        }

        void WriteStartupResult(
            int exit,
            string error,
            IReadOnlyList<string> details,
            RunStatus status = RunStatus.Failed)
        {
            if (startupRequest.HelpRequested)
            {
                return;
            }

            if (startupRequest.UsesJsonlOutput)
            {
                var output = new CliOutput(
                    "cli",
                    Guid.NewGuid()
                        .ToString("N"),
                    false,
                    true);
                if (run.Adopt(output))
                {
                    output.EmitStart();
                }
                else
                {
                    output = run.ResultOutput(startupRequest);
                }
                foreach (var detail in details)
                {
                    output.WriteError(detail);
                }

                output.EmitFinal(
                    status,
                    exit,
                    [],
                    [error],
                    null);
                return;
            }

            if (startupRequest.Json)
            {
                var value = new CommandResult<object?>(
                    1,
                    "cli",
                    Guid.NewGuid().ToString("N"),
                    status,
                    exit,
                    [],
                    [],
                    [],
                    [error],
                    null);
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        value,
                        new JsonSerializerOptions
                        {
                            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        }));
            }
        }
    }

    private static bool IsStartupInputFailure(int exit, string? parseDetail)
    {
        if (exit == ExitCodes.ValidationError)
        {
            return true;
        }

        if (parseDetail is not null)
        {
            return true;
        }

        return false;
    }

    private sealed class CapturingConsole : CommandDotNet.Rendering.SystemConsole
    {
        private readonly StringBuilder _out = new();
        private readonly StringBuilder _error = new();
        private readonly object _gate = new();

        private sealed class CaptureWriter(StringBuilder builder, object gate) : TextWriter
        {
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                lock (gate)
                {
                    builder.AppendLine(value);
                }
            }

            public override void Write(char value)
            {
                lock (gate)
                {
                    builder.Append(value);
                }
            }

            public override void Write(string? value)
            {
                if (value is null)
                {
                    return;
                }

                lock (gate)
                {
                    builder.Append(value);
                }
            }
        }

        public override TextWriter Out => new CaptureWriter(_out, _gate);
        public override TextWriter Error => new CaptureWriter(_error, _gate);
        public override bool IsOutputRedirected => true;
        public override bool IsErrorRedirected => true;

        public string GetOut()
        {
            lock (_gate)
            {
                return _out.ToString();
            }
        }

        public string GetError()
        {
            lock (_gate)
            {
                return _error.ToString();
            }
        }
    }
}

internal sealed record CliHostOverrides(
    Action<IServiceCollection>? Services = null,
    Action<AppRunner<Commands>>? Runner = null);

public sealed partial class Commands
{
    [Subcommand] public ExportCommands Export { get; set; } = new();

    [Subcommand] public QueryCommands Query { get; set; } = new();
}

public sealed class SourceArguments : IArgumentModel
{
    [Option(
        "data-dir",
        Description =
            "Schedule data root; defaults to packaged code-configured resources. Academic year and semester remain configured in C#.")]
    public string? DataDirectory { get; set; }

    [Option("no-cache", Description = "Bypass schedule cache reads and writes.")]
    public bool NoCache { get; set; }

    [Option("cache-dir", Description = "Shared schedule cache directory; defaults to the ScheduleLib user cache.")]
    public string? CacheDirectory { get; set; }
}

public sealed class ResultArguments : IArgumentModel
{
    [Option("json", Description = "Emit one versioned JSON result on stdout; diagnostics use stderr.")]
    public bool Json { get; set; }

    [Option(
        "jsonl",
        Description =
            "Stream newline-delimited JSON records on stdout (start, items as available, final summary); stderr is JSONL. Mutually exclusive with --json.")]
    public bool Jsonl { get; set; }
}
