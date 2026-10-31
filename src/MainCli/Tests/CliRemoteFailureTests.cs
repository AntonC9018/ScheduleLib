using Google.Apis.Auth.OAuth2.Responses;
using ScheduleLib.Cli;

public sealed class CliRemoteFailureTests
{
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

    [Theory]
    [InlineData("invalid_grant", CliExit.AuthenticationRequired)]
    [InlineData("access_denied", CliExit.AuthenticationRequired)]
    [InlineData("temporarily_unavailable", CliExit.IoFailure)]
    [InlineData("server_error", CliExit.IoFailure)]
    public void TokenProviderResponsesRequireLoginOnlyWhenTheGrantIsRejected(string code, int exit)
    {
        using var context = new CliCommandContext(
            "drive sync",
            new(),
            default);
        var error = new TokenResponseException(new TokenErrorResponse
        {
            Error = code,
            ErrorDescription = "synthetic-private-detail",
        });
        var failure = CliRemoteFailure.ClassifyGoogle(
            error,
            context,
            "Curmanschii Anton",
            Messages);
        Assert.Equal(expected: exit, actual: failure.ExitCode);
        Assert.DoesNotContain(expectedSubstring: "synthetic-private-detail", actualString: string.Join('\n', failure.Errors));
        if (exit == CliExit.IoFailure)
        {
            Assert.Equal(expected: "network", actual: Assert.Single(failure.Errors));
        }
    }
}
