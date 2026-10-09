using System.Text.Json;
using CommandDotNet;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed class RegistryHistoryTopicsArguments : IArgumentModel
{
    [Option("local-directory", Description = "Required private output directory named .registry-history. Existing topics are preserved; only counts appear on stdout.")]
    public string? LocalDirectory { get; set; }
}

public interface IRegistryHistoryTopicsProvider
{
    Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token);
}

public sealed partial class ConfiguredRegistryHistoryProvider
{
    public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token) =>
        WithHistoryContext(settings, client => new RegistryHistoryTopicsReader(client, new(RegistryScraping.BaseUrl + "LessonAttendance")).Discover(token), token);
}

public partial class RegistryCommands
{
    protected virtual IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() => new ConfiguredRegistryHistoryProvider();

    [Command("history-topics", Description = "Read current and matching historical topics into private download candidates. Paced read-only requests; group mapping remains manual.")]
    public async Task<int> HistoryTopics(SettingsArguments settings, RegistryHistoryTopicsArguments history, ResultArguments result,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (string.IsNullOrWhiteSpace(settings.Profile) || string.IsNullOrWhiteSpace(history.LocalDirectory))
                return Finish(2, "Select --profile and an explicit --local-directory named .registry-history.");
            try { RegistryHistoryTopicFiles.ValidateDirectory(history.LocalDirectory); }
            catch (Exception) { return Finish(2, "Private history output requires a regular directory named .registry-history."); }
            var analysis = await CreateHistoryTopicsProvider().DiscoverTopics(settings, cancellation.Token);
            var output = await RegistryHistoryTopicFiles.Save(history.LocalDirectory, analysis, cancellation.Token);
            return Finish(0, null, analysis.Current.Count, analysis.Selection.Candidates.Count, analysis.Selection.Unresolved.Count, output.CsvFiles.Count);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return Finish(130, "Cancelled."); }
        catch (RegistryAuthenticationException) { return Finish(4, "Registry authentication configuration failed."); }
        catch (RegistryHistoryException error)
        {
            var status = error.StatusCode is { } code ? code.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown";
            return Finish(5, $"Registry history topics stopped: {error.Failure}; phase {error.Phase}; HTTP {status}.");
        }
        catch (LocalOperationBusyException) { return Finish(7, "Another local registry history operation is active."); }
        catch (Exception) { return Finish(5, "Registry history topic discovery failed or stopped."); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, string? error, int courses = 0, int candidates = 0, int unresolved = 0, int files = 0)
        {
            if (error is not null) Console.Error.WriteLine(error);
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<object>(1, "registry history-topics", runId,
                    exit == 0 ? "downloaded" : "failed", exit, [], [], [], error is null ? [] : [error], new { courses, candidates, unresolved, files }),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (exit == 0) Console.WriteLine($"Saved private download candidates: {courses} current courses, {candidates} historical matches, {unresolved} unresolved courses, {files} CSV files.");
            return exit;
        }
    }
}
