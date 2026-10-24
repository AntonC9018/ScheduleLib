using Anton.LayeredData.Retrieval;
using ClosedXML.Excel;
using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Theses.Parsing;

namespace ScheduleLib.Cli;

public sealed record TeacherExportResult(string? OutputDirectory, string? Teacher, int WorkbookCount);

public partial class ExportCommands
{
    [Command(
        "lab-deadlines",
        Description =
            "Generate the selected teacher's laboratory deadlines using coded academic dates and resolved teacher settings, across all periods. Requires --profile. Existing shared/mixed-partition laboratory combinations are unsupported.")]
    public Task<int> LabDeadlines(
        SourceArguments source,
        OutputArguments destination,
        ResultArguments resultArgs,
        SettingsArguments settings,
        CancellationToken cancellationToken = default)
    {
        return TeacherExport(
                new(
                    source,
                    destination,
                    resultArgs,
                    settings),
                TeacherExportKind.LabDeadlines,
                cancellationToken);
    }

    [Command(
        "pre-defense",
        Description =
            "Generate year-thesis (An) commission workbooks from the existing coded commission configuration and thesis source. Missing commissions are a prerequisite error. A selected profile does not filter commissions; this command does not discover commissions or publish remotely.")]
    public Task<int> PreDefense(
        OutputArguments destination,
        ResultArguments resultArgs,
        SettingsArguments settings,
        CancellationToken cancellationToken = default)
    {
        return TeacherExport(
                new(
                    null,
                    destination,
                    resultArgs,
                    settings),
                TeacherExportKind.PreDefense,
                cancellationToken);
    }

