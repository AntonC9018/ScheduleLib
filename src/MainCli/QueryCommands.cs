using System.Globalization;
using CommandDotNet;
using ScheduleLib.DependencyInjection.ScopePlans;

namespace ScheduleLib.Cli;

[Command("query")]
public sealed class QueryCommands
{
    [Subcommand] public QueryLessonsCommand? Lessons { get; set; }

    [Subcommand] public QueryFreeHoursCommand? FreeHours { get; set; }
}

[Command(
    "lessons",
    Description =
        "List latest-period weekly lessons. All predicates are optional; parity includes lessons held every week. No teacher profile is required.")]
public sealed class QueryLessonsCommand(ScopeForkFactory<QueryLessonsWorkflow> workflows)
{
    [DefaultCommand]
    public async Task<int> Run(
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
        var lessons = new List<LessonResult>();
        var streamed = 0;
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            var lessonQuery = LessonQuery.From(query);
            if (!lessonQuery.IsValid)
            {
                return Finish(
                    CliExit.InvalidArguments,
                    [],
                    ["Invalid day, parity, or slot (slots start at 1)."]);
            }

            var scheduleSource = CliRuntime.ResolveSource(source);
            var settingsSelection = SettingsSelection.From(settings);
            await using var scope = await workflows.OpenAsync(
                inputs => inputs.Add(settingsSelection)
                    .Add(scheduleSource)
                    .Add(lessonQuery),
                cancellation.Token);
            foreach (var item in scope.Handler.Run(cancellation.Token))
            {
                var id = streamed.ToString(CultureInfo.InvariantCulture);
                lessons.Add(item);
                context.StreamItem(
                    id,
                    status: ActionState.Completed,
                    item,
                    renderHuman: () => output.WriteHuman(
                        $"{item.Day} {item.Start}-{item.End} ({item.Parity}) room {item.Room}: {item.Course}; {string.Join(", ", item.Teachers)}"));
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
                    string line = $"{status}: {streamed} lessons.";
                    output.WriteHuman(line);
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

public sealed record LessonResult(
    string Day,
    string Parity,
    int Slot,
    string Start,
    string End,
    string? Room,
    string Course,
    string[] Teachers);
