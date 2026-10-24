using CommandDotNet;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed class ApplyArguments : IArgumentModel
{
    [Option(
        "apply",
        Description = "Recompute current remote state and execute changes. Without this flag, only preview.")]
    public bool Apply { get; set; }
}

public sealed record RegistryActionOutcome(
    string Kind,
    string Destination,
    string Course,
    string Groups,
    DateTime Date,
    string? Topic,
    int? AttendanceCount,
    string Status,
    string? Detail);

public sealed record RegistrySyncResult(
    RegistryDestination? Target,
    IReadOnlyList<RegistryActionOutcome> Outcomes,
    IReadOnlyList<RegistrySyncDiagnostic> Diagnostics);

[Command("registry")]
public partial class RegistryCommands
{
    protected virtual IRegistryProvider CreateProvider() => new ConfiguredRegistryProvider();
    protected virtual IRegistryAccountLock CreateAccountLock() => new RegistryAccountLock();

    [Command(
        "sync",
        Description =
            "Preview existing positional lesson/topic/attendance reconciliation for a teacher. Apply recomputes; configured extra-lesson policy is preserved. No speculative create retries.")]
    public Task<int> Sync(
        SourceArguments source,
        SettingsArguments settings,
        ResultArguments result,
        ApplyArguments apply,
        CancellationToken cancellationToken = default)
    {
        return RunLessonActions(
                source,
                settings,
                result,
                apply,
                cleanupIntakeLabs: false,
                cancellationToken);
    }

    [Command(
        "cleanup-intake-labs",
        Description =
            "Preview deletion of obsolete intake laboratory columns only after every scheduled lab audience has a more specific destination. Lists needed for scoped fallback are preserved. Apply deletes selected labs and their attendance.")]
    public Task<int> CleanupIntakeLabs(
        SourceArguments source,
        SettingsArguments settings,
        ResultArguments result,
        ApplyArguments apply,
        CancellationToken cancellationToken = default)
    {
        return RunLessonActions(
                source,
                settings,
                result,
                apply,
                cleanupIntakeLabs: true,
                cancellationToken);
    }

