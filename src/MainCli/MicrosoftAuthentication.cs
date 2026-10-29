using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Curriculum.Download;

namespace ScheduleLib.Cli;

public sealed record MicrosoftToken(
    string AccountId,
    string AccessToken,
    DateTimeOffset ExpiresOn,
    string[] Scopes,
    byte[] Cache);

public interface IMicrosoftTokenProvider
{
    Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token);

    Task<MicrosoftToken> Silent(
        MicrosoftAuthConfig config,
        string accountId,
        byte[] cache,
        CancellationToken token);
}

/// <summary>Uses only supported MSAL APIs. Silent acquisition has no interactive fallback.</summary>
public sealed class MicrosoftTokenProvider : IMicrosoftTokenProvider
{
    private static IPublicClientApplication Application(MicrosoftAuthConfig config)
    {
        return PublicClientApplicationBuilder
            .Create(config.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, config.TenantId)
            .WithRedirectUri("http://localhost")
            .Build();
    }

    public async Task<MicrosoftToken> Consent(MicrosoftAuthConfig config, CancellationToken token)
    {
        var app = Application(config);
        var cache = new SupportedCache(app, []);
        var result = await app.AcquireTokenInteractive(CurriculaDownloadTasks.ApiRequiredScopes)
            .WithUseEmbeddedWebView(false)
            .ExecuteAsync(token);
        return ToToken(cache, result);
    }

    public async Task<MicrosoftToken> Silent(
        MicrosoftAuthConfig config,
        string accountId,
        byte[] cache,
        CancellationToken token)
    {
        var app = Application(config);
        var serialized = new SupportedCache(app, cache);
        var account = await app.GetAccountAsync(accountId);
        if (account is null)
        {
            throw new MsalUiRequiredException(
                errorCode: "missing_account",
                errorMessage: "Explicit login is required.");
        }

        var result = await app.AcquireTokenSilent(CurriculaDownloadTasks.ApiRequiredScopes, account)
            .ExecuteAsync(token);
        return ToToken(serialized, result);
    }

    private static MicrosoftToken ToToken(SupportedCache cache, AuthenticationResult result)
    {
        return new(
            AccountId: result.Account.HomeAccountId.Identifier,
            AccessToken: result.AccessToken,
            result.ExpiresOn,
            result.Scopes.ToArray(),
            cache.Bytes);
    }

    private sealed class SupportedCache
    {
        public byte[] Bytes { get; private set; }

        public SupportedCache(IPublicClientApplication app, byte[] initial)
        {
            Bytes = initial;
            app.UserTokenCache.SetBeforeAccess(args =>
            {
                if (Bytes.Length > 0)
                {
                    args.TokenCache.DeserializeMsalV3(Bytes);
                }
            });
            app.UserTokenCache.SetAfterAccess(args =>
            {
                if (args.HasStateChanged)
                {
                    Bytes = args.TokenCache.SerializeMsalV3();
                }
            });
        }
    }
}

/// <summary>Teacher, tenant/client and actual MSAL account partitions live in user state.
/// Hold the profile lease through acquisition and atomic publication, including refresh.</summary>
public sealed class MicrosoftAuthentication(string? stateDirectory = null, IMicrosoftTokenProvider? tokens = null)
{
    private readonly string _root = stateDirectory
        ?? Path.Combine(
            CalculatePath1(),
            "ScheduleLib",
            "auth",
            "microsoft");

    private readonly IMicrosoftTokenProvider _tokens = tokens ?? new MicrosoftTokenProvider();

    private sealed record Binding(
        int SchemaVersion,
        string TenantId,
        string ClientId,
        string AccountId);

    private sealed record CachedAuthorization(
        int SchemaVersion,
        byte[] Cache,
        DateTimeOffset ExpiresOn,
        string[] Scopes);

    private static string Key(string value)
    {
        byte[] source = Encoding.UTF8.GetBytes(value);
        byte[] inArray = SHA256.HashData(source);
        string toHexStringLower = Convert.ToHexStringLower(inArray);
        return toHexStringLower;
    }

    private string ProfileDirectory(string profile)
    {
        string path1 = _root;
        string path2 = Key(profile);
        string combine = Path.Combine(path1, path2);
        return combine;
    }

    private string BindingFile(string profile)
    {
        string path1 = ProfileDirectory(profile);
        string path2 = "account.json";
        string combine = Path.Combine(path1, path2);
        return combine;
    }

    private string TokenFile(string profile, Binding binding)
    {
        string path1 = ProfileDirectory(profile);
        string text = $"{binding.TenantId}\n{binding.ClientId}";
        string value = text;
        string path2 = Key(value);
        string key = Key(binding.AccountId);
        string text2 = $"{key}.json";
        string path3 = text2;
        string combine = Path.Combine(
            path1,
            path2,
            path3);
        return combine;
    }

    private Task<LocalFileLock> Lease(string profile, CancellationToken token)
    {
        string path1 = ProfileDirectory(profile);
        string path2 = ".operation.lock";
        string path = Path.Combine(path1, path2);
        var token2 = token;
        var acquire =
            LocalFileLock.Acquire(
                path,
                token2);
        return acquire;
    }

