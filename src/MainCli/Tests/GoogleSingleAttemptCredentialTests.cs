using Google;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;
using ScheduleLib.Cli;

public sealed class GoogleSingleAttemptCredentialTests
{
    private const string Secret = "refresh-secret-detail";

    private static readonly GoogleFailureMessages Messages = new()
    {
        Timeout = "timeout",
        Network = "network",
        Busy = "busy",
        Io = "io",
        Access = "access",
        Rejected = "rejected",
        Unexpected = "unexpected",
    };

    public static TheoryData<Exception, bool> Causes => new()
    {
        { TokenError("invalid_grant"), true },
        { TokenError("access_denied"), true },
        { TokenError("temporarily_unavailable"), false },
        { TokenError("server_error"), false },
        { new HttpRequestException(Secret), false },
        { new OperationCanceledException(Secret), false },
    };

    [Theory]
    [MemberData(nameof(Causes))]
    public async Task TranslatedRefreshFailuresChainTheCauseWithoutExposingIt(Exception cause, bool requiresLogin)
    {
        var credential = new GoogleSingleAttemptCredential(new Throwing(cause), "drive");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/files");
        var error = await Assert.ThrowsAnyAsync<Exception>(() => credential.InterceptAsync(request, default));

        Assert.Same(expected: cause, actual: error.InnerException);
        if (requiresLogin)
        {
            var api = Assert.IsType<GoogleApiException>(error);
            Assert.Equal(expected: System.Net.HttpStatusCode.Unauthorized, actual: api.HttpStatusCode);
        }
        else
        {
            Assert.IsType<GooglePreSendTransportException>(error);
        }
        Assert.DoesNotContain(expectedSubstring: Secret, actualString: error.Message);

        using var context = new CliCommandContext(
            "drive sync",
            new(),
            default);
        var failure = CliRemoteFailure.ClassifyGoogle(
            error,
            context,
            "Curmanschii Anton",
            Messages);
        Assert.Equal(
            expected: requiresLogin ? CliExit.AuthenticationRequired : CliExit.IoFailure,
            actual: failure.ExitCode);
        Assert.DoesNotContain(expectedSubstring: Secret, actualString: string.Join('\n', failure.Errors));
    }

    private static TokenResponseException TokenError(string code)
    {
        return new(new TokenErrorResponse
        {
            Error = code,
            ErrorDescription = Secret,
        });
    }

    private sealed class Throwing(Exception cause) : IHttpExecuteInterceptor
    {
        public Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException(cause);
    }
}
