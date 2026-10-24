using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record FreeRoomsResult(string? OutputDirectory, int LessonCount, string[] Worksheets);

public partial class ExportCommands
{
    [Command(
        "free-rooms",
        Description =
            "Generate the existing free-room workbook: one worksheet per parity, each day that has lessons as a header row, and one row per time slot listing the rooms free in that slot. Existing defaults are preserved and always apply: rooms and occupancy come from every weekly period rather than only the latest one, room identifiers without a block suffix are written as '<room>/4', and days without lessons are skipped. No teacher profile is required; a profile does not filter anything.")]
    public async Task<int> FreeRooms(
        SourceArguments source,
        OutputArguments destination,
        ResultArguments resultArgs,
        CancellationToken cancellationToken = default,
        SettingsArguments? settings = null)
    {
        using var context = new CliCommandContext(
            "export free-rooms",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        RunOutput? output = null;
        var warnings = context.Warnings;
        var worksheets = new List<string>();
        var lessonCount = 0;
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
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            lessonCount = schedule.EnumerateWeeklyLessons().Count();
            warnings.Add("Free rooms inspect every weekly period, not only the latest one.");
            if (lessonCount == 0)
            {
                warnings.Add("The schedule contains no weekly lessons; the workbook has no day rows.");
            }

            var handler = scope.ServiceProvider.GetRequiredService<GenerateFreeRoomsTaskHandler>();
            await output.Publish(
                "free-rooms.xlsx",
                async (stream, token) =>
                {
                    await handler.Run(new()
                    {
                        CancellationToken = token,
                        OutputStream = stream,
                    });
                    token.ThrowIfCancellationRequested();
                    stream.Position = 0;
                    using var workbook = SpreadsheetDocument.Open(stream, false);
                    var sheets = workbook.WorkbookPart?.Workbook.Sheets?.ChildElements.OfType<Sheet>().ToArray() ?? [];
                    if (sheets.Length == 0 || workbook.WorkbookPart?.WorksheetParts.Any() != true)
                    {
                        throw new InvalidDataException("Generated free-room workbook has no usable worksheet.");
                    }

                    worksheets.AddRange(sheets.Select(x => x.Name?.Value ?? ""));
                },
                cancellation.Token);
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
            return context.Finish(new CliCompletion<FreeRoomsResult>(
                exit,
                errors,
                new(
                    output?.DirectoryPath,
                    lessonCount,
                    worksheets.ToArray()))
            {
                Summary = new
                {
                    outputDirectory = output?.DirectoryPath,
                    lessonCount,
                    worksheets = worksheets.ToArray(),
                    artifactCount = paths.Length,
                },
                Paths = paths,
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = status => cli.WriteHuman(
                    $"{status}: {lessonCount} lessons, {paths.Length} artifacts in {output?.DirectoryPath}."),
            });
        }
    }
}
