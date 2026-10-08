using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record AuthAccountStatus(string AccountId, string State, string[] Scopes);
public sealed record AuthStatus(string Provider, string Profile, IReadOnlyList<AuthAccountStatus> Accounts);

/// <summary>Only Login can call Consent. Fake implementations exercise the boundary without browser/network activity.</summary>
public interface IGoogleTokenProvider
{
    Task<TokenResponse> Consent(ClientSecrets secrets, string teacher, string[] scopes, CancellationToken token);
    Task<TokenResponse?> Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token);
    UserCredential CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, IDataStore store);
}

public sealed class GoogleTokenProvider : IGoogleTokenProvider, IDisposable
{
    private readonly List<GoogleAuthorizationCodeFlow> _flows = [];
    public async Task<TokenResponse> Consent(ClientSecrets secrets, string teacher, string[] scopes, CancellationToken token)
    {
        using var flow = Flow(secrets, scopes, null);
        var credential = await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver()).AuthorizeAsync(teacher, token);
        return credential.Token;
    }

    public async Task<TokenResponse?> Refresh(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, CancellationToken token)
    {
        using var flow = Flow(secrets, scopes, null);
        var credential = new UserCredential(flow, teacher, authorization);
        return await credential.RefreshTokenAsync(token) ? credential.Token : null;
    }

    public UserCredential CreateCredential(ClientSecrets secrets, string teacher, string[] scopes, TokenResponse authorization, IDataStore store)
    {
        var flow = Flow(secrets, scopes, store);
        lock (_flows) _flows.Add(flow);
        return new(flow, teacher, authorization);
    }
    public void Dispose()
    {
        lock (_flows)
        {
            foreach (var flow in _flows) flow.Dispose();
            _flows.Clear();
        }
    }

    private static GoogleAuthorizationCodeFlow Flow(ClientSecrets secrets, string[] scopes, IDataStore? store) => new(new()
    {
        ClientSecrets = secrets, Scopes = scopes, DataStore = store, Prompt = "consent",
    });
}

