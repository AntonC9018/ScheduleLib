using System.Diagnostics;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Io;
using HttpMethod = System.Net.Http.HttpMethod;

namespace ScheduleLib.Scraping.Common;

public sealed class HttpClientContext : IDisposable
{
    public HttpClientContext(
        MemoryCookieProvider cookieProvider,
        HttpClient client,
        HttpMessageHandler handler)
    {
        CookieProvider = cookieProvider;
        Client = client;
        Handler = handler;
    }

    public readonly HttpClient Client;
    private readonly HttpMessageHandler Handler;
    public readonly MemoryCookieProvider CookieProvider;

    public CookieContainer Cookies => CookieProvider.Container;

    public static HttpClientContext Create(TimeSpan? delay = null)
    {
        var cookieProvider = new MemoryCookieProvider();
        var cookieContainer = cookieProvider.Container;

        HttpMessageHandler? handler = null;
        try
        {
#pragma warning disable CA2000 // Wrong dispose warning.
            var mainHandler = new HttpClientHandler();
            handler = mainHandler;
            mainHandler.CookieContainer = cookieContainer;
            mainHandler.UseCookies = true;
            mainHandler.AllowAutoRedirect = false;

            // It blocks the IP-address if there are too many requests.
            // It would only be possible to guess the limit via trial and error.
            var delayValue = delay ?? TimeSpan.FromSeconds(0.5f);
            if (delayValue != TimeSpan.Zero)
            {
                var delayHandler = new DelayHandler(mainHandler, delayValue);
                handler = delayHandler;
            }

#pragma warning restore CA2000

            var httpClient = new HttpClient(handler);

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36");
            httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");

            return new(
                cookieProvider: cookieProvider,
                client: httpClient,
                handler: handler);
        }
        catch
        {
            handler?.Dispose();
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

public sealed class ScrapingContext : IDisposable
{
    // Takes ownership of everything.
    private readonly HttpClientContext _http;
    public IServiceProvider? BuilderServices { get; set; }

    private ScrapingContext(
        HttpClientContext http,
        IBrowsingContext browser)
    {
        _http = http;
        Browser = browser;
    }

    public HttpClient HttpClient => _http.Client;
    public IBrowsingContext Browser { get; }

    public static ScrapingContext Create(
        HttpClientContext http,
        IAuthHandler authHandler)
    {
        var config = Configuration.Default;

        var requester = new HttpClientRequester(http.Client, authHandler);
        config = config.With<IRequester>(_ => requester);

        config = config.WithDefaultLoader();
        config = config.With<ICookieProvider>(_ => http.CookieProvider);

        var browsingContext = BrowsingContext.New(config);
        return new(http, browsingContext);
    }

    public void Dispose()
    {
        Browser.Dispose();
        if (BuilderServices is IDisposable d)
        {
            d.Dispose();
            return;
        }
        _http.Dispose();
    }
}

public interface IAuthHandler
{
    Task Authenticate(CancellationToken cancellationToken);
}

// Just an abstraction over the token context.
public sealed class TokenAuthHandler : IAuthHandler
{
    private readonly ITokenRetriever _tokenContext;

    public TokenAuthHandler(ITokenRetriever tokenContext)
    {
        _tokenContext = tokenContext;
    }

    public async Task Authenticate(CancellationToken cancellationToken)
    {
        var ok = await _tokenContext.InitializeToken(cancellationToken: cancellationToken);
        if (!ok)
        {
            throw new InvalidOperationException("Failed to retrieve token.");
        }
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

internal sealed class HttpClientRequester : BaseRequester
{
    private readonly HttpClient _httpClient;
    private readonly IAuthHandler _authHandler;

    public HttpClientRequester(
        HttpClient client,
        IAuthHandler authHandler)
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
