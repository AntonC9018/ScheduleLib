using System.Net;

namespace ScheduleLib.Cli;

/// <summary>
/// Outcome classification shared by the replace-style Google syncs (Calendar and Drive).
/// Each sync still decides which outcome it records for a given failure; this type only
/// names the shared predicates, states and exit codes.
/// </summary>
internal static class RemoteSyncOutcomes
{
    public const string CredentialRefreshTransportError =
        "Google credential refresh transport failed; the action was not sent. Completed actions remain applied.";

    /// <summary>A cancellation surfaced as a transport failure once the token is cancelled.</summary>
    public static bool IsCancellationTransportError(CancellationToken token, Exception error)
    {
        if (!token.IsCancellationRequested)
        {
            return false;
        }

        return error is OperationCanceledException or GooglePreSendTransportException;
    }

    public static bool IsUnauthorized(Google.GoogleApiException error)
        => error.HttpStatusCode == HttpStatusCode.Unauthorized;

    /// <summary>A definitive rejection of the request other than missing authorization.</summary>
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

    /// <summary>The exit of an output failure, which never retries or reclassifies outcomes.</summary>
    public static int StreamingFailureExit(IEnumerable<ActionState> states)
        => CliExit.AfterEffects(CliExit.Unexpected, effects: AnyCompleted(states));
}
