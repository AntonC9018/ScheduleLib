using CommandDotNet;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed class QuizArguments : IArgumentModel
{
    [Option("quiz-id", Description = "Required positive numeric Moodle quiz ID.")]
    public string? QuizId { get; set; }

    internal bool IsValid
    {
        get
        {
            bool CheckCondition()
            {
                if (!(QuizId is { Length: > 0 }))
                {
                    return false;
                }

                if (!(QuizId.All(x => x is >= '0' and <= '9')))
                {
                    return false;
                }

                if (!(QuizId.Any(x => x != '0')))
                {
                    return false;
                }

                return true;
            }

            return CheckCondition();
        }
    }
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
                global::System.Collections.Generic.List<string> list = warnings;
                string item = $"{notice.Kind}: {notice.Detail}";
                list.Add(item);
            }

            foreach (var action in plan.Actions)
            {
                actionIndexes.Add(action, outcomes.Count);
                global::ScheduleLib.OnlineRegistry.RegistryGradeAction action2 = action;
                string status3;
                if (apply.Apply)
                {
                    status3 = "not-attempted";
                }
                else
                {
                    status3 = "planned";
                }

                string? detail2 = null;
                var initial = ToOutcome(
                    action2, status: status3, detail: detail2);
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
                    status: outcome.Status,
                    detail: outcome.Detail);
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
                Destination: action.Destination,
                Course: action.Course,
                Groups: action.Groups,
                action.TestNumber,
                action.Grades,
                Status: status,
                Detail: detail);
        }

        void Record(
            RegistryGradeAction action,
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

        void StreamOutcome(int index, RegistryGradeOutcome outcome)
        {
            global::ScheduleLib.Cli.CliCommandContext cliCommandContext = context;
            string id2 = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string status2 = outcome.Status;
            object item = outcome;
            global::System.Action renderHuman2 = () =>
                {
                    string CalculateLine()
                    {
                        string line = $"Testarea {outcome.TestNumber} {outcome.Course} ({outcome.Groups}) {outcome.Destination}: {outcome.Status} {outcome.Detail}";
                        return line;
                    }

                    output.WriteHuman(
                                            CalculateLine());
                    foreach (var grade in outcome.Grades)
                    {
                        output.WriteHuman(
                            $"  {grade.Student}: {grade.SourceGrade} -> {grade.RoundedGrade} (previous {grade.PreviousGrade})");
                    }
                };
            cliCommandContext.StreamItem(
                id: id2, status: status2, item, renderHuman: renderHuman2);
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
            var counts = outcomes.GroupBy(x => x.Status).ToDictionary(keySelector: x => x.Key, elementSelector: x => x.Count());
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
                        global::ScheduleLib.Cli.CliOutput cliOutput = output;
                        string line = $"{status}: {target.Destination} (account {target.Account})";
                        cliOutput.WriteHuman(line);
                    }
                },
            });
        }
    }
}