    private async Task<int> TeacherExport(
        ExportRequest request,
        TeacherExportKind kind,
        CancellationToken cancellationToken)
    {
        var source = request.Source;
        var destination = request.Destination;
        var resultArgs = request.Result;
        var settings = request.Settings;
        var preDefense = kind == TeacherExportKind.PreDefense;
        using var context = new CliCommandContext(
            preDefense ? "export pre-defense" : "export lab-deadlines",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        RunOutput? output = null;
        var warnings = context.Warnings;
        var workbookCount = 0;
        string? teacher = null;
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput)
            {
                CliRuntime.ResolvePath(selectedOutput);
            }

            using var resolvedSettings = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            teacher = resolvedSettings.Profile;
            if (!preDefense && teacher is null)
            {
                return await Finish(
                    CliExit.InvalidConfiguration,
                    ["A teacher --profile is required. Use config profiles to list identities."]);
            }

            var services = preDefense
                ? new ServiceCollection()
                : CliRuntime.CreateServices(
                    source!,
                    ConfigureServices,
                    resolvedSettings.ProjectDirectory);
            if (preDefense)
            {
                ConfigureServices(services);
            }

            resolvedSettings.ConfigureServices(services);
            if (cli.Jsonl)
            {
                CliLogging.ReplaceWithJsonl(services, cli);
            }

            CliAmbient.Current = cli.Jsonl ? cli : null;
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await using var scope = provider.CreateAsyncScope();
            if (!preDefense)
            {
                var deadlinesConfig = scope.ServiceProvider
                    .GetRequiredService<DataProvider<DeadlinesExcelBuiltConfig>>().Get();
                if (deadlinesConfig is null)
                {
                    return await Finish(
                        CliExit.InvalidConfiguration,
                        ["Laboratory deadlines settings are missing for the selected teacher."]);
                }
                // Task rows start at row 2; reserve the first worksheet row for lesson dates.
                if (deadlinesConfig.MaxTaskRows < 1 || deadlinesConfig.MaxTaskRows > XLHelper.MaxRowNumber - 1)
                {
                    return await Finish(
                        CliExit.InvalidConfiguration,
                        [
                            $"Laboratory deadlines maxTaskRows for selected teacher '{teacher}' must be between 1 and {XLHelper.MaxRowNumber - 1}; resolved value is {deadlinesConfig.MaxTaskRows}.",
                        ]);
                }
            }
            if (preDefense
                && !ListsForPredzashitaTaskHandler.HasConfiguredCommissions(
                    provider.GetRequiredService<IOptions<PreDefenseOptions>>().Value))
            {
                return await Finish(
                    CliExit.InvalidConfiguration,
                    [
                        "Pre-defense requires nonempty commissions with members in the existing C# configuration. Configure commissions before running export pre-defense.",
                    ]);
            }

            if (preDefense)
            {
                provider.GetRequiredService<ScheduleBuilder>()
                    .ConfigureRemappings(provider.GetRequiredService<ConfigureRemappingsDelegate>());
            }
            else
            {
                await provider.InitializeSchedule(cancellation.Token);
            }

            if (preDefense)
            {
                // Reuse the handler's mapping and workbook writer, publishing each complete file under the output lease.
                var handler = ActivatorUtilities.CreateInstance<ListsForPredzashitaTaskHandler>(
                    scope.ServiceProvider,
                    new PreDefenseWarningLogger(warnings));
                if (handler.GetConfigurationError() is { } configurationError)
                {
                    return await Finish(CliExit.InvalidConfiguration, [configurationError]);
                }

                output = context.Artifacts = await RunOutput.Create(
                    output: destination.Directory,
                    command: context.Command,
                    runId: context.RunId,
                    cancellation.Token,
                    projectDirectory: resolvedSettings.ProjectDirectory);
                var workbooks = await handler.BuildWorkbooks(ThesisType.An, cancellation.Token);
                foreach (var (number, students) in workbooks.OrderBy(x => x.Key))
                {
                    await output.Publish(
                        $"comisia_{number}.xlsx",
                        async (stream, token) =>
                        {
                            await ListsForPredzashitaTaskHandler.WriteAsync(
                                number,
                                students,
                                stream,
                                token);
                            VerifyTeacherWorkbook(stream, token);
                        },
                        cancellation.Token);
                    workbookCount++;
                    StreamNew();
                }
            }
            else
            {
                var teacherConfig = scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
                if (TeacherProfileScheduleLookup.Find(
                        scope.ServiceProvider.GetRequiredService<LookupFacade>(),
                        teacherConfig.TeacherName) is not { } teacherId)
                {
                    return await Finish(
                        CliExit.InvalidConfiguration,
                        [$"Selected teacher '{teacher}' is absent from this schedule."]);
                }

                var schedule = scope.ServiceProvider.GetRequiredService<Schedule>().Filter(
                    FilterHelper.Builder().WithTeacher(teacherId).WithLessonType(LessonType.Lab));
                if (!schedule.EnumerateLessons().Any())
                {
                    return await Finish(
                        CliExit.InvalidConfiguration,
                        [$"Selected teacher '{teacher}' has no laboratory lessons to export."]);
                }

                warnings.Add(
                    "Laboratory deadlines inspect every period using coded academic date ranges. The existing handler cannot combine shared/all-group labs with other partition/course combinations in a group.");
                foreach (var group in schedule.Groups)
                {
                    var partitions = schedule.EnumerateLessons().Where(x => x.Lesson.Group == group)
                        .GroupBy(x => (Partition: x.Lesson.GroupPartitionKey, Course: x.Lesson.Course)).ToArray();
                    if (partitions.Length > 1 && partitions.Any(x => x.Key.Partition == GroupPartitionKey.All))
                    {
                        return await Finish(
                            CliExit.Unsupported,
                            [
                                "The existing deadlines handler does not support shared/all-group labs mixed with other partition/course combinations in a group.",
                            ]);
                    }
                }
                output = context.Artifacts = await RunOutput.Create(
                    output: destination.Directory,
                    command: context.Command,
                    runId: context.RunId,
                    cancellation.Token,
                    projectDirectory: resolvedSettings.ProjectDirectory);
                var handler = scope.ServiceProvider.GetRequiredService<GenerateDeadlinesExcelTaskHandler>();
                await output.Publish(
                    "deadlines.xlsx",
                    async (stream, token) =>
                    {
                        await handler.Run(new()
                        {
                            CancellationToken = token,
                            OutputStream = stream,
                        });
                        VerifyTeacherWorkbook(stream, token);
                    },
                    cancellation.Token);
                workbookCount++;
                StreamNew();
            }
            StreamNew();
            await output.Complete("succeeded", cancellation.Token);
            StreamNew();
            cancellation.Token.ThrowIfCancellationRequested();
            return await Finish(CliExit.Success, []);
        }
        catch (Exception error)
        {
            var failure = context.ClassifyInputFailure(
                error,
                export: true,
                originalTimeoutMessage: true);
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
                cancellation: true);
            return context.Finish(new CliCompletion<TeacherExportResult>(
                exit,
                errors,
                new(
                    OutputDirectory: output?.DirectoryPath,
                    Teacher: teacher,
                    workbookCount))
            {
                Summary = new
                {
                    outputDirectory = output?.DirectoryPath,
                    teacher,
                    workbookCount,
                    artifactCount = paths.Length,
                },
                Paths = paths,
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = status => cli.WriteHuman(
                    $"{status}: {workbookCount} workbooks, {paths.Length} artifacts in {output?.DirectoryPath}."),
            });
        }
    }

    private static void VerifyTeacherWorkbook(Stream stream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var workbook = SpreadsheetDocument.Open(stream, false);
        if (workbook.WorkbookPart?.WorksheetParts.Any() != true)
        {
            throw new InvalidDataException("Generated workbook has no usable worksheet.");
        }
    }

    private sealed class PreDefenseWarningLogger(List<string> warnings)
        : Microsoft.Extensions.Logging.ILogger<ListsForPredzashitaTaskHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) =>
            level >= Microsoft.Extensions.Logging.LogLevel.Warning;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel level,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level))
            {
                warnings.Add(formatter(state, exception));
            }
        }
    }
}
