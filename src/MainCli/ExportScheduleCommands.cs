using System.Text.Json;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed record ScheduleExportResult(string? OutputDirectory, int LessonCount, int TeacherCount);

public partial class ExportCommands
{
    [Command("pdf", Description = "Generate all group, partition and teacher PDFs for the latest period. No teacher profile is required.")]
    public Task<int> Pdf(SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default, SettingsArguments? settings = null)
        => ExportSchedule(false, source, destination, result, cancellationToken, settings);

    [Command("ics", Description = "Generate all group, partition and teacher ICS calendars for the latest period; calendars with missing semester dates are reported and skipped. No teacher profile is required.")]
    public Task<int> Ics(SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default, SettingsArguments? settings = null)
        => ExportSchedule(true, source, destination, result, cancellationToken, settings);

    private async Task<int> ExportSchedule(bool calendar, SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken, SettingsArguments? settings)
    {
        var command = calendar ? "export ics" : "export pdf";
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        var warnings = new List<string>();
        var lessonCount = 0;
        var teacherCount = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput) CliRuntime.ResolvePath(selectedOutput);
            using var resolvedSettings = await CliSettings.Load(settings ?? new(), cancellationToken: cancellation.Token);
            var services = CliRuntime.CreateServices(source, ConfigureServices, resolvedSettings.ProjectDirectory);
            resolvedSettings.ConfigureServices(services);
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, resolvedSettings.ProjectDirectory);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.LatestPeriodSchedule();
            lessonCount = schedule.EnumerateWeeklyLessons().Count();
            teacherCount = schedule.Teachers.Count();
            var all = scope.ServiceProvider.GetRequiredService<Schedule>();
            var omitted = all.EnumerateWeeklyLessons().Count() - lessonCount;
            if (omitted > 0) warnings.Add($"Latest-period selection omits {omitted} weekly lessons from other periods.");
            if (teacherCount == 0) warnings.Add("The latest period contains no teachers.");
            if (calendar)
                await scope.ServiceProvider.GetRequiredService<GenerateIcsCalendarsTaskHandler>().RunAsync(new()
                {
                    CancellationToken = cancellation.Token,
                    PublishArtifact = output.Publish,
                    ReportOmission = warnings.Add,
                });
            else
                await scope.ServiceProvider.GetRequiredService<GeneratePdfsForGroupsAndTeachersTaskHandler>().Run(new()
                {
                    CancellationToken = cancellation.Token,
                    PublishArtifact = output.Publish,
                });
            if (output.PublishedPaths.Count == 0) warnings.Add("No artifacts were generated for the latest period.");
            await output.Complete("succeeded", cancellation.Token);
            return await Finish(0, []);
        }
        catch (JsonException e) { return await Finish(3, [e.Message]); }
        catch (ArgumentException e) { return await Finish(2, [e.Message]); }
        catch (OperationCanceledException) { return await Finish(130, ["Cancelled."]); }
        catch (LocalOperationBusyException e) { return await Finish(7, [e.Message]); }
        catch (PlatformNotSupportedException e) { return await Finish(8, [e.Message]); }
        catch (DirectoryNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return await Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return await Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return await Finish(3, [e.Message]); }
        catch (UnauthorizedAccessException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (IOException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (Exception e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 1, [e.Message]); }
        finally
        {
            Console.CancelKeyPress -= cancel;
            output?.Dispose();
        }

        async Task<int> Finish(int exit, string[] errors)
        {
            if (exit != 0 && output is not null && output.PublishedPaths.Count > 0
                && !output.PublishedPaths.Contains(output.ManifestPath))
            {
                try { await output.Complete("partial", CancellationToken.None); }
                catch (Exception e) { warnings.Add($"Could not publish partial output manifest: {e.Message}"); }
            }
            var paths = output?.PublishedPaths.ToArray() ?? [];
            foreach (var error in errors) Console.Error.WriteLine(error);
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<ScheduleExportResult>(1, command, runId,
                    exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, lessonCount, teacherCount)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }
}
