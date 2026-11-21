using System.Net;

namespace ScheduleLib.Cli;

// Each sync still decides which outcome it records for a given failure; this type only
// names the shared predicates and exit codes.
internal static class RemoteSyncOutcomes
{
    public const string CredentialRefreshTransportError =
        "Google credential refresh failed; the action was not sent. Completed actions remain applied.";

    public static bool IsCancellationTransportError(CancellationToken token, Exception error)
    {
        if (!token.IsCancellationRequested)
        {
            return false;
        }

        if (error is OperationCanceledException or GooglePreSendTransportException)
        {
            return true;
        }

        return false;
    }

    public static bool IsUnauthorized(Google.GoogleApiException error)
        => error.HttpStatusCode == HttpStatusCode.Unauthorized;

    public static bool IsRejected(Google.GoogleApiException error)
    {
        return error.HttpStatusCode is HttpStatusCode.BadRequest
            or HttpStatusCode.Forbidden
            or HttpStatusCode.NotFound
            or HttpStatusCode.Conflict
            or HttpStatusCode.PreconditionFailed
            or HttpStatusCode.UnprocessableEntity;
    }

    /// <summary>An interrupted action may have reached the provider only once it was attempted.</summary>
    public static ActionState InterruptedState(bool attempted, ActionState unattempted)
    {
        if (attempted)
        {
            return ActionState.Uncertain;
        }

        return unattempted;
    }

    public static bool AnyCompleted(IEnumerable<ActionState> states)
        => states.Contains(ActionState.Completed);

    public static bool AnyRemoteEffect(IEnumerable<ActionState> states)
        => states.Any(x => x is ActionState.Completed or ActionState.Uncertain);

    /// <summary>The exit of an output failure, which never retries or reclassifies outcomes.</summary>
    public static int StreamingFailureExit(IEnumerable<ActionState> states)
        => CliExit.AfterEffects(CliExit.Unexpected, effects: AnyCompleted(states));
}
