using System.Text.Json;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed partial class Commands
{
    [Subcommand]
    public AuthCommands Auth { get; set; } = new();
}

// Enum names are the public command literals (CommandDotNet matches case exactly).
public enum AuthProvider { google }
public sealed class AuthProviderArguments : IArgumentModel
{
    [Operand("provider", Description = "Authentication provider: google.")]
    public AuthProvider Provider { get; set; }
}

[Command("auth", Description = "Manage local authorization; only login initiates browser consent.")]
public sealed class AuthCommands
{
    [Command("login", Description = "Explicitly authorize the selected teacher for Google Calendar and Drive using configured client secrets.")]
    public Task<int> Login(AuthProviderArguments provider, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) => Execute("login", settings, output, cancellationToken);
    [Command("status", Description = "Inspect local teacher/client authorization without contacting Google or loading schedules.")]
    public Task<int> Status(AuthProviderArguments provider, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) => Execute("status", settings, output, cancellationToken);
    [Command("logout", Description = "Delete local authorization for this teacher's accounts. No cloud revocation is performed.")]
    public Task<int> Logout(AuthProviderArguments provider, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken = default) => Execute("logout", settings, output, cancellationToken);

    private static async Task<int> Execute(string verb, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } teacher) return Finish(3, null, ["A teacher --profile is required. Use config profiles to list identities."]);
            using var authentication = new GoogleAuthentication();
            if (verb == "login")
            {
                var credentials = new[] { resolved.Get(GoogleCalendarConfig.Key)?.Credentials, resolved.Get(GoogleDriveConfig.Key)?.Credentials }
                    .Where(x => x is not null).Select(x => x!.Build()).ToArray();
                if (credentials.Length == 0) return Finish(3, null, ["Google client configuration is missing for this profile."]);
                // Only credential binders are registered. No schedule/runtime or unrelated cloud services are created.
                var services = new ServiceCollection();
                services.AddConfigsServices();
                services.AddGlobalConfiguration();
                resolved.ConfigureServices(services);
                services.AddScoped<CurrentUserNameProvider>();
                await using var container = services.BuildServiceProvider();
                await using var scope = container.CreateAsyncScope();
                await authentication.Login(credentials, teacher, scope.ServiceProvider, cancellation.Token);
            }
            else if (verb == "logout") await authentication.Logout(teacher, cancellation.Token);
            return Finish(0, await authentication.Status(teacher, cancellation.Token), []);
        }
        catch (OperationCanceledException) { return Finish(130, null, ["Cancelled."]); }
        catch (AuthenticationRequiredException e) { return Finish(4, null, [e.Message]); }
        catch (LocalOperationBusyException) { return Finish(7, null, ["Another authentication operation owns this teacher's local state."]); }
        catch (JsonException e) { return Finish(3, null, [e.Message]); }
        catch (DirectoryNotFoundException e) { return Finish(3, null, [e.Message]); }
        catch (IOException) { return Finish(5, null, ["Could not read or update local authorization state."]); }
        catch (UnauthorizedAccessException) { return Finish(5, null, ["Local authorization state is inaccessible."]); }
        catch (Exception) { return Finish(1, null, ["Authentication operation failed. Check the configured Google client credentials."]); }
        finally { Console.CancelKeyPress -= cancel; }

        int Finish(int exit, AuthStatus? data, string[] errors)
        {
            foreach (var error in errors) Console.Error.WriteLine(error);
            if (output.Json)
                Console.WriteLine(JsonSerializer.Serialize(new CommandResult<AuthStatus?>(1, "auth " + verb + " google", runId,
                    exit == 0 ? "succeeded" : "failed", exit, [], [], [], errors, data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            else if (data is not null)
            {
                Console.WriteLine($"Google authorization for {data.Profile}: {(data.Accounts.Count == 0 ? "not provisioned" : "provisioned")}");
                foreach (var account in data.Accounts) Console.WriteLine($"{account.AccountId}: {account.State}");
            }
            return exit;
        }
    }
}
