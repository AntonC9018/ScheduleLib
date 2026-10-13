using CommandDotNet;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed class QuizArguments : IArgumentModel
{
    [Option("quiz-id", Description = "Required positive numeric Moodle quiz ID.")]
    public string? QuizId
    {
        get;
        set;
    }

    internal bool IsValid =>
        QuizId is { Length: > 0 } && QuizId.All(x => x is >= '0' and <= '9') && QuizId.Any(x => x != '0');
}

public sealed record RegistryGradeOutcome(
    string Destination,
    string Course,
    string Groups,
    int TestNumber,
    IReadOnlyList<RegistryGradeChange> Grades,
    string Status,
    string? Detail);

public sealed record RegistryGradeResult(
    RegistryDestination? Target,
    string? QuizId,
    IReadOnlyList<RegistryGradeOutcome> Outcomes,
    IReadOnlyList<RegistryGradeNotice> Notices);

public partial class RegistryCommands
{
    protected virtual IRegistryGradePlanner CreateGradePlanner() => new ConfiguredRegistryGradePlanner();

    [Command(
        "import-grades",
        Description =
            "Preview mapped grades from a Moodle quiz using configured teacher credentials and semester. Rounding uses nearest-even; apply rereads under the registry account lock. No automatic retries.")]
    public async Task<int> ImportGrades(
        QuizArguments quiz,
        SourceArguments source,
        SettingsArguments settings,
        ResultArguments result,
        ApplyArguments apply,
        CancellationToken cancellationToken = default)
    {
        using var context = new CliCommandContext(
            "registry import-grades",
            result,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var output = context.Output;
        CliAmbient.Current = output;
        RegistryDestination? target = null;
        var outcomes = new List<RegistryGradeOutcome>();
        var actionIndexes = new Dictionary<RegistryGradeAction, int>();
        var notices = new List<RegistryGradeNotice>();
        var warnings = context.Warnings;
        warnings.Add(
            "Existing name/course/year mapping and last graded attempt policy are used. Moodle report page size is 2000; larger reports are unsupported.");
        var attempted = false;
        var finalExit = CliExit.Unexpected;
        string[] finalErrors = ["Unexpected registry failure."];
        var cancellation = context.Cancellation;
        try
        {
            cancellation.Token.ThrowIfCancellationRequested();
            if (!quiz.IsValid)
            {
                return Finish(CliExit.InvalidArguments, ["--quiz-id must be a positive numeric Moodle quiz ID."]);
            }

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
            await using var lease = apply.Apply ? await CreateAccountLock().Acquire(target, cancellation.Token) : null;
            // Every invocation derives current remote state; no preview transaction is replayed.
            var plan = await CreateGradePlanner().Plan(
                session,
                quiz.QuizId!,
                cancellation.Token);
            notices.AddRange(plan.Notices);
            foreach (var notice in notices)
            {
                warnings.Add($"{notice.Kind}: {notice.Detail}");
            }

            foreach (var action in plan.Actions)
            {
                actionIndexes.Add(action, outcomes.Count);
                var initial = ToOutcome(
                    action,
                    apply.Apply ? "not-attempted" : "planned",
                    null);
                outcomes.Add(initial);
                StreamOutcome(outcomes.Count - 1, initial);
            }
            foreach (var action in plan.Actions)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!apply.Apply)
                {
                    continue;
                }

                var request = new RegistryMutationRequest(action.Execute, cancellation.Token)
                {
                    CancellationPolicy = "Forms",
                };
                var outcome = await RegistryMutation.Execute(request);
                attempted |= outcome.SubmissionStarted;
                Record(
                    action,
                    outcome.Status,
                    outcome.Detail);
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
                    return Finish(exit, outcome.Errors);
                }
            }

            return Finish(CliExit.Success, []);
        }
        catch (Exception error)
        {
            var failure = CliRegistryFailure.Classify(
                error,
                attempted: attempted,
                grades: true);
            return Finish(failure.ExitCode, failure.Errors);
        }
        finally
        {
            WriteResult();
        }

        static RegistryGradeOutcome ToOutcome(
            RegistryGradeAction action,
            string status,
            string? detail)
        {
            return new(
                action.Destination,
                action.Course,
                action.Groups,
                action.TestNumber,
                action.Grades,
                status,
                detail);
        }

        void Record(
            RegistryGradeAction action,
            string status,
            string? detail)
        {
            var index = actionIndexes[action];
            var outcome = ToOutcome(
                action,
                status,
                detail);
            outcomes[index] = outcome;
            StreamOutcome(index, outcome);
        }

        void StreamOutcome(int index, RegistryGradeOutcome outcome)
        {
            context.StreamItem(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                outcome.Status,
                outcome,
                renderHuman: () =>
                {
                    output.WriteHuman(
                        $"Testarea {outcome.TestNumber} {outcome.Course} ({outcome.Groups}) {outcome.Destination}: {outcome.Status} {outcome.Detail}");
                    foreach (var grade in outcome.Grades)
                    {
                        output.WriteHuman(
                            $"  {grade.Student}: {grade.SourceGrade} -> {grade.RoundedGrade} (previous {grade.PreviousGrade})");
                    }
                });
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
            var success = "preview";
            if (apply.Apply)
            {
                success = "applied";
            }
            var status = CliCommandContext.Status(
                exit,
                success,
                partial: attempted);
            var counts = outcomes.GroupBy(x => x.Status).ToDictionary(x => x.Key, x => x.Count());
            var summary = new
            {
                target,
                quizId = quiz.QuizId,
                total = outcomes.Count,
                counts,
                notices,
            };
            var actions = outcomes.Select(x => $"grades: {x.Status}").ToArray();
            var data = new RegistryGradeResult(
                target,
                quiz.QuizId,
                outcomes,
                notices);
            context.Finish(new CliCompletion<RegistryGradeResult>(
                exit,
                errors,
                data)
            {
                Summary = summary,
                Actions = actions,
                Status = status,
                WarningsFirst = true,
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
