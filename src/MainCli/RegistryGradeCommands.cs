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
                outcomes.Add(ToOutcome(action, apply.Apply ? "not-attempted" : "planned", null));
            }
            foreach (var action in plan.Actions)
            {
                cancellation.Token.ThrowIfCancellationRequested();
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
                        ? "Cancelled during submission; inspect the registry before retrying. Forms are never automatically retried."
                        : "Cancelled before form submission.");
                    throw;
                }
                catch (OperationCanceledException)
                {
                    attempted = true;
                    Record(action, "uncertain", "Cancelled during execution; inspect the registry before retrying. Forms are never automatically retried.");
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
        catch (IOException) { return Finish(attempted ? 6 : 5, ["Registry input/output operation failed. No automatic retry was performed."]); }
        catch (HttpRequestException) { return Finish(attempted ? 6 : 5, ["Registry/Moodle remote read failed. No automatic retry was performed."]); }
        catch (InvalidOperationException) { return Finish(attempted ? 6 : 5, ["Registry/Moodle required form or report markup was unavailable. No automatic retry was performed."]); }
        catch (Exception) { return Finish(attempted ? 6 : 1, ["Unexpected registry operation failure. No automatic retry was performed."]); }
        finally { Console.CancelKeyPress -= cancel; WriteResult(); }

        static RegistryGradeOutcome ToOutcome(RegistryGradeAction action, string status, string? detail) => new(
            action.Destination, action.Course, action.Groups, action.TestNumber, action.Grades, status, detail);
        void Record(RegistryGradeAction action, string status, string? detail) => outcomes[actionIndexes[action]] = ToOutcome(action, status, detail);
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
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<RegistryGradeResult>(1, command, runId,
                    status, exit, [], outcomes.Select(x => $"grades: {x.Status}").ToArray(), warnings.ToArray(), errors,
                    new(target, quiz.QuizId, outcomes, notices)), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else
            {
                if (target is not null) Console.WriteLine($"{status}: {target.Destination} (account {target.Account})");
                foreach (var action in outcomes)
                {
                    Console.WriteLine($"Testarea {action.TestNumber} {action.Course} ({action.Groups}) {action.Destination}: {action.Status} {action.Detail}");
                    foreach (var grade in action.Grades) Console.WriteLine($"  {grade.Student}: {grade.SourceGrade} -> {grade.RoundedGrade} (previous {grade.PreviousGrade})");
                }
            }
        }
    }
}
