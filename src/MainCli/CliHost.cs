using System.Text.Json;
using CommandDotNet;
using CommandDotNet.Execution;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public static class CliHost
{
    public static async Task<int> Run(string[] args)
    {
        var invoked = false;
        var runner = new AppRunner<Commands>(new AppSettings { Help = { PrintHelpOption = true, UsageAppName = "schedulelib" }, DisableDirectives = true });
        runner.Configure(builder => builder.UseMiddleware((context, next) =>
        {
            invoked = true;
            return next(context);
        }, MiddlewareSteps.InvokeCommand - 1));
        runner.Configure(builder => builder.UseMiddleware(async (context, next) =>
        {
            var exit = await next(context);
            if (exit == 1 && !invoked && args.Contains("--json")) context.ShowHelpOnExit = false;
            return exit;
        }, MiddlewareSteps.Help.PrintHelpOnExit + 1));
        var result = await runner.RunAsync(args.Length == 0 ? ["--help"] : args);
        if (result == 1 && !invoked && args.Contains("--json"))
            Console.WriteLine(JsonSerializer.Serialize(new CommandResult<object?>(1, "cli", Guid.NewGuid().ToString("N"), "failed", 2, [], [], [], ["Invalid command or arguments. See stderr for details."], null), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        // CommandDotNet uses 1 for parse/binding errors; execution uses the public exit codes.
        return result == 1 && !invoked ? 2 : result;
    }
}

public sealed partial class Commands
{
    [Subcommand]
    public QueryCommands Query { get; set; } = new();
}

[Command("query")]
public partial class QueryCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) => AppConfiguration.ConfigureServices(services);

    [Command("lessons", Description = "List latest-period weekly lessons. All predicates are optional; parity includes lessons held every week. No teacher profile is required.")]
    public async Task<int> Lessons(QueryArguments query, SourceArguments source, ResultArguments output, CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (query.BeforeSlot is < 1 or > 7 || query.Day is { } day && !Enum.IsDefined(day)
                || query.Parity is { } parity && !Enum.IsDefined(parity))
                return Finish(2, [], ["Invalid day, parity, or slot (slots start at 1)."]);
            var dataDirectory = source.DataDirectory is { } path
                ? Path.GetFullPath(path)
                : Path.Combine(AppContext.BaseDirectory, "data");
            if (!Directory.Exists(dataDirectory))
                return Finish(3, [], [$"Schedule source directory does not exist: {dataDirectory}"]);
            var services = new ServiceCollection();
            ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                x.DataDirectory = dataDirectory;
                x.UseCache = false;
            });
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            if (query.Room is { } room && !schedule.EnumerateWeeklyLessons().Any(x => x.Lesson.Room.Id == room))
                return Finish(2, [], [$"Unknown room: {room}"]);
            var time = scope.ServiceProvider.GetRequiredService<LessonTimeConfig>();
            var lessons = new List<LessonResult>();
            foreach (var lesson in schedule.Filter(FilterHelper.Builder().WithLatestPeriod(schedule)).EnumerateWeeklyLessons())
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (query.Room is { } r && lesson.Lesson.Room.Id != r
                    || query.Day is { } d && lesson.Date.DayOfWeek != d
                    || query.Parity is { } p && !lesson.Date.Parity.IsMatch(p)
                    || query.BeforeSlot is { } slot && lesson.Date.TimeSlot.Index + 1 > slot)
                    continue;
                var interval = time.GetTimeSlotInterval(lesson.Date.TimeSlot);
                lessons.Add(new(lesson.Date.DayOfWeek.ToString(), lesson.Date.Parity.ToString(),
                    lesson.Date.TimeSlot.Index + 1, interval.Start.ToString("HH:mm"), interval.End.ToString("HH:mm"),
                    lesson.Lesson.Room.Id, schedule.Get(lesson.Lesson.Course).FullName,
                    lesson.Lesson.Teachers.Select(x => schedule.Get(x).PersonName.ToString()).ToArray()));
            }
            return Finish(0, lessons, []);
        }
        catch (OperationCanceledException) { return Finish(130, [], ["Cancelled."]); }
        catch (PlatformNotSupportedException e) { return Finish(8, [], [e.Message]); }
        catch (ScheduleBuildException e) { return Finish(3, [], [e.Message]); }
        catch (FileNotFoundException e) { return Finish(3, [], [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, [], [e.Message]); }
        catch (IOException e) { return Finish(5, [], [e.Message]); }
        catch (Exception e) { return Finish(1, [], [e.Message]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, IReadOnlyList<LessonResult> lessons, string[] errors)
        {
            foreach (var error in errors) Console.Error.WriteLine(error);
            var publicExit = exit;
            if (output.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<IReadOnlyList<LessonResult>>(1, "query lessons", runId, publicExit == 0 ? "succeeded" : "failed", publicExit, [], [], [], errors, lessons), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var lesson in lessons)
                    Console.WriteLine($"{lesson.Day} {lesson.Start}-{lesson.End} ({lesson.Parity}) room {lesson.Room}: {lesson.Course}; {string.Join(", ", lesson.Teachers)}");
            return exit;
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
    [Option("data-dir", Description = "Schedule data root; defaults to packaged code-configured resources. Academic year and semester remain configured in C#.")]
    public string? DataDirectory { get; set; }
    [Option("no-cache", Description = "Bypass cache (this first CLI slice always bypasses it).")]
    public bool NoCache { get; set; }
}

public sealed class ResultArguments : IArgumentModel
{
    [Option("json", Description = "Emit one versioned JSON result on stdout; diagnostics use stderr.")]
    public bool Json { get; set; }
}

public sealed record LessonResult(string Day, string Parity, int Slot, string Start, string End,
    string? Room, string Course, string[] Teachers);
