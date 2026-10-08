using System.Text.Json;
using CommandDotNet;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed class OutputArguments : IArgumentModel
{
    [Option("output", Description = "Destination directory. Defaults to a unique run under output. Only unchanged manifest-owned files may be replaced.")]
    public string? Directory { get; set; }
}

public sealed record TeachersExcelResult(string? OutputDirectory, int LessonCount, int TeacherCount);

[Command("export")]
public partial class ExportCommands
{
    protected virtual void ConfigureServices(IServiceCollection services) => AppConfiguration.ConfigureServices(services);

    [Command("teachers-excel", Description = "Generate the all-teacher workbook for the latest period. No teacher profile is required; a profile does not filter teachers.")]
    public async Task<int> TeachersExcel(SourceArguments source, OutputArguments destination, ResultArguments result, CancellationToken cancellationToken = default)
    {
        const string command = "export teachers-excel";
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
            var services = CliRuntime.CreateServices(source, ConfigureServices);
            output = await RunOutput.Create(destination.Directory, command, runId, cancellation.Token);
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
            var teacherless = schedule.EnumerateWeeklyLessons().Count(x => !x.Lesson.Teachers.Any());
            if (teacherless > 0) warnings.Add($"The teacher workbook omits {teacherless} weekly lessons with no teacher.");
            var sundays = schedule.EnumerateWeeklyLessons().Count(x => x.Date.DayOfWeek == DayOfWeek.Sunday);
            if (sundays > 0) warnings.Add($"The existing Monday-Saturday layout omits {sundays} Sunday lessons.");
            var seminar = scope.ServiceProvider.GetRequiredService<RegularSeminarDateProvider>().Get();
            var seminarLessons = schedule.EnumerateWeeklyLessons().Count(x => x.Date.DayOfWeek == seminar.Day && x.Date.TimeSlot == seminar.TimeSlot);
            if (seminarLessons > 0) warnings.Add($"The existing seminar display replaces {seminarLessons} lessons at the configured seminar slot.");
            var handler = scope.ServiceProvider.GetRequiredService<GenerateAllTeachersExcelTaskHandler>();
            await output.Publish("all-teachers.xlsx", async (stream, token) =>
            {
                await handler.Run(new() { Schedule = schedule, CancellationToken = token, OutputDirectory = stream });
                token.ThrowIfCancellationRequested();
                stream.Position = 0;
                using var workbook = SpreadsheetDocument.Open(stream, false);
                if (workbook.WorkbookPart?.Workbook.Sheets?.ChildElements.Count != 1 || !workbook.WorkbookPart.WorksheetParts.Any())
                    throw new InvalidDataException("Generated teacher workbook has no usable worksheet.");
            }, cancellation.Token);
            await output.Complete("succeeded", cancellation.Token);
            return await Finish(0, []);
        }
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
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<TeachersExcelResult>(1, command, runId,
                    exit == 0 ? "succeeded" : paths.Length > 0 ? "partial" : "failed", exit, paths, [], warnings.ToArray(), errors,
                    new(output?.DirectoryPath, lessonCount, teacherCount)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
                foreach (var path in paths) Console.WriteLine(path);
            return exit;
        }
    }
}
