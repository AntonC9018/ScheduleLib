using System.Text.Json;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Curriculum.Download;

namespace ScheduleLib.Cli;

public partial class AuthCommands
{
    protected virtual MicrosoftAuthentication CreateMicrosoftAuthentication() => new();
    protected virtual MicrosoftAuthConfig LoadMicrosoftConfig() => MicrosoftCliConfiguration.Load();

    private async Task<int> ExecuteMicrosoft(string verb, SettingsArguments settings, ResultArguments output, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        AuthStatus? data = null;
        var exit = 1;
        string[] errors = [];
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            using var resolved = await CliSettings.Load(settings, cancellationToken: cancellation.Token);
            if (resolved.Profile is not { } profile) throw new JsonException("A teacher --profile is required. Use config profiles to list identities.");
            var authentication = CreateMicrosoftAuthentication();
            if (verb == "login") await authentication.Login(LoadMicrosoftConfig(), profile, cancellation.Token);
            else if (verb == "logout") await authentication.Logout(profile, cancellation.Token);
            data = await authentication.Status(profile, cancellation.Token);
            exit = 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { exit = 130; errors = ["Cancelled."]; }
        catch (AuthenticationRequiredException e) { exit = 4; errors = [e.Message]; }
        catch (LocalOperationBusyException) { exit = 7; errors = ["Another Microsoft authentication operation owns this profile's local state."]; }
        catch (JsonException e) { exit = 3; errors = [e.Message]; }
        catch (DirectoryNotFoundException e) { exit = 3; errors = [e.Message]; }
        catch (IOException) { exit = 5; errors = ["Could not read or update local Microsoft authorization state."]; }
        catch (UnauthorizedAccessException) { exit = 5; errors = ["Local Microsoft authorization state is inaccessible."]; }
        catch (Exception) { exit = 1; errors = ["Microsoft authentication operation failed unexpectedly."]; }
        finally { Console.CancelKeyPress -= cancel; }
        foreach (var error in errors) Console.Error.WriteLine(error);
        if (output.Json)
            Console.WriteLine(JsonSerializer.Serialize(new CommandResult<AuthStatus?>(1, "auth " + verb + " microsoft", runId,
                exit == 0 ? "succeeded" : exit == 130 ? "cancelled" : "failed", exit, [], [], [], errors, data), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        else if (data is not null)
        {
            Console.WriteLine($"Microsoft authorization for {data.Profile}: {(data.Accounts.Count == 0 ? "not provisioned" : "provisioned")}");
            foreach (var account in data.Accounts) Console.WriteLine($"{account.AccountId}: {account.State}");
        }
        return exit;
    }
}

public static class MicrosoftCliConfiguration
{
    public static MicrosoftAuthConfig Load()
    {
        try { return CredentialHelper.CreateDefaultConfiguration(typeof(Commands).Assembly).GetMicrosoftGraphAuth(); }
        catch (InvalidOperationException) { throw new JsonException("Microsoft TenantId and ClientId must be configured in the existing Microsoft user-secrets section."); }
    }
}
