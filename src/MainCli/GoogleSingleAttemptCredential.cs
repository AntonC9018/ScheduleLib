using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;

namespace ScheduleLib.Cli;

/// <summary>The downstream API request has not been sent when credential interception fails.</summary>
// HttpClient rewrites HttpRequestException into cancellation when its deadline expires.
// Use IOException so the pre-send evidence survives that translation.
public sealed class GooglePreSendTransportException : IOException
{
    public GooglePreSendTransportException() : base(
        "Google credential refresh transport failed before sending the API request.")
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
            bool conditionResult;
            if (credential is UserCredential user)
            {
                if (string.IsNullOrEmpty(user.Token?.RefreshToken))
                {
                    if (user.Token is null)
                    {
                        conditionResult = true;
                    }
                    else
                    {
                        conditionResult = user.Token.IsStale;
                    }
                }
                else
                {
                    conditionResult = false;
                }
            }
            else
            {
                conditionResult = false;
            }

            if (conditionResult)
            {
                throw AuthorizationFailure();
            }

            await credential.InterceptAsync(request, cancellationToken);
        }
        catch (TokenResponseException)
        {
            throw AuthorizationFailure();
        }
        catch (HttpRequestException)
        {
            throw new GooglePreSendTransportException();
        }
        // The SDK supplies a linked request token, also cancelled by HttpClient.Timeout.
        // Preserve pre-send provenance; only the outer operation knows caller cancellation.
        catch (OperationCanceledException)
        {
            throw new GooglePreSendTransportException();
        }
        catch (InvalidOperationException error) when (IsSdkRefreshTimeout(error))
        {
            throw new GooglePreSendTransportException();
        }
    }

    private Google.GoogleApiException AuthorizationFailure()
    {
        return new(serviceName: api, message: "Google authorization failed.")
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

            if (!(declaringType.DeclaringType?.FullName == "Google.Apis.Auth.OAuth2.TokenRefreshManager"))
            {
                return false;
            }

            if (!(declaringType.Name.StartsWith("<RefreshTokenAsync>", StringComparison.Ordinal)))
            {
                return false;
            }

            if (!(error.Message
                        == "The access token has expired and could not be refreshed. Errors: timeout, timeout, timeout"))
            {
                return false;
            }

            return true;
        }

        return CheckCondition();
    }
}
