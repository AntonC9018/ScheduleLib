using System.Text.Json;
using ScheduleLib.Curriculum.Download;

namespace ScheduleLib.Cli;

public partial class AuthCommands
{
    protected virtual MicrosoftAuthentication CreateMicrosoftAuthentication() => new();
    protected virtual MicrosoftAuthConfig LoadMicrosoftConfig() => MicrosoftCliConfiguration.Load();

    private async Task<int> ExecuteMicrosoft(
        string verb,
        SettingsArguments settings,
        ResultArguments outputArgs,
        CancellationToken cancellationToken)
    {
        var command = (string) $"auth {verb} microsoft";
        using var context = new CliCommandContext(
            command,
            outputArgs,
            cancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cancellation = context.Cancellation;
        try
        {
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } profile)
            {
                throw new JsonException("A teacher --profile is required. Use config profiles to list identities.");
            }

            var authentication = CreateMicrosoftAuthentication();
            if (verb == "login")
            {
                await authentication.Login(
                    LoadMicrosoftConfig(),
                    profile,
                    cancellation.Token);
            }
            else if (verb == "logout")
            {
                await authentication.Logout(profile, cancellation.Token);
            }

            var data = await authentication.Status(profile, cancellation.Token);
            return CliAuthenticationExecution.Finish(
                context,
                CliExit.Success,
                [],
                data,
                "Microsoft");
        }
        catch (Exception error)
        {
            var failure = CliAuthenticationExecution.ClassifyMicrosoft(error, cancellation.IsCancellationRequested);
            return CliAuthenticationExecution.Finish(
                context,
                failure.ExitCode,
                failure.Errors,
                null,
                "Microsoft");
        }
    }
}

public static class MicrosoftCliConfiguration
{
    public static MicrosoftAuthConfig Load()
    {
        try
        {
            return CredentialHelper.CreateDefaultConfiguration(typeof(Commands).Assembly).GetMicrosoftGraphAuth();
        }
        catch (InvalidOperationException)
        {
            throw new JsonException(
                "Microsoft TenantId and ClientId must be configured in the existing Microsoft user-secrets section.");
        }
    }
}
