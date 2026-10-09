using System.Text.Json;
using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed record FreeRoomsResult(string? OutputDirectory, int LessonCount, string[] Worksheets);

public partial class ExportCommands
{
    [Command("free-rooms", Description = "Generate the existing free-room workbook: one worksheet per parity, each day that has lessons as a header row, and one row per time slot listing the rooms free in that slot. Existing defaults are preserved and always apply: rooms and occupancy come from every weekly period rather than only the latest one, room identifiers without a block suffix are written as '<room>/4', and days without lessons are skipped. No teacher profile is required; a profile does not filter anything.")]
    public async Task<int> FreeRooms(SourceArguments source, OutputArguments destination, ResultArguments result,
        CancellationToken cancellationToken = default, SettingsArguments? settings = null)
    {
        const string command = "export free-rooms";
        var runId = Guid.NewGuid().ToString("N");
        RunOutput? output = null;
        var warnings = new List<string>();
        var worksheets = new List<string>();
        var lessonCount = 0;
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
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token, projectDirectory: resolvedSettings.ProjectDirectory);
            await using var provider = AppConfiguration.BuildServiceProvider(services);
            await provider.InitializeSchedule(cancellation.Token);
            await using var scope = provider.CreateAsyncScope();
            var schedule = scope.ServiceProvider.GetRequiredService<Schedule>();
            lessonCount = schedule.EnumerateWeeklyLessons().Count();
            warnings.Add("Free rooms inspect every weekly period, not only the latest one.");
            if (lessonCount == 0) warnings.Add("The schedule contains no weekly lessons; the workbook has no day rows.");
            var handler = scope.ServiceProvider.GetRequiredService<GenerateFreeRoomsTaskHandler>();
            await output.Publish("free-rooms.xlsx", async (stream, token) =>
            {
                await handler.Run(new() { CancellationToken = token, OutputStream = stream });
                token.ThrowIfCancellationRequested();
                stream.Position = 0;
                using var workbook = SpreadsheetDocument.Open(stream, false);
                var sheets = workbook.WorkbookPart?.Workbook.Sheets?.ChildElements.OfType<Sheet>().ToArray() ?? [];
                if (sheets.Length == 0 || workbook.WorkbookPart?.WorksheetParts.Any() != true)
                    throw new InvalidDataException("Generated free-room workbook has no usable worksheet.");
                worksheets.AddRange(sheets.Select(x => x.Name?.Value ?? ""));
            }, cancellation.Token);
            await output.Complete("succeeded", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            return await Finish(0, []);
        }
        catch (ArgumentException e) { return await Finish(2, [e.Message]); }
        catch (OperationCanceledException)
        {
            if (cancellation.IsCancellationRequested) return await Finish(130, ["Cancelled."]);
            return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, ["Schedule provider request timed out."]);
        }
        catch (HttpRequestException e) { return await Finish(output?.PublishedPaths.Count > 0 ? 6 : 5, [e.Message]); }
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
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<FreeRoomsResult>(1, command, runId,
                    exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, lessonCount, worksheets.ToArray())), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }
}
