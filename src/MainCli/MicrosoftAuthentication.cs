using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Curriculum.Download;

namespace ScheduleLib.Cli;

public sealed record MicrosoftToken(string AccountId, string AccessToken, DateTimeOffset ExpiresOn, string[] Scopes, byte[] Cache);
public interface IMicrosoftTokenProvider
{
    Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token);
    Task<MicrosoftToken> Silent(MicrosoftAuthConfig config, string accountId, byte[] cache, CancellationToken token);
}

/// <summary>Uses only supported MSAL APIs. Silent acquisition has no interactive fallback.</summary>
public sealed class MicrosoftTokenProvider : IMicrosoftTokenProvider
{
    private static IPublicClientApplication Application(MicrosoftAuthConfig config) => PublicClientApplicationBuilder
        .Create(config.ClientId).WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
        .WithRedirectUri("http://localhost").Build();
    public async Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token)
    {
        var app = Application(config);
        var cache = new SupportedCache(app, []);
        var result = await app.AcquireTokenInteractive(CurriculaDownloadTasks.ApiRequiredScopes)
            .WithUseEmbeddedWebView(false).ExecuteAsync(token);
        return ToToken(cache, result);
    }
    public async Task<MicrosoftToken> Silent(MicrosoftAuthConfig config, string accountId, byte[] cache, CancellationToken token)
    {
        var app = Application(config);
        var serialized = new SupportedCache(app, cache);
        var account = await app.GetAccountAsync(accountId);
        if (account is null) throw new MsalUiRequiredException("missing_account", "Explicit login is required.");
        var result = await app.AcquireTokenSilent(CurriculaDownloadTasks.ApiRequiredScopes, account).ExecuteAsync(token);
        return ToToken(serialized, result);
    }
    private static MicrosoftToken ToToken(SupportedCache cache, AuthenticationResult result) =>
        new(result.Account.HomeAccountId.Identifier, result.AccessToken, result.ExpiresOn, result.Scopes.ToArray(), cache.Bytes);
    private sealed class SupportedCache
    {
        public byte[] Bytes { get; private set; }
        public SupportedCache(IPublicClientApplication app, byte[] initial)
        {
            Bytes = initial;
            app.UserTokenCache.SetBeforeAccess(args => { if (Bytes.Length > 0) args.TokenCache.DeserializeMsalV3(Bytes); });
            app.UserTokenCache.SetAfterAccess(args => { if (args.HasStateChanged) Bytes = args.TokenCache.SerializeMsalV3(); });
        }
    }
}

