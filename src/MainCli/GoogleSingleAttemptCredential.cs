using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using ScheduleLib.Application.Config;

namespace ScheduleLib.Cli;

/// <summary>
/// The downstream API request has not been sent when credential interception fails through
/// transport, timeout or a provider response that does not reject the grant.
/// </summary>
// HttpClient rewrites HttpRequestException into cancellation when its deadline expires.
// Use IOException so the pre-send evidence survives that translation.
public sealed class GooglePreSendTransportException : IOException
{
    // The cause stays available to debugging; public messages never include its details.
    public GooglePreSendTransportException(Exception? innerException = null) : base(
        "Google credential refresh failed before sending the API request.",
        innerException)
    {
    }
}

internal sealed class GoogleSingleAttemptCredential(IHttpExecuteInterceptor credential, string api)
    : IHttpExecuteInterceptor
{
    public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool CalculateConditionResult()
            {
                if (!(credential is UserCredential user))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(user.Token?.RefreshToken))
                {
                    return false;
                }

                if (user.Token is null)
                {
                    return true;
                }

                if (user.Token.IsStale)
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                throw AuthorizationFailure();
            }

            await credential.InterceptAsync(request, cancellationToken);
        }
        catch (TokenResponseException error) when (GoogleTokenErrors.RequiresLogin(error))
        {
            throw AuthorizationFailure(error);
        }
        catch (TokenResponseException error)
        {
            throw new GooglePreSendTransportException(error);
        }
        catch (HttpRequestException error)
        {
            throw new GooglePreSendTransportException(error);
        }
        // The SDK supplies a linked request token, also cancelled by HttpClient.Timeout.
        // Preserve pre-send provenance; only the outer operation knows caller cancellation.
        catch (OperationCanceledException error)
        {
            throw new GooglePreSendTransportException(error);
        }
        catch (InvalidOperationException error) when (IsSdkRefreshTimeout(error))
        {
            throw new GooglePreSendTransportException(error);
        }
    }

    private Google.GoogleApiException AuthorizationFailure(Exception? cause = null)
    {
        return new(serviceName: api, message: "Google authorization failed.", cause)
        {
            HttpStatusCode = System.Net.HttpStatusCode.Unauthorized,
        };
    }

    private bool IsSdkRefreshTimeout(InvalidOperationException error)
    {
        // Google.Apis.Auth 1.73 discards the three timeout exceptions. Match both the
        // SDK throw site (async state machine) and its exact timeout-only terminal
        // condition, never arbitrary InvalidOperationException or credential messages.
        var declaringType = error.TargetSite?.DeclaringType;

        bool CheckCondition()
        {
            if (!(credential is UserCredential))
            {
                return false;
            }

            if (!(declaringType?.Assembly == typeof(UserCredential).Assembly))
            {
                return false;
            }

            if (declaringType.DeclaringType?.FullName != "Google.Apis.Auth.OAuth2.TokenRefreshManager")
            {
                return false;
            }

            if (!declaringType.Name.StartsWith("<RefreshTokenAsync>", StringComparison.Ordinal))
            {
                return false;
            }

            if (error.Message
                == "The access token has expired and could not be refreshed. Errors: timeout, timeout, timeout")
            {
                return true;
            }

            return false;
        }

        return CheckCondition();
    }
}
