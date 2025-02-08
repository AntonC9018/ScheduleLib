using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;

namespace ScheduleLib.OnlineRegistry;

internal sealed class TokenCookieModel
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

internal sealed class TokenRetrievalContext
{
    private readonly Fields _fields;

    internal struct Fields
    {
        public required NamesConfig Names;
        public required HttpClient HttpClient;
        public required CookieContainer CookieContainer;
        public required Credentials Credentials;
        public required JsonSerializerOptions JsonOptions;
    }
    internal struct Deps
    {
        public required NamesConfig Names;
        public required HttpClient HttpClient;
        public required CookieContainer CookieContainer;
        public required Credentials Credentials;
        public JsonSerializerOptions? JsonOptions;
    }

    public TokenRetrievalContext(in Deps deps)
    {
        _fields = new Fields
        {
            Names = deps.Names,
            HttpClient = deps.HttpClient,
            CookieContainer = deps.CookieContainer,
            Credentials = deps.Credentials,
            JsonOptions = deps.JsonOptions ?? RegistryTokenHelper.DefaultJsonOptions,
        };
    }

    public async Task InitializeToken(CancellationToken cancellationToken)
    {
        if (await MaybeSetCookieFromFile(cancellationToken))
        {
            return;
        }
        await QueryTokenAndSave(cancellationToken);
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
        if (!File.Exists(_fields.Names.TokensFile))
        {
            return null;
        }

        await using var stream = File.OpenRead(_fields.Names.TokensFile);
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
            File.Delete(_fields.Names.TokensFile);
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
    }

    public async Task QueryTokenAndSave(CancellationToken cancellationToken)
    {
        var success = await LogIn(cancellationToken);
        if (!success)
        {
            throw new InvalidOperationException("Login failed.");
        }

        var cookies = _fields.CookieContainer.GetCookies(_fields.Names.LoginUrl);
        if (cookies[_fields.Names.TokenCookieName] is not { } token)
        {
            throw new InvalidOperationException("Token cookie not found.");
        }

        await using var stream = File.Open(_fields.Names.TokensFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
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

    private async Task<bool> LogIn(CancellationToken cancellationToken)
    {
        Uri uri;
        {
            var b = new UriBuilder(_fields.Names.LoginUrl);
            b.Port = -1;
            var parameters = HttpUtility.ParseQueryString("");
            parameters.Add("UserLogin", _fields.Credentials.Login);
            parameters.Add("UserPassword", _fields.Credentials.Password);
            b.Query = parameters.ToString();
            uri = b.Uri;
        }

        var response = await _fields.HttpClient.PostAsync(
            uri,
            content: null,
            cancellationToken: cancellationToken);
        bool success = response.StatusCode == HttpStatusCode.Redirect;
        return success;
    }

}

internal static class RegistryTokenHelper
{
    public static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        IndentSize = 4,
        WriteIndented = true,
    };
}
