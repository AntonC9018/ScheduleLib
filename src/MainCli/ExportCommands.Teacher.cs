using System.Text.Json;
using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.Options;
using ScheduleLib.Theses.Parsing;
using ScheduleLib;
using ScheduleLib.Application.Config;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed record TeacherExportResult(string? OutputDirectory, string? Teacher, int WorkbookCount);

public partial class ExportCommands
{
    [Command("lab-deadlines", Description = "Generate the selected teacher's laboratory deadlines using coded academic dates and resolved teacher settings, across all periods. Requires --profile. Existing shared/mixed-partition laboratory combinations are unsupported.")]
    public Task<int> LabDeadlines(SourceArguments source, OutputArguments destination, ResultArguments result,
        SettingsArguments settings, CancellationToken cancellationToken = default)
        => TeacherExport(source, destination, result, settings, false, cancellationToken);

    [Command("pre-defense", Description = "Generate year-thesis (An) commission workbooks from the existing coded commission configuration and thesis source. Missing commissions are a prerequisite error. A selected profile does not filter commissions; this command does not discover commissions or publish remotely.")]
    public Task<int> PreDefense(OutputArguments destination, ResultArguments result,
        SettingsArguments settings, CancellationToken cancellationToken = default)
        => TeacherExport(null, destination, result, settings, true, cancellationToken);

    private async Task<int> TeacherExport(SourceArguments? source, OutputArguments destination, ResultArguments result,
        SettingsArguments settings, bool preDefense, CancellationToken cancellationToken)
    {
        var command = preDefense ? "export pre-defense" : "export lab-deadlines";
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        var warnings = new List<string>();
        var workbookCount = 0;
        string? teacher = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (destination.Directory is { } selectedOutput) CliRuntime.ResolvePath(selectedOutput);
            using var resolvedSettings = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            teacher = resolvedSettings.Profile;
            if (!preDefense && teacher is null)
                return await Finish(3, ["A teacher --profile is required. Use config profiles to list identities."]);
            var services = preDefense ? new ServiceCollection() : CliRuntime.CreateServices(source!, ConfigureServices, resolvedSettings.ProjectDirectory);
            if (preDefense) ConfigureServices(services);
            resolvedSettings.ConfigureServices(services);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            if (preDefense && !ListsForPredzashitaTaskHandler.HasConfiguredCommissions(provider.GetRequiredService<IOptions<PreDefenseOptions>>().Value))
                return await Finish(3, ["Pre-defense requires nonempty commissions with members in the existing C# configuration. Configure commissions before running export pre-defense."]);
            if (preDefense)
                provider.GetRequiredService<ScheduleBuilder>().ConfigureRemappings(provider.GetRequiredService<ConfigureRemappingsDelegate>());
            else
                await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            if (preDefense)
            {
                // Reuse the handler's mapping and workbook writer, publishing each complete file under the output lease.
                var handler = ActivatorUtilities.CreateInstance<ListsForPredzashitaTaskHandler>(scope.ServiceProvider,
                    new PreDefenseWarningLogger(warnings));
                if (handler.GetConfigurationError() is { } configurationError)
                    return await Finish(3, [configurationError]);
                output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, projectDirectory: resolvedSettings.ProjectDirectory);
                var workbooks = await handler.BuildWorkbooks(ThesisType.An, cancellation.Token);
                foreach (var (number, students) in workbooks.OrderBy(x => x.Key))
                {
                    await output.Publish($"comisia_{number}.xlsx", async (stream, token) =>
                    {
                        await ListsForPredzashitaTaskHandler.WriteAsync(number, students, stream, token);
                        VerifyTeacherWorkbook(stream, token);
                    }, cancellation.Token);
                    workbookCount++;
                }
            }
            else
            {
                if (scope.ServiceProvider.GetRequiredService<DataProvider<DeadlinesExcelBuiltConfig>>().Get() is null)
                    return await Finish(3, ["Laboratory deadlines settings are missing for the selected teacher."]);
                var teacherConfig = scope.ServiceProvider.GetRequiredService<TeacherLayerConfig>();
                if (scope.ServiceProvider.GetRequiredService<LookupFacade>().Teacher(teacherConfig.TeacherName.ToNameModel()) is not { } teacherId)
                    return await Finish(3, [$"Selected teacher '{teacher}' is absent from this schedule."]);
                var schedule = scope.ServiceProvider.GetRequiredService<Schedule>().Filter(
                    FilterHelper.Builder().WithTeacher(teacherId).WithLessonType(LessonType.Lab));
                if (!schedule.EnumerateLessons().Any())
                    return await Finish(3, [$"Selected teacher '{teacher}' has no laboratory lessons to export."]);
                warnings.Add("Laboratory deadlines inspect every period using coded academic date ranges. The existing handler cannot combine shared/all-group labs with other partition/course combinations in a group.");
                foreach (var group in schedule.Groups)
                {
                    var partitions = schedule.EnumerateLessons().Where(x => x.Lesson.Group == group)
                        .GroupBy(x => (Partition: x.Lesson.GroupPartitionKey, Course: x.Lesson.Course)).ToArray();
                    if (partitions.Length > 1 && partitions.Any(x => x.Key.Partition == GroupPartitionKey.All))
                        return await Finish(8, ["The existing deadlines handler does not support shared/all-group labs mixed with other partition/course combinations in a group."]);
                }
                output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, projectDirectory: resolvedSettings.ProjectDirectory);
                var handler = scope.ServiceProvider.GetRequiredService<GenerateDeadlinesExcelTaskHandler>();
                await output.Publish("deadlines.xlsx", async (stream, token) =>
                {
                    await handler.Run(new() { CancellationToken = token, OutputStream = stream });
                    VerifyTeacherWorkbook(stream, token);
                }, cancellation.Token);
                workbookCount++;
            }
            await output.Complete("succeeded", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            return await Finish(0, []);
        }
        catch (AuthenticationRequiredException e) { return await Finish(4, [e.Message]); }
        catch (HttpRequestException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
        catch (ArgumentException e) { return await Finish(2, [e.Message]); }
        catch (OperationCanceledException e)
        {
            return cancellation.IsCancellationRequested
                ? await Finish(130, ["Cancelled."])
                : await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]);
        }
        catch (LocalOperationBusyException e) { return await Finish(7, [e.Message]); }
        catch (PlatformNotSupportedException e) { return await Finish(8, [e.Message]); }
        catch (JsonException e) { return await Finish(3, [e.Message]); }
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
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<TeacherExportResult>(1, command, runId,
                    exit == 130 ? "cancelled" : exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, teacher, workbookCount)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }

    private static void VerifyTeacherWorkbook(Stream stream, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        stream.Position = 0;
        using var workbook = SpreadsheetDocument.Open(stream, false);
        if (workbook.WorkbookPart?.WorksheetParts.Any() != true)
            throw new InvalidDataException("Generated workbook has no usable worksheet.");
    }

    private sealed class PreDefenseWarningLogger(List<string> warnings) : Microsoft.Extensions.Logging.ILogger<ListsForPredzashitaTaskHandler>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel level) => level >= Microsoft.Extensions.Logging.LogLevel.Warning;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel level, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) warnings.Add(formatter(state, exception));
        }
    }
}
