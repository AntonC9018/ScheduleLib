using System.Diagnostics;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Io;
using ScheduleLib.OnlineRegistry;

using HttpMethod = System.Net.Http.HttpMethod;

internal sealed class HttpClientContext : IDisposable
{
    public required HttpClient Client { get; init; }
    public required HttpMessageHandler Handler { get; init; }
    public required MemoryCookieProvider CookieProvider { get; init; }

    public CookieContainer Cookies => CookieProvider.Container;

    public static HttpClientContext Create()
    {
        var cookieProvider = new MemoryCookieProvider();
        var cookieContainer = cookieProvider.Container;

        HttpClientHandler? mainHandler = null;
        DelayHandler? delayHandler = null;
        try
        {
#pragma warning disable CA2000 // Wrong dispose warning.
            mainHandler = new HttpClientHandler();
            mainHandler.CookieContainer = cookieContainer;
            mainHandler.UseCookies = true;
            mainHandler.AllowAutoRedirect = false;

            // It blocks the IP-address if there are too many requests.
            // It would only be possible to guess the limit via trial and error.
            delayHandler = new DelayHandler(
                mainHandler,
                TimeSpan.FromSeconds(0.5f));

#pragma warning restore CA2000

            var handler = delayHandler;

            var httpClient = new HttpClient(handler);
            return new()
            {
                CookieProvider = cookieProvider,
                Client = httpClient,
                Handler = handler,
            };
        }
        catch
        {
            if (delayHandler is not null)
            {
                delayHandler.Dispose();
                throw;
            }
            if (mainHandler is not null)
            {
                mainHandler.Dispose();
                throw;
            }
            throw;
        }
    }

    public void Dispose()
    {
        // Because it might be null
        Client?.Dispose();
        Handler?.Dispose();
    }
}

internal readonly struct RegistryScrapingContext : IDisposable
{
    // Takes ownership of everything.
    public required HttpClientContext Http { get; init; }
    public HttpClient HttpClient => Http.Client;
    public required IBrowsingContext Browser { get; init; }

    public static RegistryScrapingContext Create(
        HttpClientContext http,
        TokenRetrievalContext tokenContext)
    {
        var config = Configuration.Default;

        var authHandler = new AuthHandler(tokenContext);
        var requester = new HttpClientRequester(http.Client, authHandler);
        config = config.With<IRequester>(_ => requester);

        config = config.WithDefaultLoader();
        config = config.With<ICookieProvider>(_ => http.CookieProvider);

        var browsingContext = BrowsingContext.New(config);
        return new()
        {
            Http = http,
            Browser = browsingContext,
        };
    }

    public void Dispose()
    {
        Browser.Dispose();
        Http.Dispose();
    }
}

// Just an abstraction over the token context.
file sealed class AuthHandler
{
    private readonly TokenRetrievalContext _tokenContext;

    public AuthHandler(TokenRetrievalContext tokenContext)
    {
        _tokenContext = tokenContext;
    }

    public Task Authenticate(CancellationToken cancellationToken)
    {
        return _tokenContext.QueryTokenAndSave(cancellationToken: cancellationToken);
    }
}

file sealed class DelayHandler : DelegatingHandler
{
    private readonly TimeSpan _delay;

    public DelayHandler(
        HttpMessageHandler innerHandler,
        TimeSpan delay)
        : base(innerHandler)
    {
        _delay = delay;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Task.Delay(_delay, cancellationToken);
        return await base.SendAsync(request, cancellationToken);
    }
}

file sealed class HttpClientRequester : BaseRequester
{
    private readonly HttpClient _httpClient;
    private readonly AuthHandler _authHandler;

    public HttpClientRequester(
        HttpClient client,
        AuthHandler authHandler)
    {
        _httpClient = client;
        _authHandler = authHandler;
    }

    public override bool SupportsProtocol(string protocol) => true;

    protected override async Task<IResponse?> PerformRequestAsync(Request request, CancellationToken cancel)
    {
        bool failedOnce = false;
        while (true)
        {
            using var httpRequest = ToHttpRequest(request);
            var httpResponse = await _httpClient.SendAsync(
                request: httpRequest,
                completionOption: HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancel);

            bool ShouldAuthenticate()
            {
                if (httpResponse.StatusCode is HttpStatusCode.Unauthorized)
                {
                    return true;
                }
                if (httpRequest.Method == HttpMethod.Get
                    && httpResponse.StatusCode == HttpStatusCode.Redirect)
                {
                    return true;
                }
                return false;
            }

            // if invalid token
            if (ShouldAuthenticate())
            {
                if (failedOnce)
                {
                    throw new InvalidOperationException("Failed to use the password to log in once.");
                }

                await _authHandler.Authenticate(cancel);
                failedOnce = true;
                continue;
            }

            var response = await ToResponse(request.Address, httpResponse, cancel);
            return response;
        }
    }

    private static HttpRequestMessage ToHttpRequest(Request request)
    {
        var method = request.Method switch
        {
            AngleSharp.Io.HttpMethod.Get => System.Net.Http.HttpMethod.Get,
            AngleSharp.Io.HttpMethod.Post => System.Net.Http.HttpMethod.Post,
            AngleSharp.Io.HttpMethod.Put => System.Net.Http.HttpMethod.Put,
            AngleSharp.Io.HttpMethod.Delete => System.Net.Http.HttpMethod.Delete,
            AngleSharp.Io.HttpMethod.Options => System.Net.Http.HttpMethod.Options,
            AngleSharp.Io.HttpMethod.Head => System.Net.Http.HttpMethod.Head,
            AngleSharp.Io.HttpMethod.Trace => System.Net.Http.HttpMethod.Trace,
            AngleSharp.Io.HttpMethod.Connect => System.Net.Http.HttpMethod.Connect,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Method)),
        };

        var ret = new HttpRequestMessage(
            method: method,
            requestUri: new Uri(request.Address.ToString()));
        try
        {
            if (request.Content != null)
            {
                ret.Content = new StreamContent(request.Content);
            }

            foreach (var header in request.Headers)
            {
                bool added = ret.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (added)
                {
                    continue;
                }

                if (ret.Content is { } c)
                {
                    bool addedToContent = c.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    Debug.Assert(addedToContent);
                    continue;
                }

                Debug.Fail("Some header ignored");
            }
        }
        catch
        {
            ret.Dispose();
        }
        return ret;
    }

    private static async Task<DefaultResponse> ToResponse(
        Url requestUrl,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var ret = new DefaultResponse();
        try
        {
            ret.Address = requestUrl;
            ret.Content = stream;
            ret.Headers = response.Headers.ToDictionary(x => x.Key, x => string.Join(", ", x.Value));
            ret.StatusCode = response.StatusCode;
        }
        catch
        {
            ((IDisposable) ret).Dispose();
            throw;
        }
        return ret;
    }
}
