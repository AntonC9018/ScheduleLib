using System.Text.Json;
using CommandDotNet;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed class ApplyArguments : IArgumentModel
{
    [Option("apply", Description = "Recompute current remote state and execute changes. Without this flag, only preview.")]
    public bool Apply { get; set; }
}

public sealed record RegistryActionOutcome(string Kind, string Destination, string Course, string Groups,
    DateTime Date, string? Topic, int? AttendanceCount, string Status, string? Detail);
public sealed record RegistrySyncResult(RegistryDestination? Target, IReadOnlyList<RegistryActionOutcome> Outcomes,
    IReadOnlyList<RegistrySyncDiagnostic> Diagnostics);

[Command("registry")]
public partial class RegistryCommands
{
    protected virtual IRegistryProvider CreateProvider() => new ConfiguredRegistryProvider();
    protected virtual IRegistryAccountLock CreateAccountLock() => new RegistryAccountLock();

    [Command("sync", Description = "Preview existing positional lesson/topic/attendance reconciliation for a teacher. Apply recomputes; configured extra-lesson policy is preserved. No speculative create retries.")]
    public Task<int> Sync(SourceArguments source, SettingsArguments settings, ResultArguments result,
        ApplyArguments apply, CancellationToken cancellationToken = default)
        => RunLessonActions(source, settings, result, apply, cleanupIntakeLabs: false, cancellationToken);

    [Command("cleanup-intake-labs", Description = "Preview deletion of obsolete intake laboratory columns only after every scheduled lab audience has a more specific destination. Lists needed for scoped fallback are preserved. Apply deletes selected labs and their attendance.")]
    public Task<int> CleanupIntakeLabs(SourceArguments source, SettingsArguments settings, ResultArguments result,
        ApplyArguments apply, CancellationToken cancellationToken = default)
        => RunLessonActions(source, settings, result, apply, cleanupIntakeLabs: true, cancellationToken);

    private async Task<int> RunLessonActions(SourceArguments source, SettingsArguments settings, ResultArguments result,
        ApplyArguments apply, bool cleanupIntakeLabs, CancellationToken cancellationToken)
    {
        var command = cleanupIntakeLabs ? "registry cleanup-intake-labs" : "registry sync";
        var runId = Guid.NewGuid().ToString("N");
        if (CliOutput.ExclusiveError(result) is { } exclusive)
        {
            Console.Error.WriteLine(exclusive);
            return 2;
        }
        var output = new CliOutput(command, runId, result);
        output.EmitStart();
        CliAmbient.Current = output;
        RegistryDestination? target = null;
        var outcomes = new List<RegistryActionOutcome>();
        IReadOnlyList<RegistrySyncDiagnostic> diagnostics = [];
        RegistrySyncDiagnostics? liveDiagnostics = null;
        var streamedWarnings = new HashSet<string>(StringComparer.Ordinal);
        var actionIndexes = new Dictionary<RegistrySyncAction, int>();
        var warnings = new List<string> { cleanupIntakeLabs
            ? "Deleting a laboratory column also deletes its attendance marks. Only unsafe intake-wide laboratory columns are selected."
            : "Existing positional matching is used; updates are not a semantic minimum patch." };
        var attempted = false;
        var finalExit = 1;
        string[] finalErrors = ["Unexpected registry failure."];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(settings.Profile)) return Finish(3, ["Select a teacher with --profile. Use config profiles to list supported teachers."]);
            await using var session = await CreateProvider().Open(source, settings, cancellation.Token);
            target = session.Target;
            diagnostics = session.Diagnostics;
            liveDiagnostics = session.LiveDiagnostics;
            if (liveDiagnostics is not null) liveDiagnostics.Reported += StreamDiagnostic;
            await using var lease = apply.Apply ? await CreateAccountLock().Acquire(target, cancellation.Token) : null;
            // The planner discovers a course's assignments before applying its groups in sequence.
            var stopped = false;
            if (cleanupIntakeLabs) await session.PlanIntakeLabCleanup(ProcessGroup, cancellation.Token);
            else await session.PlanGroups(ProcessGroup, cancellation.Token);
            return stopped ? finalExit : Finish(0, []);

