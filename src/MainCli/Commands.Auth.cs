using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand] public AuthCommands Auth { get; set; } = new();
}

// Enum names are the public command literals (CommandDotNet matches case exactly).
public enum AuthProvider
{
    google,
    microsoft,
}

public sealed class AuthProviderArguments : IArgumentModel
{
    [Operand("provider", Description = "Authentication provider: google or microsoft.")]
    public AuthProvider Provider { get; set; }
}

[Command("auth", Description = "Manage local authorization; only login initiates browser consent.")]
public partial class AuthCommands
{
    [Command(
        "login",
        Description =
            "Explicitly authorize the selected teacher for the chosen provider using configured client settings.")]
    public Task<int> Login(
        AuthProviderArguments provider,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        return Route(
            "login",
            provider.Provider,
            settings,
            output,
            cancellationToken);
    }

    [Command(
        "status",
        Description = "Inspect local teacher/client authorization without contacting providers or loading schedules.")]
    public Task<int> Status(
        AuthProviderArguments provider,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        return Route(
            "status",
            provider.Provider,
            settings,
            output,
            cancellationToken);
    }

    [Command(
        "logout",
        Description = "Delete local authorization for this teacher's accounts. No cloud revocation is performed.")]
    public Task<int> Logout(
        AuthProviderArguments provider,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken cancellationToken = default)
    {
        return Route(
            "logout",
            provider.Provider,
            settings,
            output,
            cancellationToken);
    }

    private Task<int> Route(
        string verb,
        AuthProvider provider,
        SettingsArguments settings,
        ResultArguments output,
        CancellationToken token)
    {
        return provider == AuthProvider.microsoft
            ? ExecuteMicrosoft(
                verb,
                settings,
                output,
                token)
            : Execute(
                verb,
                settings,
                output,
                token);
    }

    private static async Task<int> Execute(
        string verb,
        SettingsArguments settings,
        ResultArguments outputArgs,
        CancellationToken cancellationToken)
    {
        var command = (string) $"auth {verb} google";
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
            if (resolved.Profile is not { } teacher)
            {
                return CliAuthenticationExecution.Finish(
                    context,
                    CliExit.InvalidConfiguration,
                    ["A teacher --profile is required. Use config profiles to list identities."],
                    null,
                    "Google");
            }

            using var authentication = new GoogleAuthentication();
            if (verb == "login")
            {
                var credentials = new[]
                    {
                        resolved.Get(GoogleCalendarConfig.Key)?.Credentials,
                        resolved.Get(GoogleDriveConfig.Key)?.Credentials,
                    }
                    .Where(x => x is not null)
                    .Select(x => x!.Build())
                    .ToArray();
                if (credentials.Length == 0)
                {
                    return CliAuthenticationExecution.Finish(
                        context,
                        CliExit.InvalidConfiguration,
                        ["Google client configuration is missing for this profile."],
                        null,
                        "Google");
                }
                // Only credential binders are registered. No schedule/runtime or unrelated cloud services are created.
                var services = new ServiceCollection();
                services.AddConfigsServices();
                services.AddGlobalConfiguration();
                resolved.ConfigureServices(services);
                services.AddScoped<CurrentUserNameProvider>();
                await using var container = services.BuildServiceProvider();
                await using var scope = container.CreateAsyncScope();
                await authentication.Login(teacher, scope.ServiceProvider)
                    .WithCredentials(credentials)
                    .Execute(cancellation.Token);
            }
            else if (verb == "logout")
            {
                await authentication.Logout(teacher, cancellation.Token);
            }

            return CliAuthenticationExecution.Finish(
                context,
                CliExit.Success,
                [],
                await authentication.Status(teacher, cancellation.Token),
                "Google");
        }
        catch (Exception error)
        {
            var failure = CliAuthenticationExecution.ClassifyGoogle(error);
            return CliAuthenticationExecution.Finish(
                context,
                failure.ExitCode,
                failure.Errors,
                null,
                "Google");
        }
    }
}
