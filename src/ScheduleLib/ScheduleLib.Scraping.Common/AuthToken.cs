using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AngleSharp;
using AngleSharp.Html.Dom;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

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
    public required string Login { get; init; }
    public required string Password { get; init; }
}

public interface ITokenRetriever
{
    Task<bool> InitializeToken(CancellationToken cancellationToken);
}

public sealed class PasswordTokenRetriever : ITokenRetriever
{
    private readonly Services _services;

    internal struct Services
    {
        public required TokenNamesConfig Names;
        public required PasswordLoginFieldNames FieldNames;
        public required HttpClient HttpClient;
        public required Credentials Credentials;
    }

    public PasswordTokenRetriever(
        HttpClient httpClient,
        Credentials credentials,
        TokenNamesConfig names,
        PasswordLoginFieldNames fieldNames)
    {
        _services = new Services
        {
            FieldNames = fieldNames,
            HttpClient = httpClient,
            Credentials = credentials,
            Names = names,
        };
    }

    public async Task<bool> InitializeToken(CancellationToken cancellationToken)
    {
        return await LogIn(cancellationToken);
    }

    internal async Task<bool> LogIn(CancellationToken cancellationToken)
    {
        var uri = _services.Names.LoginUrl;
        using var content = new FormUrlEncodedContent([
            new(_services.FieldNames.Login, _services.Credentials.Login),
            new(_services.FieldNames.Password, _services.Credentials.Password)]);

        var response = await _services.HttpClient.PostAsync(
            uri,
            content: content,
            cancellationToken: cancellationToken);
        _ = response;
        if (response.StatusCode == HttpStatusCode.Redirect)
        {
            return true;
        }
        return false;
    }
}

public sealed class PasswordLoginFormConfig
{
    public required string LoginName { get; init; }
    public required string PasswordName { get; init; }
    public required bool RequireButtonClick { get; init; }
}

public sealed class BrowsingContextProvider
{
    internal IBrowsingContext? Value { get; set; }

    public IBrowsingContext Get()
    {
        Debug.Assert(Value != null);
        return Value;
    }
}

public sealed class PasswordLoginFormTokenRetriever : ITokenRetriever
{
    private readonly PasswordLoginFormConfig _formConfig;
    private readonly TokenNamesConfig _names;
    private readonly BrowsingContextProvider _browser;
    private readonly Credentials _credentials;
    private readonly ILogger _logger;

    public PasswordLoginFormTokenRetriever(
        BrowsingContextProvider browser,
        PasswordLoginFormConfig formConfig,
        TokenNamesConfig names,
        Credentials credentials,
        ILogger<PasswordLoginFormTokenRetriever> logger)
    {
        _browser = browser;
        _formConfig = formConfig;
        _names = names;
        _credentials = credentials;
        _logger = logger;
    }

    public async Task<bool> InitializeToken(CancellationToken cancellationToken)
    {
        var html = await _browser.Get().OpenAsync(_names.LoginUrl.ToString(), cancellationToken);
        if (GetInput(_formConfig.LoginName) is not { } loginInput)
        {
            return false;
        }
        if (GetInput(_formConfig.PasswordName) is not { } passwordInput)
        {
            return false;
        }

        loginInput.Value = _credentials.Login;
        passwordInput.Value = _credentials.Password;

        var form = loginInput.Form;
        if (form is null)
        {
            _logger.LogError("Login input is not in a form");
            return false;
        }
        if (passwordInput.Form != form)
        {
            _logger.LogError("Login and password inputs are not in the same form");
            return false;
        }

        IDocument response;
        if (_formConfig.RequireButtonClick)
        {
            var button = form.QuerySelector<IHtmlButtonElement>("button[type=submit]");
            if (button is null)
            {
                ErrorNotFoundInPage("button");
                return false;
            }

            response = await button.SubmitAsync();
        }
        else
        {
            response = await form.SubmitAsync();
        }

        // if (response.Url == _names.BaseUrl.ToString())
        // {
        //     return false;
        // }
        _ = response;
        return true;

        IHtmlInputElement? GetInput(string name)
        {
            var ret = html.QuerySelector<IHtmlInputElement>($"""input[name="{name}"]""");
            if (ret is null)
            {
                ErrorNotFoundInPage(name);
            }
            return ret;
        }

        void ErrorNotFoundInPage(string name)
        {
            _logger.LogError("`{Element}` not found in page", name);
        }
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

    public async Task<bool> InitializeToken(CancellationToken cancellationToken)
    {
        if (!_alreadyLoaded)
        {
            if (await MaybeSetCookieFromFile(cancellationToken))
            {
                return true;
            }
        }
        return await RecreateToken(cancellationToken);
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

    public async Task<bool> RecreateToken(CancellationToken cancellationToken)
    {
        if (!await _fields.UnderlyingRetriever.InitializeToken(cancellationToken))
        {
            return false;
        }

        var token = _fields.CookieContainer.FindCookie(_fields.Names);
        Debug.Assert(token != null, "Must be set by token retriever");

        await using var stream = File.Open(_fields.StorageConfig.TokensFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
        if (await TryUpdateExisting())
        {
            return true;
        }
        await CreateNew();
        return true;


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
