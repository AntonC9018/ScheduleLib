using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

[assembly: InternalsVisibleTo("MainCli.Tests")]

namespace ScheduleLib.Cli;

public static class CliHost
{
    public static Task<int> Run(string[] args) => RunCore(args, null);

    internal static async Task<int> RunCore(string[] args, Action<AppRunner<Commands>>? configure)
    {
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
        runner.Configure(builder => builder.UseMiddleware(
            (context, next) =>
            {
                invoked = true;
                return next(context);
            },
            MiddlewareSteps.InvokeCommand - 1));
        runner.Configure(builder => builder.UseMiddleware(
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
            MiddlewareSteps.Help.PrintHelpOnExit + 1));
        try
        {
            configure?.Invoke(runner);
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

            WriteStartupResult(exit, summary, details);
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
                var output = new CliOutput("cli", Guid.NewGuid().ToString("N"), false, true);
                output.EmitStart();
                foreach (var detail in details)
                {
                    output.WriteError(detail);
                }

                output.EmitFinal("failed", exit, [], [error], null);
                return;
            }

            if (startupRequest.Json)
            {
                var value = new CommandResult<object?>(
                    1,
                    "cli",
                    Guid.NewGuid().ToString("N"),
                    "failed",
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
            return new(Json: json, Jsonl: jsonl, HelpRequested: helpRequested);
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

public sealed partial class Commands
{
    [Subcommand] public ExportCommands Export { get; set; } = new();

    [Subcommand] public QueryCommands Query { get; set; } = new();
}

[Command("query")]
public partial class QueryCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) =>
        AppConfiguration.ConfigureServices(services);

    [Command(
        "lessons",
        Description =
            "List latest-period weekly lessons. All predicates are optional; parity includes lessons held every week. No teacher profile is required.")]
    public async Task<int> Lessons(
        QueryArguments query,
        SourceArguments source,
        ResultArguments outputArgs,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        using var context = new CliCommandContext(
            "query lessons",
            outputArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        if (output.Jsonl)
        {
            CliAmbient.Current = output;
        }

        var lessons = new List<LessonResult>();
        var streamed = 0;
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            bool IsValidQuery()
            {
                if (query.BeforeSlot is < 1 or > 7)
                {
                    return false;
                }

                if (query.Day is { } day)
                {
                    if (!Enum.IsDefined(day))
                    {
                        return false;
                    }
                }

                if (query.Parity is { } parity)
                {
                    if (!Enum.IsDefined(parity))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (!IsValidQuery())
            {
                return Finish(
                    CliExit.InvalidArguments,
                    [],
                    ["Invalid day, parity, or slot (slots start at 1)."]);
            }

            using var resolvedSettings = await CliSettings.Load(
                settings ?? new(),
                cancellationToken: cancellation.Token);
            var services = context.CreateServices(
                source,
                resolvedSettings,
                ConfigureServices);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            if (query.Room is { } room
                && !schedule.EnumerateWeeklyLessons()
                    .Any(x => x.Lesson.Room.Id == room))
            {
                return Finish(
                    CliExit.InvalidArguments,
                    [],
                    [$"Unknown room: {room}"]);
            }

            var time = scope.ServiceProvider.GetRequiredService<LessonTimeConfig>();
            foreach (var lesson in schedule.Filter(FilterHelper.Builder()
                             .WithLatestPeriod(schedule))
                         .EnumerateWeeklyLessons())
            {
                cancellation.Token.ThrowIfCancellationRequested();

                if (query.Room is { } roomFilter)
                {
                    if (lesson.Lesson.Room.Id != roomFilter)
                    {
                        continue;
                    }
                }

                if (query.Day is { } dayFilter)
                {
                    if (lesson.Date.DayOfWeek != dayFilter)
                    {
                        continue;
                    }
                }

                if (query.Parity is { } parityFilter)
                {
                    if (!lesson.Date.Parity.IsMatch(parityFilter))
                    {
                        continue;
                    }
                }

                if (query.BeforeSlot is { } lastSlot)
                {
                    var slotNumber = lesson.Date.TimeSlot.Index + 1;
                    if (slotNumber > lastSlot)
                    {
                        continue;
                    }
                }

                var interval = time.GetTimeSlotInterval(lesson.Date.TimeSlot);

                var start = interval.Start.ToString("HH:mm");

                var item = new LessonResult(
                    lesson.Date.DayOfWeek.ToString(),
                    lesson.Date.Parity.ToString(),
                    lesson.Date.TimeSlot.Index + 1,
                    start,
                    interval.End.ToString("HH:mm"),
                    lesson.Lesson.Room.Id,
                    schedule.Get(lesson.Lesson.Course).FullName,
                    lesson.Lesson.Teachers.Select(x => schedule.Get(x).PersonName
                            .ToString())
                        .ToArray());
                lessons.Add(item);
                var id = streamed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Action renderHuman = () =>
                {
                    output.WriteHuman(
                        $"{item.Day} {item.Start}-{item.End} ({item.Parity}) room {item.Room}: {item.Course}; {string.Join(", ", item.Teachers)}");
                };
                context.StreamItem(
                    id,
                    status: "completed",
                    item,
                    renderHuman: renderHuman);
                streamed++;
            }
            return Finish(
                CliExit.Success,
                lessons,
                []);
        }
        catch (Exception error)
        {
            var failure = context.ClassifyInputFailure(error);
            IReadOnlyList<LessonResult> reportedLessons = lessons;
            if (error is ArgumentException or Application.Config.AuthenticationRequiredException)
            {
                reportedLessons = [];
            }
            return Finish(
                failure.ExitCode,
                reportedLessons,
                failure.Errors);
        }

        int Finish(
            int exit,
            IReadOnlyList<LessonResult> lessonsForJson,
            string[] errors)
        {
            var status = CliCommandContext.Status(exit);
            var jsonStatus = CliCommandContext.Status(exit, cancellation: false);
            return context.Finish(new CliCompletion<IReadOnlyList<LessonResult>>(
                exit,
                errors,
                lessonsForJson)
            {
                Summary = new { count = streamed },
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = status =>
                {
                    var cliOutput = output;
                    string line = $"{status}: {streamed} lessons.";
                    cliOutput.WriteHuman(line);
                },
            });
        }
    }
}

public sealed class QueryArguments : IArgumentModel
{
    [Option("room", Description = "Exact room identifier, for example 423/4.")]
    public string? Room { get; set; }

    [Option("day", Description = "Day name, for example Tuesday.")]
    public DayOfWeek? Day { get; set; }

    [Option("parity", Description = "OddWeek, EvenWeek or EveryWeek.")]
    public Parity? Parity { get; set; }

    [Option("before-slot", Description = "Inclusive time-slot cutoff, numbered from 1.")]
    public int? BeforeSlot { get; set; }
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

public sealed record LessonResult(
    string Day,
    string Parity,
    int Slot,
    string Start,
    string End,
    string? Room,
    string Course,
    string[] Teachers);