    public async Task Login(
        MicrosoftAuthConfig config,
        string profile,
        CancellationToken token)
    {
        Validate(config, profile);
        await using var lease = await Lease(profile, token);
        MicrosoftToken authorization;
        try
        {
            authorization = await _tokens.Consent(config, token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        EnsureUsable(authorization, profile);
        var binding = new Binding(
            1,
            TenantId: config.TenantId,
            ClientId: config.ClientId,
            AccountId: authorization.AccountId);

        CachedAuthorization CalculateValue()
        {
            var value = new CachedAuthorization(
                1,
                authorization.Cache,
                authorization.ExpiresOn,
                authorization.Scopes);
            return value;
        }

        await Write(
            TokenFile(profile, binding),
            CalculateValue(),
            token);
        // Publish the binding last: cancelled login leaves the previous authorization selected.
        await Write(
            BindingFile(profile),
            binding,
            token);
    }

    public async Task<string> Resolve(
        MicrosoftAuthConfig config,
        string profile,
        CancellationToken token)
    {
        Validate(config, profile);
        await using var lease = await Lease(profile, token);
        var binding = await Read<Binding>(BindingFile(profile), token);
        if (binding is null)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (binding.SchemaVersion != 1)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (binding.TenantId != config.TenantId)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (binding.ClientId != config.ClientId)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (string.IsNullOrWhiteSpace(binding.AccountId))
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }

        var cached = await Read<CachedAuthorization>(TokenFile(profile, binding), token);
        if (cached is null)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (cached.SchemaVersion != 1)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        if (cached.Cache is not { Length: > 0 })
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }

        MicrosoftToken authorization;
        try
        {
            authorization = await _tokens.Silent(
                config,
                binding.AccountId,
                cached.Cache,
                token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
        EnsureUsable(authorization, profile);
        if (authorization.AccountId != binding.AccountId)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }

        CachedAuthorization CalculateValue()
        {
            var value = new CachedAuthorization(
                1,
                authorization.Cache,
                authorization.ExpiresOn,
                authorization.Scopes);
            return value;
        }

        await Write(
            TokenFile(profile, binding),
            CalculateValue(),
            token);
        return authorization.AccessToken;
    }

    public async Task<AuthStatus> Status(string profile, CancellationToken token)
    {
        await using var lease = await Lease(profile, token);
        var binding = await Read<Binding>(BindingFile(profile), token);
        if (binding is null)
        {
            return new(
                Provider: "microsoft",
                Profile: profile,
                []);
        }

        bool CheckCondition()
        {
            if (binding.SchemaVersion != 1)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(binding.TenantId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(binding.ClientId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(binding.AccountId))
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new IOException("Microsoft authorization binding is invalid; log in explicitly to reprovision it.");
        }

        var cached = await Read<CachedAuthorization>(TokenFile(profile, binding), token);

        static IOException InvalidCache()
        {
            return new(
                "Microsoft authorization cache is invalid; log in explicitly to reprovision it.");
        }

        if (cached is not null)
        {
            if (cached.SchemaVersion != 1)
            {
                throw InvalidCache();
            }
            if (cached.Cache is not { Length: > 0 })
            {
                throw InvalidCache();
            }
            if (cached.Scopes is null)
            {
                throw InvalidCache();
            }
        }

        return new(
            Provider: "microsoft",
            Profile: profile,
            cached is null
                ? []
                :
                [
                    new(
                        AccountId: binding.AccountId,
                        State: cached.ExpiresOn > DateTimeOffset.UtcNow ? "authorized" : "refreshable",
                        cached.Scopes),
                ]);
    }

    public async Task Logout(string profile, CancellationToken token)
    {
        await using var lease = await Lease(profile, token);
        token.ThrowIfCancellationRequested();
        // Delete only this provider/profile's owned state; keep lock files and other teachers.
        foreach (var file in Directory.EnumerateFiles(
                     path: ProfileDirectory(profile),
                     searchPattern: "*.json",
                     SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
    }

    private static void Validate(MicrosoftAuthConfig config, string profile)
    {
        bool CalculateConditionResult()
        {
            if (string.IsNullOrWhiteSpace(config.TenantId))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(config.ClientId))
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
    }

    private static void EnsureUsable(MicrosoftToken authorization, string profile)
    {
        bool CalculateConditionResult()
        {
            if (string.IsNullOrWhiteSpace(authorization.AccessToken))
            {
                return true;
            }
            if (string.IsNullOrWhiteSpace(authorization.AccountId))
            {
                return true;
            }
            if (authorization.Cache.Length == 0)
            {
                return true;
            }
            if (authorization.ExpiresOn <= DateTimeOffset.UtcNow)
            {
                return true;
            }

            if (CurriculaDownloadTasks.ApiRequiredScopes.Except(
                    authorization.Scopes,
                    StringComparer.OrdinalIgnoreCase)
                .Any())
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            throw new AuthenticationRequiredException(provider: "microsoft", teacher: profile);
        }
    }

    private static async Task<T?> Read<T>(string path, CancellationToken token)
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
        }
        catch (JsonException)
        {
            throw new IOException("Microsoft authorization state is invalid; log in explicitly to reprovision it.");
        }
    }

    private static Task Write<T>(
        string path,
        T value,
        CancellationToken token)
    {
        return AtomicFile.Publish(
            path,
            async (stream, ct) =>
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(((FileStream) stream).Name, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }

                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    cancellationToken: ct);
            },
            token);
    }

    private static string CalculatePath1()
    {
        string path12 = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return path12;
    }
}
