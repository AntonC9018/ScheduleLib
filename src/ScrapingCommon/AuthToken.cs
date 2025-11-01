using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScheduleLib.Scraping.Common;

public sealed class TokenCookieModel
{
    public required string Value { get; set; }
    public DateTime ExpireTime { get; set; }

    public Cookie ToObject(string name)
    {
        var ret = new Cookie(name, Value)
        {
            Expires = ExpireTime,
        };
        return ret;
    }

    public static TokenCookieModel FromObject(Cookie cookie)
    {
        var expireTime = cookie.Expires;
        return new()
        {
            Value = cookie.Value,
            ExpireTime = expireTime,
        };
    }
}

public sealed class TokenNamesConfig
{
    public required string TokenCookieName { get; init; }
    public required Uri BaseUrl { get; init; }
    public required Uri LoginUrl { get; init; }
}

public sealed class PasswordLoginFieldNames
{
    public required string UserName { get; init; }
    public required string UserPassword { get; init; }
}

public interface ITokenRetriever
{
    Task InitializeToken(CancellationToken cancellationToken);
}

public sealed class PasswordTokenRetriever : ITokenRetriever
{
    private readonly Services _services;

    internal struct Services
    {
        public required TokenNamesConfig Names;
        public required PasswordLoginFieldNames FieldNames;
        public required HttpClient HttpClient;
        public required CookieContainer CookieContainer;
        public required Credentials Credentials;
    }

    public PasswordTokenRetriever(
        HttpClient httpClient,
        CookieContainer cookieContainer,
        Credentials credentials,
        TokenNamesConfig names,
        PasswordLoginFieldNames fieldNames)
    {
        _services = new Services
        {
            FieldNames = fieldNames,
            HttpClient = httpClient,
            CookieContainer = cookieContainer,
            Credentials = credentials,
            Names = names,
        };
    }

    public async Task InitializeToken(CancellationToken cancellationToken)
    {
        await LogIn(cancellationToken);

        var token = _services.CookieContainer.FindCookie(_services.Names);
        if (token is null)
        {
            throw new InvalidOperationException("Token cookie not found.");
        }
    }

    internal async Task LogIn(CancellationToken cancellationToken)
    {
        var uri = _services.Names.LoginUrl;
        using var content = new FormUrlEncodedContent([
            new(_services.FieldNames.UserName, _services.Credentials.Login),
            new(_services.FieldNames.UserPassword, _services.Credentials.Password)]);

        var response = await _services.HttpClient.PostAsync(
            uri,
            content: content,
            cancellationToken: cancellationToken);
        _ = response;
    }

}

public static class TokenCookieHelper
{
    public static Cookie? FindCookie(this CookieContainer cookieContainer, TokenNamesConfig names)
    {
        var cookies = cookieContainer.GetCookies(names.LoginUrl);
        if (cookies[names.TokenCookieName] is { } token)
        {
            return token;
        }
        return null;
    }
}

public sealed class TokensStorageConfig
{
    public required string TokensFile { get; init; }
}

public sealed class CachingPasswordTokenRetriever : ITokenRetriever
{
    private readonly Services _fields;
    private bool _alreadyLoaded;

    internal struct Services
    {
        public required ITokenRetriever UnderlyingRetriever;
        public required TokensStorageConfig StorageConfig;
        public required TokenNamesConfig Names;
        public required CookieContainer CookieContainer;
        public required Credentials Credentials;
        public required JsonSerializerOptions JsonOptions;
    }

    public CachingPasswordTokenRetriever(
        ITokenRetriever underlyingRetriever,
        CookieContainer cookieContainer,
        Credentials credentials,
        TokenNamesConfig names,
        TokensStorageConfig storageConfig,
        JsonSerializerOptions? jsonOptions = null)
    {
        _alreadyLoaded = false;
        _fields = new Services
        {
            UnderlyingRetriever = underlyingRetriever,
            StorageConfig = storageConfig,
            CookieContainer = cookieContainer,
            Credentials = credentials,
            JsonOptions = jsonOptions ?? DefaultJsonOptions,
            Names = names,
        };
    }

    internal static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        IndentSize = 4,
        WriteIndented = true,
    };

    public async Task InitializeToken(CancellationToken cancellationToken)
    {
        if (!_alreadyLoaded)
        {
            if (await MaybeSetCookieFromFile(cancellationToken))
            {
                return;
            }
        }
        await RecreateToken(cancellationToken);
    }

    private async Task<bool> MaybeSetCookieFromFile(CancellationToken cancellationToken)
    {
        var token = await LoadToken(cancellationToken);
        if (token is null)
        {
            return false;
        }
        if (token.Expired)
        {
            return false;
        }
        _fields.CookieContainer.Add(_fields.Names.BaseUrl, token);
        return true;
    }

    private async ValueTask<Cookie?> LoadToken(CancellationToken cancellationToken)
    {
        if (!File.Exists(_fields.StorageConfig.TokensFile))
        {
            return null;
        }

        await using var stream = File.OpenRead(_fields.StorageConfig.TokensFile);
        if (stream.Length == 0)
        {
            return null;
        }

        JsonDocument cookies;
        try
        {
            cookies = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken);
            if (cookies == null)
            {
                return null;
            }
        }
        catch (JsonException)
        {
            stream.Close();
            File.Delete(_fields.StorageConfig.TokensFile);
            return null;
        }

        using var cookies_ = cookies;

        var root = cookies.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        if (!root.TryGetProperty(_fields.Credentials.Login, out var token))
        {
            return null;
        }

        try
        {
            var cookie = token.Deserialize<TokenCookieModel>();
            if (cookie is null)
            {
                return null;
            }
            return cookie.ToObject(_fields.Names.TokenCookieName);
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            _alreadyLoaded = true;
        }
    }

    public async Task RecreateToken(CancellationToken cancellationToken)
    {
        await _fields.UnderlyingRetriever.InitializeToken(cancellationToken);

        var token = _fields.CookieContainer.FindCookie(_fields.Names);
        Debug.Assert(token != null, "Must be set by token retriever");

        await using var stream = File.Open(_fields.StorageConfig.TokensFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        if (await TryUpdateExisting())
        {
            return;
        }
        await CreateNew();
        return;


        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async ValueTask<bool> TryUpdateExisting()
        {
            if (stream.Length == 0)
            {
                return false;
            }

            JsonNode? document;
            try
            {
                document = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
            }
            catch (JsonException)
            {
                return false;
            }

            if (document is not JsonObject jobj)
            {
                return false;
            }
            return await Save(jobj);
        }

        async Task<bool> CreateNew()
        {
            var root = new JsonObject();
            return await Save(root);
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<bool> Save(JsonObject root)
        {
            stream.Seek(0, SeekOrigin.Begin);
            var tokenModel = TokenCookieModel.FromObject(token);
            root[_fields.Credentials.Login] = JsonSerializer.SerializeToNode(tokenModel);
            await JsonSerializer.SerializeAsync(
                stream,
                root,
                options: _fields.JsonOptions,
                cancellationToken: cancellationToken);
            stream.SetLength(stream.Position);
            return true;
        }
    }

}
