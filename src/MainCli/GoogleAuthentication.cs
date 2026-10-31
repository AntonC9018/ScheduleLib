using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public sealed record AuthAccountStatus(string AccountId, string State, string[] Scopes);

public sealed record AuthStatus(string Provider, string Profile, IReadOnlyList<AuthAccountStatus> Accounts);

/// <summary>The client, account and granted scope set shared by all token operations.</summary>
public readonly record struct GoogleTokenContext(ClientSecrets Secrets, string Teacher, string[] Scopes);

/// <summary>Only Login can call Consent. Fake implementations exercise the boundary without browser/network activity.</summary>
public interface IGoogleTokenProvider
{
    Task<TokenResponse> Consent(
        GoogleTokenContext context,
        CancellationToken token);

    Task<TokenResponse?> Refresh(
        GoogleTokenContext context,
        TokenResponse authorization,
        CancellationToken token);

    UserCredential CreateCredential(
        GoogleTokenContext context,
        TokenResponse authorization,
        IDataStore store);
}

public sealed class GoogleTokenProvider : IGoogleTokenProvider, IDisposable
{
    private readonly List<GoogleAuthorizationCodeFlow> _flows = [];

    public async Task<TokenResponse> Consent(
        GoogleTokenContext context,
        CancellationToken token)
    {
        using var flow = Flow(
            context.Secrets,
            context.Scopes,
            null);
        var credential =
            await new AuthorizationCodeInstalledApp(flow, new LocalServerCodeReceiver()).AuthorizeAsync(
                context.Teacher,
                token);
        return credential.Token;
    }

    public async Task<TokenResponse?> Refresh(
        GoogleTokenContext context,
        TokenResponse authorization,
        CancellationToken token)
    {
        using var flow = Flow(
            context.Secrets,
            context.Scopes,
            null);
        var credential = new UserCredential(
            flow,
            context.Teacher,
            authorization);
        if (!(await credential.RefreshTokenAsync(token)))
        {
            return null;
        }

        return credential.Token;
    }

    public UserCredential CreateCredential(
        GoogleTokenContext context,
        TokenResponse authorization,
        IDataStore store)
    {
        var flow = Flow(
            context.Secrets,
            context.Scopes,
            store);
        lock (_flows)
        {
            _flows.Add(flow);
        }

        return new(
            flow,
            context.Teacher,
            authorization);
    }

    public void Dispose()
    {
        lock (_flows)
        {
            foreach (var flow in _flows)
            {
                flow.Dispose();
            }

            _flows.Clear();
        }
    }

    private static GoogleAuthorizationCodeFlow Flow(
        ClientSecrets secrets,
        string[] scopes,
        IDataStore? store)
    {
        return new(new()
        {
            ClientSecrets = secrets,
            Scopes = scopes,
            DataStore = store,
            Prompt = "consent",
        });
    }
}

/// <summary>User state is partitioned by teacher and OAuth client (a logical account).
/// Status/logout need neither client secrets nor a schedule/provider initialization.</summary>
public sealed class GoogleAuthentication : IGoogleCredentialAuthorization, IDisposable
{
    public static readonly string[] LoginScopes =
    [
        "https://www.googleapis.com/auth/calendar", "https://www.googleapis.com/auth/drive",
        "https://www.googleapis.com/auth/drive.file",
    ];

    private readonly string _root;
    private readonly IGoogleTokenProvider _tokens;
    private readonly ILogger _logger;

    public GoogleAuthentication(
        string? stateDirectory = null,
        IGoogleTokenProvider? tokens = null,
        ILogger? logger = null)
    {
        _logger = logger ?? NullLogger.Instance;
        string CalculatePath1()
        {
            string path1 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return path1;
        }

        _root = stateDirectory
            ?? Path.Combine(
                CalculatePath1(),
                "ScheduleLib",
                "auth",
                "google");
        _tokens = tokens ?? new GoogleTokenProvider();
    }

    public static void Register(IServiceCollection services) =>
        services.AddSingleton<IGoogleCredentialAuthorization>(sp =>
            new GoogleAuthentication(logger: sp.GetService<ILogger<GoogleAuthentication>>()));

