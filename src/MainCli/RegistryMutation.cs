using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

internal sealed record RegistryMutationRequest(Func<CancellationToken, Task> Submit, CancellationToken Token)
{
    public required string CancellationPolicy { get; init; }

    public bool AllowRefusedDeletion { get; init; }
}

internal sealed record RegistryMutationOutcome
{
    public required string Status { get; init; }

    public string? Detail { get; init; }

    public bool SubmissionStarted { get; init; }

    public bool Cancelled { get; init; }

    public bool DeletionRefused { get; init; }

    public string[] Errors { get; init; } = [];

    public bool Stop
    {
        get
        {
            if (Cancelled)
            {
                return true;
            }

            if (Errors.Length > 0)
            {
                return true;
            }

            return false;
        }
    }
}

/// <summary>Executes one submission once. Rendering runs after this returns and cannot be a remote failure.</summary>
internal static class RegistryMutation
{
    public static async Task<RegistryMutationOutcome> Execute(RegistryMutationRequest request)
    {
        static bool IsAllowedRefusedDeletion(
            RegistryMutationRequest request,
            RegistryActionExecutionException error)
        {
            if (!request.AllowRefusedDeletion)
            {
                return false;
            }

            if (error.InnerException is not RegistryDeletionRefusedException)
            {
                return false;
            }

            return true;
        }

        try
        {
            await request.Submit(request.Token);
            return new()
            {
                Status = "completed",
                SubmissionStarted = true,
            };
        }
        catch (RegistryActionCancelledException error)
        {
            var status = "cancelled";
            var detail = "Cancelled before form submission.";
            if (error.SubmissionStarted)
            {
                status = "uncertain";
                detail =
                    $"Cancelled during submission; inspect the registry before retrying. {request.CancellationPolicy} are never automatically retried.";
            }
            return new()
            {
                Status = status,
                Detail = detail,
                SubmissionStarted = error.SubmissionStarted,
                Cancelled = true,
            };
        }
        catch (OperationCanceledException)
        {
            return new()
            {
                Status = "uncertain",
                Detail =
                    $"Cancelled during execution; inspect the registry before retrying. {request.CancellationPolicy} are never automatically retried.",
                SubmissionStarted = true,
                Cancelled = true,
            };
        }
        catch (RegistryActionExecutionException error) when (IsAllowedRefusedDeletion(request, error))
        {
            return new()
            {
                Status = "omitted",
                Detail = "Registry refused deletion; the lesson remains unchanged. Skipped without retrying.",
                SubmissionStarted = error.SubmissionStarted,
                DeletionRefused = true,
            };
        }
        catch (RegistryActionExecutionException error)
        {
            var status = "uncertain";
            var detail =
                "Submission result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.";
            if (error.InnerException is RegistrySubmissionRejectedException)
            {
                status = "failed";
                detail = "Registry rejected the submission; no automatic retry was performed.";
            }
            else if (!error.SubmissionStarted)
            {
                status = "failed";
                detail = "Action failed before form submission.";
            }
            return new()
            {
                Status = status,
                Detail = detail,
                SubmissionStarted = error.SubmissionStarted,
                Errors = ["Registry application stopped after an action failure."],
            };
        }
        catch (Exception)
        {
            return new()
            {
                Status = "uncertain",
                Detail =
                    "Submission failed or its result could not be confirmed. Inspect the registry before retrying; no automatic retry was performed.",
                SubmissionStarted = true,
                Errors = ["Registry application stopped after a submission failure."],
            };
        }
    }
}
