using System.Text.Json;
using CommandDotNet;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Builders;

namespace ScheduleLib.Cli;

public sealed class QuizArguments : IArgumentModel
{
    [Option("quiz-id", Description = "Required positive numeric Moodle quiz ID.")]
    public string? QuizId { get; set; }
    internal bool IsValid => QuizId is { Length: > 0 } && QuizId.All(x => x is >= '0' and <= '9')
        && QuizId.Any(x => x != '0');
}

public sealed record RegistryGradeOutcome(string Destination, string Course, string Groups, int TestNumber,
    IReadOnlyList<RegistryGradeChange> Grades, string Status, string? Detail);
public sealed record RegistryGradeResult(RegistryDestination? Target, string? QuizId,
    IReadOnlyList<RegistryGradeOutcome> Outcomes, IReadOnlyList<RegistryGradeNotice> Notices);

public partial class RegistryCommands
{
    protected virtual IRegistryGradePlanner CreateGradePlanner() => new ConfiguredRegistryGradePlanner();

    [Command("import-grades", Description = "Preview mapped grades from a Moodle quiz using configured teacher credentials and semester. Rounding uses nearest-even; apply rereads under the registry account lock. No automatic retries.")]
    public async Task<int> ImportGrades(QuizArguments quiz, SourceArguments source, SettingsArguments settings, ResultArguments result,
        ApplyArguments apply, CancellationToken cancellationToken = default)
    {
        const string command = "registry import-grades";
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
        var outcomes = new List<RegistryGradeOutcome>();
        var actionIndexes = new Dictionary<RegistryGradeAction, int>();
        var notices = new List<RegistryGradeNotice>();
        var warnings = new List<string> { "Existing name/course/year mapping and last graded attempt policy are used. Moodle report page size is 2000; larger reports are unsupported." };
        var attempted = false;
        var finalExit = 1;
        string[] finalErrors = ["Unexpected registry failure."];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!quiz.IsValid) return Finish(2, ["--quiz-id must be a positive numeric Moodle quiz ID."]);
            if (string.IsNullOrWhiteSpace(settings.Profile)) return Finish(3, ["Select a teacher with --profile. Use config profiles to list supported teachers."]);
            await using var session = await CreateProvider().Open(source, settings, cancellation.Token);
            target = session.Target;
            await using var lease = apply.Apply ? await CreateAccountLock().Acquire(target, cancellation.Token) : null;
            // Every invocation derives current remote state; no preview transaction is replayed.
            var plan = await CreateGradePlanner().Plan(session, quiz.QuizId!, cancellation.Token);
            notices.AddRange(plan.Notices);
            foreach (var notice in notices) warnings.Add($"{notice.Kind}: {notice.Detail}");
            foreach (var action in plan.Actions)
            {
                actionIndexes.Add(action, outcomes.Count);
                var initial = ToOutcome(action, apply.Apply ? "not-attempted" : "planned", null);
                outcomes.Add(initial);
                StreamOutcome(outcomes.Count - 1, initial);
            }
            foreach (var action in plan.Actions)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!apply.Apply) continue;
                string status;
                string? detail = null;
                var terminal = false;
                var terminalExit = 0;
                string[] terminalErrors = [];
                var propagateCancel = false;
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
                        ? "Cancelled during submission; inspect the registry before retrying. Forms are never automatically retried."
                        : "Cancelled before form submission.";
                    terminal = true;
                    propagateCancel = true;
                }
                catch (OperationCanceledException)
                {
                    attempted = true;
                    status = "uncertain";
                    detail = "Cancelled during execution; inspect the registry before retrying. Forms are never automatically retried.";
                    terminal = true;
                    propagateCancel = true;
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
                if (propagateCancel) throw new OperationCanceledException();
                if (terminal) return Finish(terminalExit, terminalErrors);
            }
            return Finish(0, []);
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
        catch (IOException) { return Finish(attempted ? 6 : 5, ["Registry input/output operation failed. No automatic retry was performed."]); }
        catch (HttpRequestException) { return Finish(attempted ? 6 : 5, ["Registry/Moodle remote read failed. No automatic retry was performed."]); }
        catch (InvalidOperationException) { return Finish(attempted ? 6 : 5, ["Registry/Moodle required form or report markup was unavailable. No automatic retry was performed."]); }
        catch (Exception) { return Finish(attempted ? 6 : 1, ["Unexpected registry operation failure. No automatic retry was performed."]); }
        finally { Console.CancelKeyPress -= cancel; CliAmbient.Current = null; WriteResult(); }

        static RegistryGradeOutcome ToOutcome(RegistryGradeAction action, string status, string? detail)
        {
            return new(action.Destination, action.Course, action.Groups, action.TestNumber, action.Grades, status, detail);
        }
        void Record(RegistryGradeAction action, string status, string? detail)
        {
            var index = actionIndexes[action];
            var outcome = ToOutcome(action, status, detail);
            outcomes[index] = outcome;
            StreamOutcome(index, outcome);
        }
        void StreamOutcome(int index, RegistryGradeOutcome outcome)
        {
            try
            {
                output.WriteHuman($"Testarea {outcome.TestNumber} {outcome.Course} ({outcome.Groups}) {outcome.Destination}: {outcome.Status} {outcome.Detail}");
                foreach (var grade in outcome.Grades)
                    output.WriteHuman($"  {grade.Student}: {grade.SourceGrade} -> {grade.RoundedGrade} (previous {grade.PreviousGrade})");
                output.EmitItem(index.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome.Status, outcome);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new CliOutputStreamingException("Output streaming failed.", ex);
            }
        }
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
            foreach (var warning in warnings) output.WriteWarning(warning);
            foreach (var error in errors) output.WriteError(error);
            var status = exit == 130 ? "cancelled" : exit == 0 ? apply.Apply ? "applied" : "preview" : attempted ? "partial" : "failed";
            if (result.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<RegistryGradeResult>(1, command, runId,
                    status, exit, [], outcomes.Select(x => $"grades: {x.Status}").ToArray(), warnings.ToArray(), errors,
                    new(target, quiz.QuizId, outcomes, notices)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (result.Jsonl)
            {
                var counts = outcomes.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count());
                output.EmitFinal(status, exit, warnings.ToArray(), errors, new
                {
                    target,
                    quizId = quiz.QuizId,
                    total = outcomes.Count,
                    counts,
                    notices,
                });
            }
            else
            {
                if (target is not null) output.WriteHuman($"{status}: {target.Destination} (account {target.Account})");
            }
        }
    }
}
