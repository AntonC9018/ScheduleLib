using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace ScheduleLib.Cli;

public partial class AuthCommands
{
    protected virtual MicrosoftAuthentication CreateMicrosoftAuthentication(CliCommandContext context) =>
        new(logger: context.CreateLogger(typeof(MicrosoftAuthentication).FullName!));
    protected virtual MicrosoftAuthConfig LoadMicrosoftConfig() => MicrosoftCliConfiguration.Load();

    private async Task<int> ExecuteMicrosoft(
        string verb,
        SettingsArguments settings,
        ResultArguments outputArgs,
        CancellationToken cancellationToken)
    {
        var command = $"auth {verb} microsoft";
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

            var authentication = CreateMicrosoftAuthentication(context);
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

public sealed class MicrosoftAuthConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
}

public static class MicrosoftCliConfiguration
{
    public static MicrosoftAuthConfig Load()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Commands).Assembly)
            .Build();
        try
        {
            return configuration.GetRequiredSection("Microsoft").Get<MicrosoftAuthConfig>()
                ?? throw new InvalidOperationException("Microsoft section is empty.");
        }
        catch (InvalidOperationException)
        {
            throw new JsonException(
                "Microsoft TenantId and ClientId must be configured in the existing Microsoft user-secrets section.");
        }
    }
}