    public void Dispose()
    {
        if (_tokens is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private static string Key(string identity)
    {
        byte[] source = Encoding.UTF8.GetBytes(identity);
        byte[] inArray = SHA256.HashData(source);
        string toHexString = Convert.ToHexString(inArray);
        string toLowerInvariant = toHexString.ToLowerInvariant();
        return toLowerInvariant;
    }

    private string TeacherDirectory(string teacher)
    {
        string path1 = _root;
        string path2 = Key(teacher);
        string combine = Path.Combine(path1, path2);
        return combine;
    }

    private string AccountFile(string teacher, string clientId)
    {
        string path1 = TeacherDirectory(teacher);
        string key = Key(clientId);
        string text = $"{key}.json";
        string path2 = text;
        string combine = Path.Combine(path1, path2);
        return combine;
    }

    public GoogleLoginBuilder Login(string teacher, IServiceProvider services)
    {
        return new(
            this,
            teacher,
            services);
    }

    public sealed class GoogleLoginBuilder
    {
        private readonly GoogleAuthentication _authentication;
        private readonly string _teacher;
        private readonly IServiceProvider _services;
        private IReadOnlyList<BuiltGoogleCredentialsConfig>? _configurations;

        internal GoogleLoginBuilder(
            GoogleAuthentication authentication,
            string teacher,
            IServiceProvider services)
        {
            _authentication = authentication;
            _teacher = teacher;
            _services = services;
        }

        public GoogleLoginBuilder WithCredentials(BuiltGoogleCredentialsConfig configuration)
        {
            _configurations = [configuration];
            return this;
        }

        public GoogleLoginBuilder WithCredentials(IReadOnlyList<BuiltGoogleCredentialsConfig> configurations)
        {
            _configurations = configurations;
            return this;
        }

        public Task Execute(CancellationToken token)
        {
            if (_configurations is null)
            {
                throw new InvalidOperationException("Configure Google login credentials before execution.");
            }
            return _authentication.LoginConfigured(
                _configurations,
                _teacher,
                _services,
                token);
        }
    }

    private async Task LoginConfigured(
        IReadOnlyList<BuiltGoogleCredentialsConfig> configurations,
        string teacher,
        IServiceProvider services,
        CancellationToken token)
    {
        var clients = new Dictionary<string, ClientSecrets>(StringComparer.Ordinal);
        foreach (var config in configurations)
        {
            var client = await Secrets(
                config,
                teacher,
                services,
                token);
            clients.TryAdd(client.ClientId, client);
        }
        foreach (var secrets in clients.Values)
        {
            await LoginClient(
                secrets,
                teacher,
                token);
        }
    }

    private async Task LoginClient(
        ClientSecrets secrets,
        string teacher,
        CancellationToken token)
    {
        await using var lease = await LocalFileLock.Acquire(
            Path.Combine(TeacherDirectory(teacher), ".operation.lock"),
            token);
        string path = AccountFile(teacher: teacher, clientId: secrets.ClientId);
        var store = new AtomicTokenStore(path);
        var previous = await store.GetAsync<TokenResponse>(teacher);
        var scopes = LoginScopes.Union(previous?.Scope?.Split(' ') ?? [])
            .ToArray();
        TokenResponse result;
        try
        {
            result = await _tokens.Consent(
                new GoogleTokenContext(
                    secrets,
                    teacher,
                    scopes),
                token);
        }
        catch (TokenResponseException error)
        {
            throw TranslatedFailureLog.Translated(
                _logger,
                new AuthenticationRequiredException(
                    provider: "google",
                    teacher: teacher,
                    innerException: error),
                error.Error?.Error);
        }
        token.ThrowIfCancellationRequested();
        if (!Usable(result, scopes))
        {
            throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
        }

        await store.Write(result, token);
    }

    public async Task<UserCredential> Resolve(
        GoogleCredentialRequest request,
        CancellationToken cancellationToken)
    {
        var config = request.Config;
        var scopes = request.Scopes;
        var teacher = request.Teacher;
        var services = request.Services;
        var secrets = await Secrets(
            config,
            teacher,
            services,
            cancellationToken);
        await using var lease = await LocalFileLock.Acquire(
            Path.Combine(TeacherDirectory(teacher), ".operation.lock"),
            cancellationToken);
        string path = AccountFile(teacher: teacher, clientId: secrets.ClientId);
        var store = new AtomicTokenStore(path);
        var authorization = await store.GetAsync<TokenResponse>(teacher);
        if (authorization is null || !Usable(authorization, scopes))
        {
            throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
        }

        if (authorization.IsStale)
        {
            if (string.IsNullOrEmpty(authorization.RefreshToken))
            {
                throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
            }

            try
            {
                var refreshed = await _tokens.Refresh(
                    new GoogleTokenContext(
                        secrets,
                        teacher,
                        scopes),
                    authorization,
                    cancellationToken);
                if (refreshed is null)
                {
                    throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
                }
                // Google may omit unchanged refresh token/scope in refresh responses.
                refreshed.RefreshToken ??= authorization.RefreshToken;
                refreshed.Scope ??= authorization.Scope;
                if (refreshed.IsStale || !Usable(refreshed, scopes))
                {
                    throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
                }

                authorization = refreshed;
                await store.Write(authorization, cancellationToken);
            }
            catch (TokenResponseException error)
            {
                throw TranslatedFailureLog.Translated(
                    _logger,
                    new AuthenticationRequiredException(
                        provider: "google",
                        teacher: teacher,
                        innerException: error),
                    error.Error?.Error);
            }
        }
        var credentialPath = AccountFile(teacher: teacher, clientId: secrets.ClientId);
        var credentialStore = new AtomicTokenStore(credentialPath, requireExisting: true);
        var tokenContext = new GoogleTokenContext(secrets, teacher, scopes);
        return _tokens.CreateCredential(tokenContext, authorization, credentialStore);
    }

    private static bool Usable(TokenResponse authorization, string[] scopes)
    {
        return !string.IsNullOrEmpty(authorization.AccessToken)
            && !scopes.Except(authorization.Scope?.Split(' ') ?? [])
                .Any();
    }

    private async Task<ClientSecrets> Secrets(
        BuiltGoogleCredentialsConfig config,
        string teacher,
        IServiceProvider services,
        CancellationToken token)
    {
        try
        {
            var result = await config.ApiKeysSource.Get(services, token);

            bool CalculateConditionResult()
            {
                if (string.IsNullOrWhiteSpace(result.ClientId))
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(result.ClientSecret))
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                throw new AuthenticationRequiredException(provider: "google", teacher: teacher);
            }

            return result;
        }
        catch (GoogleApiKeysMissingException error)
        {
            throw TranslatedFailureLog.Translated(
                _logger,
                new AuthenticationRequiredException(
                    provider: "google",
                    teacher: teacher,
                    innerException: error));
        }
    }

    public async Task<AuthStatus> Status(string teacher, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var directory = TeacherDirectory(teacher);
        if (!Directory.Exists(directory))
        {
            return new(
                Provider: "google",
                Profile: teacher,
                []);
        }

        var accounts = new List<AuthAccountStatus>();
        foreach (var path in Directory.EnumerateFiles(path: directory, searchPattern: "*.json")
                     .Order())
        {
            token.ThrowIfCancellationRequested();
            var authorization = await new AtomicTokenStore(path).GetAsync<TokenResponse>(teacher);
            if (authorization is null)
            {
                continue;
            }

            accounts.Add(new(
                AccountId: Path.GetFileNameWithoutExtension(path),
                State: authorization.IsStale
                    ? string.IsNullOrEmpty(authorization.RefreshToken) ? "expired" : "refreshable"
                    : "authorized",
                authorization.Scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []));
        }
        return new(
            Provider: "google",
            Profile: teacher,
            accounts);
    }

    public async Task Logout(string teacher, CancellationToken token)
    {
        var directory = TeacherDirectory(teacher);
        if (!Directory.Exists(directory))
        {
            return;
        }

        await using var lease = await LocalFileLock.Acquire(Path.Combine(directory, ".operation.lock"), token);
        foreach (var path in Directory.EnumerateFiles(path: directory, searchPattern: "*.json"))
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
        if (!File.Exists(path))
        {
            return default!;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return (await JsonSerializer.DeserializeAsync<T>(stream))!;
        }
        catch (FileNotFoundException)
        {
            return default!;
        }
        catch (JsonException)
        {
            throw new IOException("Stored Google authorization is invalid; log out and log in again.");
        }
    }

    public Task StoreAsync<T>(string key, T value)
    {
        return Write(
            value,
            CancellationToken.None,
            preserveOmittedScopes: true);
    }

    public Task Write<T>(T value, CancellationToken token)
    {
        return Write(
            value,
            token,
            preserveOmittedScopes: false);
    }

    private async Task Write<T>(
        T value,
        CancellationToken token,
        bool preserveOmittedScopes)
    {
        string CalculatePath()
        {
            string text = $"{path}.lock";
            string path2 = text;
            return path2;
        }

        await using var lease = await LocalFileLock.Acquire(
            CalculatePath(),
            token,
            wait: true);
        if (requireExisting && !File.Exists(path))
        {
            throw new IOException("Local authorization was logged out; login is required.");
        }
        // SDK refresh responses may omit an unchanged grant. Read and merge under the
        // same account lock as publication, and update the response used by UserCredential too.
        // Explicit grants (including an empty grant) must replace the previous scopes.
        if (preserveOmittedScopes && value is TokenResponse { Scope: null } refreshed)
        {
            refreshed.Scope = (await GetAsync<TokenResponse>(""))?.Scope;
        }

        await AtomicFile.Publish(
            path,
            (stream, ct) =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(((FileStream) stream).Name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                return JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    cancellationToken: ct);
            },
            token);
    }

    public async Task DeleteAsync<T>(string key)
    {
        string CalculatePath()
        {
            string text = $"{path}.lock";
            string path2 = text;
            return path2;
        }

        await using var lease = await LocalFileLock.Acquire(
            CalculatePath(),
            CancellationToken.None,
            wait: true);
        File.Delete(path);
    }

    public Task ClearAsync() => DeleteAsync<TokenResponse>("");
}