/// <summary>Teacher, tenant/client and actual MSAL account partitions live in user state.
/// Hold the profile lease through acquisition and atomic publication, including refresh.</summary>
public sealed class MicrosoftAuthentication(string? stateDirectory = null, IMicrosoftTokenProvider? tokens = null)
{
    private readonly string _root = stateDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScheduleLib", "auth", "microsoft");
    private readonly IMicrosoftTokenProvider _tokens = tokens ?? new MicrosoftTokenProvider();
    private sealed record Binding(int SchemaVersion, string TenantId, string ClientId, string AccountId);
    private sealed record CachedAuthorization(int SchemaVersion, byte[] Cache, DateTimeOffset ExpiresOn, string[] Scopes);
    private static string Key(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string ProfileDirectory(string profile) => Path.Combine(_root, Key(profile));
    private string BindingFile(string profile) => Path.Combine(ProfileDirectory(profile), "account.json");
    private string TokenFile(string profile, Binding binding) => Path.Combine(ProfileDirectory(profile), Key(binding.TenantId + "\n" + binding.ClientId), Key(binding.AccountId) + ".json");
    private Task<LocalFileLock> Lease(string profile, CancellationToken token) => LocalFileLock.Acquire(Path.Combine(ProfileDirectory(profile), ".operation.lock"), token);

    public async Task Login(MicrosoftAuthConfig config, string profile, CancellationToken token)
    {
        Validate(config, profile);
        await using var lease = await Lease(profile, token);
        MicrosoftToken authorization;
        try { authorization = await _tokens.Consent(config, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AuthenticationRequiredException("microsoft", profile); }
        EnsureUsable(authorization, profile);
        var binding = new Binding(1, config.TenantId, config.ClientId, authorization.AccountId);
        await Write(TokenFile(profile, binding), new CachedAuthorization(1, authorization.Cache, authorization.ExpiresOn, authorization.Scopes), token);
        // Publish the binding last: cancelled login leaves the previous authorization selected.
        await Write(BindingFile(profile), binding, token);
    }

    public async Task<string> Resolve(MicrosoftAuthConfig config, string profile, CancellationToken token)
    {
        Validate(config, profile);
        await using var lease = await Lease(profile, token);
        var binding = await Read<Binding>(BindingFile(profile), token);
        if (binding is null || binding.SchemaVersion != 1 || binding.TenantId != config.TenantId || binding.ClientId != config.ClientId || string.IsNullOrWhiteSpace(binding.AccountId))
            throw new AuthenticationRequiredException("microsoft", profile);
        var cached = await Read<CachedAuthorization>(TokenFile(profile, binding), token);
        if (cached is null || cached.SchemaVersion != 1 || cached.Cache is not { Length: > 0 })
            throw new AuthenticationRequiredException("microsoft", profile);
        MicrosoftToken authorization;
        try { authorization = await _tokens.Silent(config, binding.AccountId, cached.Cache, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AuthenticationRequiredException("microsoft", profile); }
        EnsureUsable(authorization, profile);
        if (authorization.AccountId != binding.AccountId) throw new AuthenticationRequiredException("microsoft", profile);
        await Write(TokenFile(profile, binding), new CachedAuthorization(1, authorization.Cache, authorization.ExpiresOn, authorization.Scopes), token);
        return authorization.AccessToken;
    }

    public async Task<AuthStatus> Status(string profile, CancellationToken token)
    {
        await using var lease = await Lease(profile, token);
        var binding = await Read<Binding>(BindingFile(profile), token);
        if (binding is null) return new("microsoft", profile, []);
        if (binding.SchemaVersion != 1 || string.IsNullOrWhiteSpace(binding.TenantId)
            || string.IsNullOrWhiteSpace(binding.ClientId) || string.IsNullOrWhiteSpace(binding.AccountId))
            throw new IOException("Microsoft authorization binding is invalid; log in explicitly to reprovision it.");
        var cached = await Read<CachedAuthorization>(TokenFile(profile, binding), token);
        if (cached is not null && (cached.SchemaVersion != 1 || cached.Cache is not { Length: > 0 } || cached.Scopes is null))
            throw new IOException("Microsoft authorization cache is invalid; log in explicitly to reprovision it.");
        return new("microsoft", profile, cached is null ? [] : [new(binding.AccountId,
            cached.ExpiresOn > DateTimeOffset.UtcNow ? "authorized" : "refreshable", cached.Scopes)]);
    }

    public async Task Logout(string profile, CancellationToken token)
    {
        await using var lease = await Lease(profile, token);
        token.ThrowIfCancellationRequested();
        // Delete only this provider/profile's owned state; keep lock files and other teachers.
        foreach (var file in Directory.EnumerateFiles(ProfileDirectory(profile), "*.json", SearchOption.AllDirectories)) File.Delete(file);
    }

    private static void Validate(MicrosoftAuthConfig config, string profile)
    {
        if (string.IsNullOrWhiteSpace(config.TenantId) || string.IsNullOrWhiteSpace(config.ClientId))
            throw new AuthenticationRequiredException("microsoft", profile);
    }
    private static void EnsureUsable(MicrosoftToken authorization, string profile)
    {
        if (string.IsNullOrWhiteSpace(authorization.AccessToken) || string.IsNullOrWhiteSpace(authorization.AccountId)
            || authorization.Cache.Length == 0 || authorization.ExpiresOn <= DateTimeOffset.UtcNow
            || CurriculaDownloadTasks.ApiRequiredScopes.Except(authorization.Scopes, StringComparer.OrdinalIgnoreCase).Any())
            throw new AuthenticationRequiredException("microsoft", profile);
    }
    private static async Task<T?> Read<T>(string path, CancellationToken token)
    {
        if (!File.Exists(path)) return default;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
        }
        catch (JsonException) { throw new IOException("Microsoft authorization state is invalid; log in explicitly to reprovision it."); }
    }
    private static Task Write<T>(string path, T value, CancellationToken token) => AtomicFile.Publish(path, async (stream, ct) =>
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(((FileStream)stream).Name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await JsonSerializer.SerializeAsync(stream, value, cancellationToken: ct);
    }, token);
}