    private async Task<int> RunLessonActions(
        SourceArguments source,
        SettingsArguments settings,
        ResultArguments result,
        ApplyArguments apply,
        bool cleanupIntakeLabs,
        CancellationToken cancellationToken)
    {
        using var context = new CliCommandContext(
            cleanupIntakeLabs ? "registry cleanup-intake-labs" : "registry sync",
            result,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        CliAmbient.Current = output;
        RegistryDestination? target = null;
        var outcomes = new List<RegistryActionOutcome>();
        IReadOnlyList<RegistrySyncDiagnostic> diagnostics = [];
        RegistrySyncDiagnostics? liveDiagnostics = null;
        var streamedWarnings = new HashSet<string>(StringComparer.Ordinal);
        var actionIndexes = new Dictionary<RegistrySyncAction, int>();
        var warnings = context.Warnings;
        warnings.Add(
            cleanupIntakeLabs
                ? "Deleting a laboratory column also deletes its attendance marks. Only unsafe intake-wide laboratory columns are selected."
                : "Existing positional matching is used; updates are not a semantic minimum patch.");
        var attempted = false;
        var finalExit = CliExit.Unexpected;
        string[] finalErrors = ["Unexpected registry failure."];
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(settings.Profile))
            {
                return Finish(
                    CliExit.InvalidConfiguration,
                    ["Select a teacher with --profile. Use config profiles to list supported teachers."]);
            }

            await using var session = await CreateProvider().Open(
                source,
                settings,
                cancellation.Token);
            target = session.Target;
            diagnostics = session.Diagnostics;
            liveDiagnostics = session.LiveDiagnostics;
            if (liveDiagnostics is not null)
            {
                liveDiagnostics.Reported += StreamDiagnostic;
            }

            await using var lease = apply.Apply ? await CreateAccountLock().Acquire(target, cancellation.Token) : null;
            // The planner discovers a course's assignments before applying its groups in sequence.
            var stopped = false;
            if (cleanupIntakeLabs)
            {
                await session.PlanIntakeLabCleanup(ProcessGroup, cancellation.Token);
            }
            else
            {
                await session.PlanGroups(ProcessGroup, cancellation.Token);
            }

            if (stopped)
            {
                return finalExit;
            }

            return Finish(CliExit.Success, []);

            async Task<bool> ProcessGroup(IReadOnlyList<RegistrySyncAction> plan)
            {
                foreach (var action in plan)
                {
                    actionIndexes.Add(action, outcomes.Count);
                    var initial = ToOutcome(
                        action,
                        status: !action.Enabled ? "omitted" : apply.Apply ? "not-attempted" : "planned",
                        detail: action.Omission);
                    outcomes.Add(initial);
                    StreamOutcome(outcomes.Count - 1, initial);
                }
                foreach (var action in plan)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!action.Enabled)
                    {
                        continue;
                    }

                    if (!apply.Apply)
                    {
                        continue;
                    }

                    var request = new RegistryMutationRequest(action.Execute, cancellation.Token)
                    {
                        CancellationPolicy = "Creates",
                        AllowRefusedDeletion = action.Kind == "delete",
                    };
                    var outcome = await RegistryMutation.Execute(request);
                    attempted |= outcome.SubmissionStarted;
                    Record(
                        action,
                        status: outcome.Status,
                        detail: outcome.Detail);
                    if (outcome.DeletionRefused)
                    {
                        var warning =
                            $"{action.Course} ({action.Groups}), {action.Date:yyyy-MM-dd HH:mm}, {action.Destination}: {outcome.Detail}";
                        warnings.Add(warning);
                        StreamWarning(warning);
                    }
                    if (outcome.Cancelled)
                    {
                        throw new OperationCanceledException();
                    }
                    if (outcome.Stop)
                    {
                        var exit = CliExit.IoFailure;
                        if (attempted)
                        {
                            exit = CliExit.Partial;
                        }
                        Finish(exit, outcome.Errors);
                        stopped = true;
                        return false;
                    }
                }
                return true;
            }
        }
        catch (Exception error)
        {
            var failure = CliRegistryFailure.Classify(
                error,
                attempted: attempted,
                grades: false);
            return Finish(failure.ExitCode, failure.Errors);
        }
        finally
        {
            if (liveDiagnostics is not null)
            {
                liveDiagnostics.Reported -= StreamDiagnostic;
            }

            WriteResult();
        }

        static RegistryActionOutcome ToOutcome(
            RegistrySyncAction action,
            string status,
            string? detail)
        {
            return new(
                Kind: action.Kind,
                Destination: action.Destination,
                Course: action.Course,
                Groups: action.Groups,
                action.Date,
                Topic: action.Topic,
                action.AttendanceCount,
                Status: status,
                Detail: detail);
        }

        void Record(
            RegistrySyncAction action,
            string status,
            string? detail)
        {
            var index = actionIndexes[action];
            var outcome = ToOutcome(
                action,
                status: status,
                detail: detail);
            outcomes[index] = outcome;
            StreamOutcome(index, outcome);
        }

        void StreamOutcome(int index, RegistryActionOutcome outcome)
        {
            context.StreamItem(
                id: index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                status: outcome.Status,
                outcome,
                renderHuman: () =>
                {
                    output.WriteHuman(
                        $"{outcome.Kind} {outcome.Date:yyyy-MM-dd HH:mm} {outcome.Course} ({outcome.Groups}): {outcome.Status} {outcome.Detail}");
                });
        }

        void StreamDiagnostic(RegistrySyncDiagnostic diagnostic)
        {
            var text = $"{diagnostic.Kind}: {diagnostic.Course} ({diagnostic.Group}). {diagnostic.Detail}";
            StreamWarning(text);
        }

        void StreamWarning(string text)
        {
            if (!streamedWarnings.Add(text))
            {
                return;
            }

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
            finalExit = CliExit.AfterEffects(exit, effects: attempted);
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
            var success = "preview";
            if (apply.Apply)
            {
                success = "applied";
            }
            var status = CliCommandContext.Status(
                exit,
                success,
                partial: attempted);
            var counts = outcomes.GroupBy(x => x.Status).ToDictionary(keySelector: x => x.Key, elementSelector: x => x.Count());
            var summary = new
            {
                target,
                diagnostics,
                total = outcomes.Count,
                counts,
            };
            var actions = outcomes.Select(x => $"{x.Kind}: {x.Status}").ToArray();
            var data = new RegistrySyncResult(
                target,
                outcomes,
                diagnostics);
            context.Finish(new CliCompletion<RegistrySyncResult>(
                exit,
                errors,
                data)
            {
                Summary = summary,
                Actions = actions,
                Status = status,
                WarningsFirst = true,
                ShouldWriteWarning = warning => !streamedWarnings.Contains(warning),
                RenderHuman = status =>
                {
                    if (target is not null)
                    {
                        output.WriteHuman($"{status}: {target.Destination} (account {target.Account})");
                    }
                },
            });
        }
    }
}
