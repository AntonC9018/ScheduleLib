using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record ScheduleExportResult(string? OutputDirectory, int LessonCount, int TeacherCount);

public partial class ExportCommands
{
    [Command(
        "pdf",
        Description =
            "Generate all group, partition and teacher PDFs for the latest period. No teacher profile is required.")]
    public Task<int> Pdf(
        SourceArguments source,
        OutputArguments destination,
        ResultArguments result,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        ExportRequest request = new(
            source,
            destination,
            result,
            settings ?? new());
        var kind = ScheduleExportKind.Pdf;
        var cancellationToken2 = cancellationToken;
        var exportSchedule = ExportSchedule(
            request,
            kind,
            cancellationToken2);
        return exportSchedule;
    }

    [Command(
        "ics",
        Description =
            "Generate all group, partition and teacher ICS calendars for the latest period; calendars with missing semester dates are reported and skipped. No teacher profile is required.")]
    public Task<int> Ics(
        SourceArguments source,
        OutputArguments destination,
        ResultArguments result,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        ExportRequest request = new(
            source,
            destination,
            result,
            settings ?? new());
        var kind = ScheduleExportKind.Ics;
        var cancellationToken2 = cancellationToken;
        var exportSchedule = ExportSchedule(
            request,
            kind,
            cancellationToken2);
        return exportSchedule;
    }

    private async Task<int> ExportSchedule(
        ExportRequest request,
        ScheduleExportKind kind,
        CancellationToken cancellationToken)
    {
        var source = request.Source!;
        var destination = request.Destination;
        var resultArgs = request.Result;
        var settings = request.Settings;
        var calendar = kind == ScheduleExportKind.Ics;

        string CalculateCommand()
        {
            string command2;
            if (calendar)
            {
                command2 = "export ics";
            }
            else
            {
                command2 = "export pdf";
            }

            return command2;
        }

        using var context = new CliCommandContext(
            CalculateCommand(),
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

            async Task PublishStreaming(
                string name,
                Func<Stream, CancellationToken, Task> generate,
                CancellationToken token)
            {
                await output.Publish(
                    name,
                    generate,
                    token);
                StreamNew();
            }

            if (calendar)
            {
                await scope.ServiceProvider.GetRequiredService<GenerateIcsCalendarsTaskHandler>()
                    .RunAsync(new()
                    {
                        CancellationToken = cancellation.Token,
                        PublishArtifact = PublishStreaming,
                        ReportOmission = warnings.Add,
                    });
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>()
                    .Run(new()
                    {
                        CancellationToken = cancellation.Token,
                        PublishArtifact = PublishStreaming,
                    });
            }

            StreamNew();
            if (output.PublishedPaths.Count == 0)
            {
                warnings.Add("No artifacts were generated for the latest period.");
            }

            await output.Complete(RunStatus.Succeeded, cancellation.Token);
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

            ScheduleExportResult CalculateData()
            {
                ScheduleExportResult data = new(
                    output?.DirectoryPath,
                    LessonCount: lessonCount,
                    TeacherCount: teacherCount);
                return data;
            }

            return context.Finish(new CliCompletion<ScheduleExportResult>(
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
