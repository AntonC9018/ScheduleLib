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
public sealed record RegistrySyncResult(RegistryDestination? Target, IReadOnlyList<RegistryActionOutcome> Outcomes);

[Command("registry")]
public partial class RegistryCommands
{
    protected virtual IRegistryProvider CreateProvider() => new ConfiguredRegistryProvider();
    protected virtual IRegistryAccountLock CreateAccountLock() => new RegistryAccountLock();

    [Command("sync", Description = "Preview existing positional lesson/topic/attendance reconciliation for a teacher. Apply recomputes; configured extra-lesson policy is preserved. No speculative create retries.")]
    public async Task<int> Sync(SourceArguments source, SettingsArguments settings, ResultArguments result,
        ApplyArguments apply, CancellationToken cancellationToken = default)
    {
        const string command = "registry sync";
        var runId = Guid.NewGuid().ToString("N");
        RegistryDestination? target = null;
        var outcomes = new List<RegistryActionOutcome>();
        var actionIndexes = new Dictionary<RegistrySyncAction, int>();
        var warnings = new List<string> { "Existing positional matching is used; updates are not a semantic minimum patch." };
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
            await using var lease = apply.Apply ? await CreateAccountLock().Acquire(target, cancellation.Token) : null;
            // Every invocation derives current remote state; no preview transaction is replayed.
            var plan = await session.Plan(cancellation.Token);
            foreach (var action in plan)
            {
                actionIndexes.Add(action, outcomes.Count);
                outcomes.Add(ToOutcome(action, !action.Enabled ? "omitted" : apply.Apply ? "not-attempted" : "planned", action.Omission));
            }
            foreach (var action in plan)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!action.Enabled) { Record(action, "omitted", action.Omission); continue; }
                if (!apply.Apply) { Record(action, "planned", null); continue; }
                try
                {
                    await action.Execute(cancellation.Token);
                    attempted = true;
                    Record(action, "completed", null);
                }
                catch (RegistryActionCancelledException error)
                {
                    attempted |= error.SubmissionStarted;
                    Record(action, error.SubmissionStarted ? "uncertain" : "cancelled", error.SubmissionStarted
                        ? "Cancelled during submission; inspect the registry before retrying. Creates are never automatically retried."
                        : "Cancelled before form submission.");
                    throw;
                }
                catch (OperationCanceledException)
                {
                    attempted = true;
                    Record(action, "uncertain", "Cancelled during execution; inspect the registry before retrying. Creates are never automatically retried.");
                    throw;
                }
                catch (RegistryActionExecutionException error)
                {
                    attempted |= error.SubmissionStarted;
                    var rejected = error.InnerException is RegistrySubmissionRejectedException;
                    Record(action, rejected || !error.SubmissionStarted ? "failed" : "uncertain",
                        rejected ? "Registry rejected the submission; no automatic retry was performed."
                        : !error.SubmissionStarted ? "Action failed before form submission."
                        : "Submission result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.");
                    return Finish(attempted ? 6 : 5, ["Registry application stopped after an action failure."]);
                }
                catch (Exception)
                {
                    // Form validation failures and transport failures are both explicit. No credentials/student details in diagnostics.
                    attempted = true;
                    Record(action, "uncertain", "Submission failed or its result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.");
                    return Finish(6, ["Registry application stopped after a submission failure."]);
                }
            }
            return Finish(0, []);
        }
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
        finally { Console.CancelKeyPress -= cancel; WriteResult(); }

        static RegistryActionOutcome ToOutcome(RegistrySyncAction action, string status, string? detail) => new(action.Kind,
            action.Destination, action.Course, action.Groups, action.Date, action.Topic, action.AttendanceCount, status, detail);
        void Record(RegistrySyncAction action, string status, string? detail) => outcomes[actionIndexes[action]] = ToOutcome(action, status, detail);
        int Finish(int exit, string[] errors)
        {
            finalExit = exit;
            finalErrors = errors;
            return exit;
        }
        void WriteResult()
        {
            var exit = finalExit;
            var errors = finalErrors;
            foreach (var warning in warnings) Console.Error.WriteLine(warning);
            foreach (var error in errors) Console.Error.WriteLine(error);
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : attempted ? "partial" : "failed";
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<RegistrySyncResult>(1, command, runId,
                    status, exit, [], outcomes.Select(x => $"{x.Kind}: {x.Status}").ToArray(), warnings.ToArray(), errors,
                    new(target, outcomes)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
            {
                if (target is not null) Console.WriteLine($"{status}: {target.Destination} (account {target.Account})");
                foreach (var action in outcomes) Console.WriteLine($"{action.Kind} {action.Date:yyyy-MM-dd HH:mm} {action.Course} ({action.Groups}): {action.Status} {action.Detail}");
            }
        }
    }
}
