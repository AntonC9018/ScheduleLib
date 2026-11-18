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
        await using var rootServices = new CliRootServices(overrides?.Services);
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
        new InvocationScopeResolver(rootServices).Register(runner);
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
        catch (Exception e)
        {
            if (startupRequest.RejectsFormatConflict)
            {
                Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                return CliExit.InvalidArguments;
            }
            WriteStartupResult(
                1,
                "Unexpected CLI startup failure. See stderr for details.",
                [e.Message]);
            if (!startupRequest.UsesJsonlOutput)
            {
                Console.Error.WriteLine(e.Message);
            }
            return CliExit.Unexpected;
        }

        void WriteStartupResult(
            int exit,
            string error,
            IReadOnlyList<string> details)
        {
            if (startupRequest.HelpRequested)
            {
                return;
            }

            if (startupRequest.Jsonl)
            {
                var output = new CliOutput(
                    "cli",
                    Guid.NewGuid()
                        .ToString("N"),
                    false,
                    true);
                output.EmitStart();
                foreach (var detail in details)
                {
                    output.WriteError(detail);
                }

                output.EmitFinal(
                    RunStatus.Failed,
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
                    RunStatus.Failed,
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

    private readonly record struct StartupRequest(bool Json, bool Jsonl, bool HelpRequested)
    {
        public static StartupRequest None => default;

        public bool HasFormat => Json || Jsonl;

        public bool ConflictingFormats => Json && Jsonl;

        public bool CapturesDiagnostics
        {
            get
            {
                if (HelpRequested)
                {
                    return false;
                }

                if (ConflictingFormats)
                {
                    return true;
                }

                if (Jsonl)
                {
                    return true;
                }

                return false;
            }
        }

        public bool UsesJsonlOutput
        {
            get
            {
                if (HelpRequested)
                {
                    return false;
                }

                if (Json)
                {
                    return false;
                }

                if (Jsonl)
                {
                    return true;
                }

                return false;
            }
        }

        public bool RejectsFormatConflict
        {
            get
            {
                if (HelpRequested)
                {
                    return false;
                }

                if (ConflictingFormats)
                {
                    return true;
                }

                return false;
            }
        }

        public bool ShouldSuppressFailureHelp(bool commandInvoked, int exit)
        {
            if (commandInvoked)
            {
                return false;
            }

            if (exit == CliExit.Success)
            {
                return false;
            }

            if (HelpRequested)
            {
                return false;
            }

            if (HasFormat)
            {
                return true;
            }

            return false;
        }

        public static StartupRequest FromParsedInput(CommandDotNet.Parsing.ParseResult? parseResult)
        {
            var json = parseResult?.TargetCommand.HasInputValues("json") == true;
            var jsonl = parseResult?.TargetCommand.HasInputValues("jsonl") == true;
            var helpRequested = parseResult?.HelpWasRequested() == true;
            return new(
                Json: json,
                Jsonl: jsonl,
                HelpRequested: helpRequested);
        }
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

/// <summary>What a test changes about a CLI run.</summary>
/// <param name="Services">Replaces registrations of the root container after the CLI's own.</param>
/// <param name="Runner">Configures the runner, for example with extra middleware.</param>
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