/// <summary>User state is partitioned by teacher and OAuth client (a logical account).
/// Status/logout need neither client secrets nor a schedule/provider initialization.</summary>
public sealed class GoogleAuthentication : IGoogleCredentialAuthorization, IDisposable
{
    public static readonly string[] LoginScopes = [
        "https://www.googleapis.com/auth/calendar", "https://www.googleapis.com/auth/drive", "https://www.googleapis.com/auth/drive.file",
    ];
    private readonly string _root;
    private readonly IGoogleTokenProvider _tokens;
    public GoogleAuthentication(string? stateDirectory = null, IGoogleTokenProvider? tokens = null)
    {
        _root = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "auth", "google");
        _tokens = tokens ?? new GoogleTokenProvider();
    }

    public static void Register(IServiceCollection services) => services.AddSingleton<IGoogleCredentialAuthorization>(_ => new GoogleAuthentication());
    public void Dispose() { if (_tokens is IDisposable disposable) disposable.Dispose(); }
    private static string Key(string identity) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    private string TeacherDirectory(string teacher) => Path.Combine(_root, Key(teacher));
    private string AccountFile(string teacher, string clientId) => Path.Combine(TeacherDirectory(teacher), Key(clientId) + ".json");

    public Task Login(BuiltGoogleCredentialsConfig config, string teacher, IServiceProvider services, CancellationToken token) =>
        Login([config], teacher, services, token);

    public async Task Login(IReadOnlyList<BuiltGoogleCredentialsConfig> configurations, string teacher, IServiceProvider services, CancellationToken token)
    {
        var clients = new Dictionary<string, ClientSecrets>(StringComparer.Ordinal);
        foreach (var config in configurations)
        {
            var client = await Secrets(config, teacher, services, token);
            clients.TryAdd(client.ClientId, client);
        }
        foreach (var secrets in clients.Values) await LoginClient(secrets, teacher, token);
    }

    private async Task LoginClient(ClientSecrets secrets, string teacher, CancellationToken token)
    {
        await using var lease = await LocalFileLock.Acquire(Path.Combine(TeacherDirectory(teacher), ".operation.lock"), token);
        var store = new AtomicTokenStore(AccountFile(teacher, secrets.ClientId));
        var previous = await store.GetAsync<TokenResponse>(teacher);
        var scopes = LoginScopes.Union(previous?.Scope?.Split(' ') ?? []).ToArray();
        TokenResponse result;
        try { result = await _tokens.Consent(secrets, teacher, scopes, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AuthenticationRequiredException("google", teacher); }
        token.ThrowIfCancellationRequested();
        if (!Usable(result, scopes)) throw new AuthenticationRequiredException("google", teacher);
        await store.Write(result, token);
    }

    public async Task<UserCredential> Resolve(BuiltGoogleCredentialsConfig config, string[] scopes, string teacher, IServiceProvider services, CancellationToken cancellationToken)
    {
        var secrets = await Secrets(config, teacher, services, cancellationToken);
        await using var lease = await LocalFileLock.Acquire(Path.Combine(TeacherDirectory(teacher), ".operation.lock"), cancellationToken);
        var store = new AtomicTokenStore(AccountFile(teacher, secrets.ClientId));
        var authorization = await store.GetAsync<TokenResponse>(teacher);
        if (authorization is null || !Usable(authorization, scopes)) throw new AuthenticationRequiredException("google", teacher);
        if (authorization.IsStale)
        {
            if (string.IsNullOrEmpty(authorization.RefreshToken)) throw new AuthenticationRequiredException("google", teacher);
            try
            {
                var refreshed = await _tokens.Refresh(secrets, teacher, scopes, authorization, cancellationToken);
                if (refreshed is null) throw new AuthenticationRequiredException("google", teacher);
                // Google may omit unchanged refresh token/scope in refresh responses.
                refreshed.RefreshToken ??= authorization.RefreshToken;
                refreshed.Scope ??= authorization.Scope;
                if (refreshed.IsStale || !Usable(refreshed, scopes)) throw new AuthenticationRequiredException("google", teacher);
                authorization = refreshed;
                await store.Write(authorization, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { throw; }
            catch (UnauthorizedAccessException) { throw; }
            catch (Exception) { throw new AuthenticationRequiredException("google", teacher); }
        }
        return _tokens.CreateCredential(secrets, teacher, scopes, authorization, new AtomicTokenStore(AccountFile(teacher, secrets.ClientId), requireExisting: true));
    }

    private static bool Usable(TokenResponse authorization, string[] scopes) =>
        !string.IsNullOrEmpty(authorization.AccessToken) && !scopes.Except(authorization.Scope?.Split(' ') ?? []).Any();

    private static async Task<ClientSecrets> Secrets(BuiltGoogleCredentialsConfig config, string teacher, IServiceProvider services, CancellationToken token)
    {
        try
        {
            var result = await config.ApiKeysSource.Get(services, token);
            if (string.IsNullOrWhiteSpace(result.ClientId) || string.IsNullOrWhiteSpace(result.ClientSecret))
                throw new AuthenticationRequiredException("google", teacher);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AuthenticationRequiredException("google", teacher); }
    }

    public async Task<AuthStatus> Status(string teacher, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = TeacherDirectory(teacher);
        if (!Directory.Exists(directory)) return new("google", teacher, []);
        var accounts = new List<AuthAccountStatus>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order())
        {
            token.ThrowIfCancellationRequested();
            var authorization = await new AtomicTokenStore(path).GetAsync<TokenResponse>(teacher);
            if (authorization is null) continue;
            accounts.Add(new(Path.GetFileNameWithoutExtension(path), authorization.IsStale
                ? string.IsNullOrEmpty(authorization.RefreshToken) ? "expired" : "refreshable" : "authorized",
                authorization.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []));
        }
        return new("google", teacher, accounts);
    }

    public async Task Logout(string teacher, CancellationToken token)
    {
        var directory = TeacherDirectory(teacher);
        if (!Directory.Exists(directory)) return;
        await using var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".operation.lock"), token);
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
        {
            token.ThrowIfCancellationRequested();
            await new AtomicTokenStore(path).DeleteAsync<TokenResponse>(teacher);
        }
    }
}

/// <summary>Google SDK refresh writes also use complete-file replacement and a per-account lock.</summary>
public sealed class AtomicTokenStore(string path, bool requireExisting = false) : IDataStore
{
    public async Task<T> GetAsync<T>(string key)
    {
        if (!File.Exists(path)) return default!;
        try
        {
            await using var stream = File.OpenRead(path);
            return (await JsonSerializer.DeserializeAsync<T>(stream))!;
        }
        catch (FileNotFoundException) { return default!; }
        catch (JsonException) { throw new IOException("Stored Google authorization is invalid; log out and log in again."); }
    }
    public Task StoreAsync<T>(string key, T value) => Write(value, CancellationToken.None);
    public async Task Write<T>(T value, CancellationToken token)
    {
        await using var lease = await LocalFileLock.Acquire(path + ".lock", token, wait: true);
        if (requireExisting && !File.Exists(path)) throw new IOException("Local authorization was logged out; login is required.");
        await AtomicFile.Publish(path, (stream, ct) =>
        {
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(((FileStream)stream).Name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return JsonSerializer.SerializeAsync(stream, value, cancellationToken: ct);
        }, token);
    }
    public async Task DeleteAsync<T>(string key)
    {
        await using var lease = await LocalFileLock.Acquire(path + ".lock", CancellationToken.None, wait: true);
        File.Delete(path);
    }
    public Task ClearAsync() => DeleteAsync<TokenResponse>("");
}
