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
    private readonly record struct AuthCommandRequest(
        string Verb,
        AuthProvider Provider,
        SettingsArguments Settings,
        ResultArguments Output,
        CancellationToken CancellationToken);

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
        AuthCommandRequest request = new(
            Verb: "login",
            Provider: provider.Provider,
            Settings: settings,
            Output: output,
            CancellationToken: cancellationToken);
        return Route(request);
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
        AuthCommandRequest request = new(
            Verb: "status",
            Provider: provider.Provider,
            Settings: settings,
            Output: output,
            CancellationToken: cancellationToken);
        return Route(request);
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
        AuthCommandRequest request = new(
            Verb: "logout",
            Provider: provider.Provider,
            Settings: settings,
            Output: output,
            CancellationToken: cancellationToken);
        return Route(request);
    }

    private Task<int> Route(AuthCommandRequest request)
    {
        if (request.Provider == AuthProvider.microsoft)
        {
            return ExecuteMicrosoft(request);
        }

        return Execute(request);
    }

    private static async Task<int> Execute(AuthCommandRequest request)
    {
        var verb = request.Verb;
        var command = $"auth {verb} google";
        using var context = new CliCommandContext(
            command,
            request.Output,
            request.CancellationToken);
        if (!context.Start())
        {
            return CliExit.InvalidArguments;
        }
        var cancellation = context.Cancellation;
        try
        {
            var resolved = await CliSettings.Load(request.Settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } teacher)
            {
                return CliAuthenticationExecution.Finish(
                    context,
                    CliExit.InvalidConfiguration,
                    ["A teacher --profile is required. Use config profiles to list identities."],
                    null,
                    "Google");
            }

            using var authentication = new GoogleAuthentication(
                logger: context.CreateLogger(typeof(GoogleAuthentication).FullName!));
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