            async Task<bool> ProcessGroup(IReadOnlyList<RegistrySyncAction> plan)
            {
                foreach (var action in plan)
                {
                    actionIndexes.Add(action, outcomes.Count);
                    var initial = ToOutcome(action, !action.Enabled ? "omitted" : apply.Apply ? "not-attempted" : "planned", action.Omission);
                    outcomes.Add(initial);
                    StreamOutcome(outcomes.Count - 1, initial);
                }
                foreach (var action in plan)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!action.Enabled) continue;
                    if (!apply.Apply) continue;
                    string status;
                    string? detail = null;
                    var terminal = false;
                    var terminalExit = 0;
                    string[] terminalErrors = [];
                    var propagateCancel = false;
                    var deletionRefused = false;
                    try
                    {
                        await action.Execute(cancellation.Token);
                        attempted = true;
                        status = "completed";
                    }
                    catch (RegistryActionCancelledException error)
                    {
                        attempted |= error.SubmissionStarted;
                        status = error.SubmissionStarted ? "uncertain" : "cancelled";
                        detail = error.SubmissionStarted
                            ? "Cancelled during submission; inspect the registry before retrying. Creates are never automatically retried."
                            : "Cancelled before form submission.";
                        terminal = true;
                        propagateCancel = true;
                    }
                    catch (OperationCanceledException)
                    {
                        attempted = true;
                        status = "uncertain";
                        detail = "Cancelled during execution; inspect the registry before retrying. Creates are never automatically retried.";
                        terminal = true;
                        propagateCancel = true;
                    }
                    catch (RegistryActionExecutionException error) when (action.Kind == "delete"
                        && error.InnerException is RegistryDeletionRefusedException)
                    {
                        attempted |= error.SubmissionStarted;
                        status = "omitted";
                        detail = "Registry refused deletion; the lesson remains unchanged. Skipped without retrying.";
                        deletionRefused = true;
                    }
                    catch (RegistryActionExecutionException error)
                    {
                        attempted |= error.SubmissionStarted;
                        var rejected = error.InnerException is RegistrySubmissionRejectedException;
                        status = rejected || !error.SubmissionStarted ? "failed" : "uncertain";
                        detail = rejected ? "Registry rejected the submission; no automatic retry was performed."
                            : !error.SubmissionStarted ? "Action failed before form submission."
                            : "Submission result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.";
                        terminal = true;
                        terminalExit = attempted ? 6 : 5;
                        terminalErrors = ["Registry application stopped after an action failure."];
                    }
                    catch (Exception)
                    {
                        // Form validation failures and transport failures are both explicit. No credentials/student details in diagnostics.
                        attempted = true;
                        status = "uncertain";
                        detail = "Submission failed or its result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.";
                        terminal = true;
                        terminalExit = 6;
                        terminalErrors = ["Registry application stopped after a submission failure."];
                    }
                    // The recorded remote outcome is rendered outside the mutation
                    // try above, so a rendering failure cannot be misclassified as
                    // provider uncertainty or trigger another mutation.
                    Record(action, status, detail);
                    if (deletionRefused)
                    {
                        var warning = $"{action.Course} ({action.Groups}), {action.Date:yyyy-MM-dd HH:mm}, {action.Destination}: {detail}";
                        warnings.Add(warning);
                        StreamWarning(warning);
                    }
                    if (propagateCancel) throw new OperationCanceledException();
                    if (terminal)
                    {
                        Finish(terminalExit, terminalErrors);
                        stopped = true;
                        return false;
                    }
                }
                return true;
            }
        }
        catch (CliOutputStreamingException) { return Finish(attempted ? 6 : 1, ["Output streaming failed. Completed actions remain applied; inspect the registry before retrying."]); }
        catch (OperationCanceledException) { return Finish(130, ["Cancelled."]); }
        catch (RegistryAuthenticationException e) { return Finish(4, [e.Message]); }
        catch (LocalOperationBusyException e) { return Finish(7, [e.Message]); }
        catch (JsonException e) { return Finish(3, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, [e.Message]); }
        catch (FileNotFoundException e) { return Finish(3, [e.Message]); }
        catch (InvalidScheduleSourceException e) { return Finish(3, [e.Message]); }
        catch (ScheduleBuildException e) { return Finish(3, [e.Message]); }
        catch (ArgumentException e) { return Finish(2, [e.Message]); }
        catch (PlatformNotSupportedException e) { return Finish(8, [e.Message]); }
        catch (Exception) { return Finish(attempted ? 6 : 5, ["Registry operation failed. No automatic retry was performed."]); }
        finally
        {
            if (liveDiagnostics is not null) liveDiagnostics.Reported -= StreamDiagnostic;
            Console.CancelKeyPress -= cancel;
            CliAmbient.Current = null;
            WriteResult();
        }

        static RegistryActionOutcome ToOutcome(RegistrySyncAction action, string status, string? detail) => new(action.Kind,
            action.Destination, action.Course, action.Groups, action.Date, action.Topic, action.AttendanceCount, status, detail);
        void Record(RegistrySyncAction action, string status, string? detail)
        {
            var index = actionIndexes[action];
            var outcome = ToOutcome(action, status, detail);
            outcomes[index] = outcome;
            StreamOutcome(index, outcome);
        }
        void StreamOutcome(int index, RegistryActionOutcome outcome)
        {
            try
            {
                output.WriteHuman($"{outcome.Kind} {outcome.Date:yyyy-MM-dd HH:mm} {outcome.Course} ({outcome.Groups}): {outcome.Status} {outcome.Detail}");
                output.EmitItem(index.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome.Status, outcome);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new CliOutputStreamingException("Output streaming failed.", ex);
            }
        }
        void StreamDiagnostic(RegistrySyncDiagnostic diagnostic)
        {
            var text = $"{diagnostic.Kind}: {diagnostic.Course} ({diagnostic.Group}). {diagnostic.Detail}";
            StreamWarning(text);
        }
        void StreamWarning(string text)
        {
            if (!streamedWarnings.Add(text)) return;
            try
            {
                output.WriteWarning(text);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new CliOutputStreamingException("Output streaming failed.", ex);
            }
        }
        int Finish(int exit, string[] errors)
        {
            finalExit = attempted && exit is not (0 or 130) ? 6 : exit;
            finalErrors = errors;
            return finalExit;
        }
        void WriteResult()
        {
            var exit = finalExit;
            var errors = finalErrors;
            foreach (var diagnostic in diagnostics)
            {
                var text = $"{diagnostic.Kind}: {diagnostic.Course} ({diagnostic.Group}). {diagnostic.Detail}";
                warnings.Add(text);
            }
            foreach (var warning in warnings)
                if (!streamedWarnings.Contains(warning)) output.WriteWarning(warning);
            foreach (var error in errors) output.WriteError(error);
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : attempted ? "partial" : "failed";
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<RegistrySyncResult>(1, command, runId,
                    status, exit, [], outcomes.Select(x => $"{x.Kind}: {x.Status}").ToArray(), warnings.ToArray(), errors,
                    new(target, outcomes, diagnostics)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (result.Jsonl)
            {
                var counts = outcomes.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count());
                output.EmitFinal(status, exit, warnings.ToArray(), errors, new
                {
                    target,
                    diagnostics,
                    total = outcomes.Count,
                    counts,
                });
            }
            else
            {
                // Items already streamed above; the final summary does not repeat them.
                if (target is not null) output.WriteHuman($"{status}: {target.Destination} (account {target.Account})");
            }
        }
    }
}
