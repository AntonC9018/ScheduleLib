using CommandDotNet;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed class RegistryHistoryTopicsArguments : IArgumentModel
{
    [Option(
        "local-directory",
        Description =
            "Required private output directory named .registry-history. Existing topics are preserved; only counts appear on stdout.")]
    public string? LocalDirectory { get; set; }
}

public interface IRegistryHistoryTopicsProvider
{
    Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token);
}

public sealed partial class ConfiguredRegistryHistoryProvider
{
    public Task<RegistryHistoryTopicsAnalysis> DiscoverTopics(SettingsArguments settings, CancellationToken token)
    {
        return WithHistoryContext(
            settings,
            client =>
            {
                var client2 = client;
                Uri index = new(RegistryScraping.BaseUrl + "LessonAttendance");
                var registryHistoryTopicsReader =
                    new RegistryHistoryTopicsReader(client2, index);
                var token2 = token;
                var
                    discover = registryHistoryTopicsReader.Discover(token2);
                return discover;
            },
            token);
    }
}

public partial class RegistryCommands
{
    protected virtual IRegistryHistoryTopicsProvider CreateHistoryTopicsProvider() =>
        new ConfiguredRegistryHistoryProvider();

    [Command(
        "history-topics",
        Description =
            "Read current and matching historical topics into private download candidates. Paced read-only requests; group mapping remains manual.")]
    public async Task<int> HistoryTopics(
        SettingsArguments settings,
        RegistryHistoryTopicsArguments history,
        ResultArguments resultArgs,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "registry history-topics",
            resultArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cli = context.Output;
        var cancellation = context.Cancellation;
        try
        {
            int MissingInput()
            {
                return Finish(
                    CliExit.InvalidArguments,
                    "Select --profile and an explicit --local-directory named .registry-history.");
            }

            if (string.IsNullOrWhiteSpace(settings.Profile))
            {
                return MissingInput();
            }
            if (string.IsNullOrWhiteSpace(history.LocalDirectory))
            {
                return MissingInput();
            }

            RegistryHistoryTopicDirectory outputDirectory;
            try
            {
                outputDirectory = new(history.LocalDirectory);
            }
            catch (Exception error) when (error is IOException or ArgumentException)
            {
                return Finish(
                    CliExit.InvalidArguments,
                    "Private history output requires a regular directory named .registry-history.");
            }
            var analysis = await CreateHistoryTopicsProvider()
                .DiscoverTopics(settings, cancellation.Token);
            var output = await outputDirectory.Save(
                analysis,
                cancellation.Token);
            // Stream anonymized per-collection counts. Offered year values are
            // coordinator-visible; course/group names, URIs and topic contents
            // remain in private files and never reach stdout.
            for (var yearIndex = 0; yearIndex < analysis.OfferedYears.Count; yearIndex++)
            {
                var offeredYear = analysis.OfferedYears[yearIndex];
                var cliOutput = cli;
                string line = $"Offered year {offeredYear}.";
                cliOutput.WriteHuman(line);
                var cliOutput2 = cli;
                string toString = yearIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string text = $"year-{toString}";
                string id2 = text;
                string status2 = "completed";
                object item = new { year = offeredYear };
                cliOutput2.EmitItem(
                    id: id2,
                    status: status2,
                    item);
            }
            for (var candidateIndex = 0; candidateIndex < analysis.Selection.Candidates.Count; candidateIndex++)
            {
                var candidate = analysis.Selection.Candidates[candidateIndex];
                var lessonCount = candidate.Historical.Groups.Sum((group) => group.Lessons.Count);
                var groupCount = candidate.Historical.Groups.Count;
                var cliOutput3 = cli;
                string line2 =
                    $"Candidate {candidateIndex}: {groupCount} groups, {lessonCount} lessons in {candidate.Historical.Year}.";
                cliOutput3.WriteHuman(
                    line2);
                var cliOutput4 = cli;
                string toString2 = candidateIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string text2 = $"candidate-{toString2}";
                string id3 = text2;
                string status3 = "completed";
                object item2 = new
                {
                    historicalYear = candidate.Historical.Year,
                    groups = groupCount,
                    lessons = lessonCount,
                };
                cliOutput4.EmitItem(
                    id: id3,
                    status: status3,
                    item2);
            }
            for (var unresolvedIndex = 0; unresolvedIndex < analysis.Selection.Unresolved.Count; unresolvedIndex++)
            {
                var unresolved = analysis.Selection.Unresolved[unresolvedIndex];
                var cliOutput5 = cli;
                string line3 = $"Unresolved {unresolvedIndex}: {unresolved.Reason}.";
                cliOutput5.WriteHuman(line3);
                var cliOutput6 = cli;
                string toString3 = unresolvedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                string text3 = $"unresolved-{toString3}";
                string id4 = text3;
                string status4 = "completed";
                object item3 = new { reason = unresolved.Reason.ToString() };
                cliOutput6.EmitItem(
                    id: id4,
                    status: status4,
                    item3);
            }
            return Finish(
                exit: CliExit.Success,
                null,
                courses: analysis.Current.Count,
                candidates: analysis.Selection.Candidates.Count,
                unresolved: analysis.Selection.Unresolved.Count,
                files: output.CsvFiles.Count);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return Finish(CliExit.Cancelled, "Cancelled.");
        }
        catch (RegistryAuthenticationException)
        {
            return Finish(CliExit.AuthenticationRequired, "Registry authentication configuration failed.");
        }
        catch (RegistryHistoryException error)
        {
            var status = error.StatusCode is { } code
                ? code.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "unknown";
            int exit2 = CliExit.IoFailure;
            string error2 =
                $"Registry history topics stopped: {error.Failure}; phase {error.Phase}; diagnostic {error.Diagnostic}; HTTP {status}.";
            int finish = Finish(
                exit2,
                error2);
            return finish;
        }
        catch (LocalOperationBusyException)
        {
            return Finish(CliExit.Busy, "Another local registry history operation is active.");
        }
        // The cause may contain private registry data, so it is classified but never echoed.
        catch (Exception error)
        {
            var exit = context.ClassifyInputFailure(error).ExitCode;
            return Finish(exit, "Registry history topic discovery failed or stopped.");
        }

        int Finish(
            int exit,
            string? error,
            int courses = 0,
            int candidates = 0,
            int unresolved = 0,
            int files = 0)
        {
            string[] errors = [];
            if (error is not null)
            {
                errors = [error];
            }
            var data = new
            {
                courses,
                candidates,
                unresolved,
                files,
            };
            var status = CliCommandContext.Status(exit, success: "downloaded");
            var jsonStatus = CliCommandContext.Status(
                exit,
                success: "downloaded",
                cancellation: false);
            return context.Finish(new CliCompletion<object>(
                exit,
                errors,
                data)
            {
                Summary = data,
                Status = status,
                JsonStatus = jsonStatus,
                RenderHuman = _ =>
                {
                    if (exit == CliExit.Success)
                    {
                        var cliOutput = cli;
                        string line =
                            $"Saved private download candidates: {courses} current courses, {candidates} historical matches, {unresolved} unresolved courses, {files} CSV files.";
                        cliOutput.WriteHuman(
                            line);
                    }
                },
            });
        }
    }
}
