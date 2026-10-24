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
        var inputFailure = false;
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
        // When --jsonl is selected (without --json and without explicit --help), capture
        // CommandDotNet's own human parse-error output so neither stdout nor stderr
        // mixes human text into JSONL. Command results use System.Console directly and
        // are unaffected; explicit --help keeps the ordinary text console.
        var hasJson = args.Contains("--json");
        var hasJsonl = args.Contains("--jsonl");
        var hasHelp = args.Contains("--help") || args.Contains("-h");
        CapturingConsole? capture = null;
        bool CheckCondition()
        {
            if (!(hasJsonl))
            {
                return false;
            }

            if (!(!hasJson))
            {
                return false;
            }

            if (!(!hasHelp))
            {
                return false;
            }

            return true;
        }

        if (CheckCondition())
        {
            capture = new();
            runner.Configure(builder =>
            {
                global::CommandDotNet.AppConfigBuilder appConfigBuilder = builder;
                appConfigBuilder.Console = capture;
            });
        }
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
                bool conditionResult;
                if (!invoked)
                {
                    if (context.ParseResult?.ParseError is not null)
                    {
                        conditionResult = true;
                    }
                    else
                    {
                        conditionResult = exit == ExitCodes.ValidationError;
                    }
                }
                else
                {
                    conditionResult = false;
                }

                inputFailure = conditionResult;
                if (!invoked && context.ParseResult?.ParseError is not null)
                {
                    parseDetail = context.ParseResult.ParseError.ToString();
                }

                bool conditionResult2;
                if (exit != CliExit.Success)
                {
                    if (!invoked)
                    {
                        if (args.Contains("--json"))
                        {
                            conditionResult2 = true;
                        }
                        else
                        {
                            conditionResult2 = args.Contains("--jsonl");
                        }
                    }
                    else
                    {
                        conditionResult2 = false;
                    }
                }
                else
                {
                    conditionResult2 = false;
                }

                if (conditionResult2)
                {
                    context.ShowHelpOnExit = false;
                }

                return exit;
            },
            MiddlewareSteps.Help.PrintHelpOnExit + 1));
        try
        {
            configure?.Invoke(runner);
            string[] CalculateArgs()
            {
                string[] args2;
                if (args.Length == 0)
                {
                    args2 = ["--help"];
                }
                else
                {
                    args2 = args;
                }

                return args2;
            }

            var result = await runner.RunAsync(CalculateArgs());
            bool conditionResult;
            if (result == 0)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = invoked;
            }

            if (conditionResult)
            {
                return result;
            }

            hasJson = args.Contains("--json");
            hasJsonl = args.Contains("--jsonl");
            bool conditionResult2;
            if (hasJson)
            {
                conditionResult2 = hasJsonl;
            }
            else
            {
                conditionResult2 = false;
            }

            if (conditionResult2)
            {
                Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                return CliExit.InvalidArguments;
            }
            var exit = inputFailure ? CliExit.InvalidArguments : CliExit.Unexpected;
            // Preserve actionable parser diagnostics on stderr without dumping argv
            // or credentials: CommandDotNet's captured human error plus ParseError.
            var details = new List<string>();
            if (capture is not null)
            {
                foreach (var line in capture.GetError().Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        details.Add(trimmed);
                    }
                }
                foreach (var line in capture.GetOut().Split('\n'))
                {
                    var trimmed = line.Trim();
                    if (trimmed.Length > 0)
                    {
                        details.Add(trimmed);
                    }
                }
            }
            if (parseDetail is { Length: > 0 } parsed && !details.Contains(parsed))
            {
                details.Add(parsed);
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
                details.ToArray());
            return exit;
        }
        catch (Exception e)
        {
            hasJson = args.Contains("--json");
            hasJsonl = args.Contains("--jsonl");
            bool conditionResult3;
            if (hasJson)
            {
                conditionResult3 = hasJsonl;
            }
            else
            {
                conditionResult3 = false;
            }

            if (conditionResult3)
            {
                Console.Error.WriteLine(CliOutput.ConflictingResultFormatsMessage);
                return CliExit.InvalidArguments;
            }
            bool conditionResult4;
            if (hasJsonl)
            {
                conditionResult4 = hasJson;
                conditionResult4 = !(conditionResult4);
            }
            else
            {
                conditionResult4 = false;
            }

            if (conditionResult4)
            {
                string command = "cli";
                string runId = Guid.NewGuid().ToString("N");
                bool json = false;
                bool jsonl = true;
                var output = new CliOutput(
                    command, runId, json, jsonl);
                output.EmitStart();
                output.WriteError(e.Message);
                output.EmitFinal(
                    "failed",
                    1,
                    [],
                    ["Unexpected CLI startup failure. See stderr for details."],
                    null);
                return CliExit.Unexpected;
            }
            Console.Error.WriteLine(e.Message);
            WriteStartupResult(
                1,
                "Unexpected CLI startup failure. See stderr for details.",
                [e.Message]);
            return CliExit.Unexpected;
        }

        void WriteStartupResult(
            int exit,
            string error,
            string[] details)
        {
            hasJson = args.Contains("--json");
            hasJsonl = args.Contains("--jsonl");
            bool conditionResult;
            if (hasJson)
            {
                conditionResult = hasJsonl;
            }
            else
            {
                conditionResult = false;
            }

            if (conditionResult)
            {
                return;
            }

            if (hasJsonl)
            {
                string command = "cli";
                string runId = Guid.NewGuid().ToString("N");
                bool json = false;
                bool jsonl = true;
                var output = new CliOutput(
                    command, runId, json, jsonl);
                output.EmitStart();
                foreach (var detail in details)
                {
                    output.WriteError(detail);
                }

                output.EmitFinal(
                    "failed",
                    exit,
                    [],
                    [error],
                    null);
            }
            else if (hasJson)
            {
                global::ScheduleLib.Cli.CommandResult<object?> CalculateValue()
                {
                    int schemaVersion = 1;
                    string command2 = "cli";
                    string runId2 = Guid.NewGuid().ToString("N");
                    string status = "failed";
                    int exitCode = exit;
                    string[] outputs = [];
                    string[] actions = [];
                    string[] warnings = [];
                    string[] errors = [error];
                    object? data = null;
                    global::ScheduleLib.Cli.CommandResult<object?> value = new CommandResult<object?>(
                    schemaVersion, command2, runId2, status, exitCode, outputs, actions, warnings, errors, data);
                    return value;
                }

                Console.WriteLine(
                                    JsonSerializer.Serialize(
                                        CalculateValue(),
                                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            }
        }
    }

    private sealed class CapturingConsole : CommandDotNet.Rendering.SystemConsole
    {
        private readonly StringBuilder _out = new();
        private readonly StringBuilder _error = new();
        private readonly object _gate = new();

        private sealed class CaptureWriter(StringBuilder builder, object gate) : TextWriter
        {
            public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

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
    [Subcommand]
    public ExportCommands Export { get; set; } = new();

    [Subcommand]
    public QueryCommands Query { get; set; } = new();
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
            bool conditionResult;
            if (query.BeforeSlot is < 1 or > 7)
            {
                conditionResult = true;
            }
            else
            {
                if (query.Day is { } day)
                {
                    conditionResult = Enum.IsDefined(day);
                    conditionResult = !(conditionResult);
                }
                else
                {
                    conditionResult = false;
                }

                if (conditionResult)
                {
                    conditionResult = true;
                }
                else
                {
                    if (query.Parity is { } parity)
                    {
                        conditionResult = Enum.IsDefined(parity);
                        conditionResult = !(conditionResult);
                    }
                    else
                    {
                        conditionResult = false;
                    }
                }
            }

            if (conditionResult)
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
            if (query.Room is { } room && !schedule.EnumerateWeeklyLessons().Any(x => x.Lesson.Room.Id == room))
            {
                return Finish(
                    CliExit.InvalidArguments,
                    [],
                    [$"Unknown room: {room}"]);
            }

            var time = scope.ServiceProvider.GetRequiredService<LessonTimeConfig>();
            foreach (var lesson in schedule.Filter(FilterHelper.Builder().WithLatestPeriod(schedule))
                         .EnumerateWeeklyLessons())
            {
                cancellation.Token.ThrowIfCancellationRequested();
                bool conditionResult2;
                if (query.Room is { } r)
                {
                    conditionResult2 = lesson.Lesson.Room.Id != r;
                }
                else
                {
                    conditionResult2 = false;
                }

                if (conditionResult2)
                {
                    conditionResult2 = true;
                }
                else
                {
                    if (query.Day is { } d)
                    {
                        conditionResult2 = lesson.Date.DayOfWeek != d;
                    }
                    else
                    {
                        conditionResult2 = false;
                    }

                    if (conditionResult2)
                    {
                        conditionResult2 = true;
                    }
                    else
                    {
                        if (query.Parity is { } p)
                        {
                            conditionResult2 = lesson.Date.Parity.IsMatch(p);
                            conditionResult2 = !(conditionResult2);
                        }
                        else
                        {
                            conditionResult2 = false;
                        }

                        if (conditionResult2)
                        {
                            conditionResult2 = true;
                        }
                        else
                        {
                            if (query.BeforeSlot is { } slot)
                            {
                                conditionResult2 = lesson.Date.TimeSlot.Index + 1 > slot;
                            }
                            else
                            {
                                conditionResult2 = false;
                            }
                        }
                    }
                }

                if (conditionResult2)
                {
                    continue;
                }

                var interval = time.GetTimeSlotInterval(lesson.Date.TimeSlot);
                string CalculateStart()
                {
                    string start = interval.Start.ToString("HH:mm");
                    return start;
                }

                var item = new LessonResult(
                                    lesson.Date.DayOfWeek.ToString(),
                                    lesson.Date.Parity.ToString(),
                                    lesson.Date.TimeSlot.Index + 1,
                                    CalculateStart(),
                                    interval.End.ToString("HH:mm"),
                                    lesson.Lesson.Room.Id,
                                    schedule.Get(lesson.Lesson.Course).FullName,
                                    lesson.Lesson.Teachers.Select(x => schedule.Get(x).PersonName.ToString()).ToArray());
                lessons.Add(item);
                global::ScheduleLib.Cli.CliCommandContext cliCommandContext = context;
                string id = streamed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string status2 = "completed";
                object item2 = item;
                global::System.Action renderHuman2 = () =>
                    {
                        output.WriteHuman(
                            $"{item.Day} {item.Start}-{item.End} ({item.Parity}) room {item.Room}: {item.Course}; {string.Join(", ", item.Teachers)}");
                    };
                cliCommandContext.StreamItem(
                    id, status2, item2, renderHuman: renderHuman2);
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
            if (error is ArgumentException or ScheduleLib.Application.Config.AuthenticationRequiredException)
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
                    global::ScheduleLib.Cli.CliOutput cliOutput = output;
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
