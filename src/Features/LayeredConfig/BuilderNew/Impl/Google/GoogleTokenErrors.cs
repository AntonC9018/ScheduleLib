using Google.Apis.Auth.OAuth2.Responses;

namespace ScheduleLib.Application.Config;

public static class GoogleTokenErrors
{
    /// <summary>
    /// Only a rejected grant or declined consent needs a new login. Other OAuth errors, such as
    /// temporarily_unavailable, server_error or invalid_client, keep their own classification.
    /// </summary>
    public static bool RequiresLogin(TokenResponseException error) =>
        error.Error?.Error is "invalid_grant" or "access_denied";
}
