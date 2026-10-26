using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;

namespace ScheduleLib.Cli;

public sealed class OutputArguments : IArgumentModel
{
    [Option(
        "output",
        Description =
            "Destination directory. Defaults to a unique run under output. Only unchanged manifest-owned files may be replaced.")]
    public string? Directory { get; set; }
}

public sealed record TeachersExcelResult(string? OutputDirectory, int LessonCount, int TeacherCount);

[Command("export")]
public partial class ExportCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) =>
        AppConfiguration.ConfigureServices(services);

    [Command(
        "teachers-excel",
        Description =
            "Generate the all-teacher workbook for the latest period. No teacher profile is required; a profile does not filter teachers.")]
    public async Task<int> TeachersExcel(
        SourceArguments source,
        OutputArguments destination,
        ResultArguments resultArgs,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        using var context = new CliCommandContext(
            "export teachers-excel",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        RunOutput? output = null;
        var warnings = context.Warnings;
        var lessonCount = 0;
        var teacherCount = 0;
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput)
            {
                CliRuntime.ResolvePath(selectedOutput);
            }

            using var resolvedSettings = await CliSettings.Load(
                settings ?? new(),
                cancellationToken: cancellation.Token);
            var services = context.CreateServices(
                source,
                resolvedSettings,
                ConfigureServices);
            output = context.Artifacts = await RunOutput.Create(
                output: destination.Directory,
                command: context.Command,
                runId: context.RunId,
                cancellation.Token,
                projectDirectory: resolvedSettings.ProjectDirectory);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.LatestPeriodSchedule();
            lessonCount = schedule.EnumerateWeeklyLessons()
                .Count();
            teacherCount = schedule.Teachers.Count();
            var all = scope.ServiceProvider.GetRequiredService<Schedule>();
            var omitted = all.EnumerateWeeklyLessons()
                    .Count()
                - lessonCount;
            if (omitted > 0)
            {
                var list = warnings;
                string item = $"Latest-period selection omits {omitted} weekly lessons from other periods.";
                list.Add(item);
            }

            if (teacherCount == 0)
            {
                warnings.Add("The latest period contains no teachers.");
            }

            var teacherless = schedule.EnumerateWeeklyLessons()
                .Count(x => !x.Lesson.Teachers.Any());
            if (teacherless > 0)
            {
                var list2 = warnings;
                string item2 = $"The teacher workbook omits {teacherless} weekly lessons with no teacher.";
                list2.Add(item2);
            }

            var sundays = schedule.EnumerateWeeklyLessons()
                .Count(x => x.Date.DayOfWeek == DayOfWeek.Sunday);
            if (sundays > 0)
            {
                var list3 = warnings;
                string item3 = $"The existing Monday-Saturday layout omits {sundays} Sunday lessons.";
                list3.Add(item3);
            }

            var seminar = scope.ServiceProvider.GetRequiredService<RegularSeminarDateProvider>()
                .Get();
            var seminarLessons = schedule.EnumerateWeeklyLessons()
                .Count(x =>
                    x.Date.DayOfWeek == seminar.Day && x.Date.TimeSlot == seminar.TimeSlot);
            if (seminarLessons > 0)
            {
                var list4 = warnings;
                string item4 =
                    $"The existing seminar display replaces {seminarLessons} lessons at the configured seminar slot.";
                list4.Add(
                    item4);
            }

            var handler = scope.ServiceProvider.GetRequiredService<GenerateAllTeachersExcelTaskHandler>();
            await output.Publish(
                "all-teachers.xlsx",
                async (stream, token) =>
                {
                    await handler.Run(new()
                    {
                        Schedule = schedule,
                        CancellationToken = token,
                        OutputDirectory = stream,
                    });
                    token.ThrowIfCancellationRequested();
                    stream.Position = 0;
                    using var workbook = SpreadsheetDocument.Open(stream, false);
                    if (workbook.WorkbookPart?.Workbook.Sheets?.ChildElements.Count != 1
                        || !workbook.WorkbookPart.WorksheetParts.Any())
                    {
                        throw new InvalidDataException("Generated teacher workbook has no usable worksheet.");
                    }
                },
                cancellation.Token);
            StreamNew();
            await output.Complete("succeeded", cancellation.Token);
            StreamNew();
            return await Finish(CliExit.Success, []);
        }
        catch (Exception error)
        {
            var failure = context.ClassifyInputFailure(
                error,
                export: true,
                originalTimeoutMessage: false);
            return await Finish(failure.ExitCode, failure.Errors);
        }

        void StreamNew() => context.StreamArtifacts();

        async Task<int> Finish(int exit, string[] errors)
        {
            var paths = await context.CompleteArtifacts(exit);
            var status = CliCommandContext.Status(exit, partial: paths.Length > 0);
            var jsonStatus = CliCommandContext.Status(
                exit,
                partial: paths.Length > 0,
                cancellation: false);

            TeachersExcelResult CalculateData()
            {
                TeachersExcelResult data = new(
                    output?.DirectoryPath,
                    LessonCount: lessonCount,
                    TeacherCount: teacherCount);
                return data;
            }

            return context.Finish(new CliCompletion<TeachersExcelResult>(
                exit,
                errors,
                CalculateData())
            {
                Summary = new
                {
                    outputDirectory = output?.DirectoryPath,
                    lessonCount,
                    teacherCount,
                    artifactCount = paths.Length,
                },
                Paths = paths,
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = status =>
                {
                    var cliOutput = cli;
                    string line =
                        $"{status}: {lessonCount} lessons, {teacherCount} teachers, {paths.Length} artifacts in {output?.DirectoryPath}.";
                    cliOutput.WriteHuman(
                        line);
                },
            });
        }
    }
}
