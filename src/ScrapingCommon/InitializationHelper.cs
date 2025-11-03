using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Scraping.Common;

public sealed class ScrapingContextBuilder
{
    private readonly ServiceCollection _services = new();
    private TimeSpan? _delay = null;
    public IConfigurationSection? ConfigurationSection { get; set; } = null;
    public bool ValidateDataAnnotationsOnConfig { get; set; } = true;

    internal static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        IndentSize = 4,
        WriteIndented = true,
    };

    public ScrapingContextBuilder()
    {
        JsonOptions(DefaultJsonOptions);

        _services.AddSingleton(_ =>
        {
            return HttpClientContext.Create(_delay);
        });
        _services.AddSingleton<CookieContainer>(sp =>
        {
            var ctx = sp.GetRequiredService<HttpClientContext>();
            return ctx.Cookies;
        });
        _services.AddSingleton<HttpClient>(sp =>
        {
            var ctx = sp.GetRequiredService<HttpClientContext>();
            return ctx.Client;
        });
        _services.AddSingleton<BrowsingContextProvider>();
        _services.AddLogging();
    }

    public void AddConfig<T>(T? value) where T : class
    {
        if (value != null)
        {
            _services.Replace(ServiceDescriptor.Singleton(value));
            return;
        }
        if (_services.Any(sd => sd.ServiceType == typeof(T)))
        {
            return;
        }
        if (_services.Any(sd => sd.ServiceType == typeof(IOptions<T>)))
        {
            return;
        }

        if (ConfigurationSection is null)
        {
            throw new InvalidOperationException();
        }

        var optsConfig = _services
            .AddOptions<T>();
        optsConfig.Bind(ConfigurationSection);
        if (ValidateDataAnnotationsOnConfig)
        {
            optsConfig.ValidateDataAnnotations();
        }
        _services
            .AddSingleton<T>(static services =>
            {
                // get and validate
                var options = services.GetRequiredService<IOptions<T>>();
                var val = options.Value;
                return val;
            });
    }

    public sealed class TokenAuthBuilder
    {
        private bool _cached = false;
        private bool _password = false;
        private readonly ScrapingContextBuilder _builder;

        public TokenAuthBuilder(ScrapingContextBuilder builder)
        {
            _builder = builder;
            _builder._services.AddSingleton<ITokenRetriever>(sp =>
            {
                var factory = sp.GetRequiredService<Func<ITokenRetriever>>();
                var impl = factory();
                var factories = sp.GetServices<Func<ITokenRetriever, ITokenRetriever>>();
                var ret = impl;
                foreach (var f in factories)
                {
                    ret = f(ret);
                }
                return ret;
            });
        }

        /// <summary>
        /// Makes it call the login by filling up a form on the login page and submitting it.
        /// Should be used when there are additional hidden fields that need to be filled in.
        /// </summary>
        public void PasswordLoginForm(
            Credentials? credentials = null,
            PasswordLoginFormConfig? formConfig = null)
        {
            _builder.AddConfig(credentials);
            _builder.AddConfig(formConfig);
            _builder._services.AddSingleton<PasswordLoginFormTokenRetriever>();
            _builder._services.AddSingleton<Func<ITokenRetriever>>(
                sp => sp.GetRequiredService<PasswordLoginFormTokenRetriever>);
            _password = true;
        }

        /// <summary>
        /// Makes it call the login with a regular POST call.
        /// </summary>
        public void PasswordLoginCall(
            Credentials? credentials = null,
            PasswordLoginFieldNames? fieldNames = null)
        {
            _builder.AddConfig(credentials);
            _builder.AddConfig(fieldNames);
            _builder._services.AddSingleton<PasswordTokenRetriever>();
            _builder._services.AddSingleton<Func<ITokenRetriever>>(
                sp => sp.GetRequiredService<PasswordTokenRetriever>);
            _password = true;
        }

        public void Cache(
            TokensStorageConfig? storageConfig = null)
        {
            _builder.AddConfig(storageConfig);
            _builder._services.AddSingleton<Func<ITokenRetriever, ITokenRetriever>>(sp =>
            {
                // This sucks. It's completely unmaintainable.
                return x => new CachingPasswordTokenRetriever(
                    underlyingRetriever: x,
                    cookieContainer: sp.GetRequiredService<CookieContainer>(),
                    credentials: sp.GetRequiredService<Credentials>(),
                    names: sp.GetRequiredService<TokenNamesConfig>(),
                    storageConfig: sp.GetRequiredService<TokensStorageConfig>(),
                    jsonOptions: sp.GetRequiredService<JsonSerializerOptions>());
            });
            _cached = true;
        }

        internal void Validate()
        {
            if (_cached && !_password)
            {
                throw new NotSupportedException();
            }
            if (!_password)
            {
                throw new NotSupportedException();
            }
        }
    }

    public void TokenAuth(Action<TokenAuthBuilder> f)
    {
        TokenAuth(null, f);
    }

    public void TokenAuth(TokenNamesConfig? names, Action<TokenAuthBuilder> f)
    {
        _services.AddSingleton<IAuthHandler, TokenAuthHandler>();
        AddConfig(names);
        var b = new TokenAuthBuilder(this);
        f(b);
        b.Validate();
    }

    public void Delay(TimeSpan delay)
    {
        _delay = delay;
    }

    public void JsonOptions(JsonSerializerOptions jsonOptions)
    {
        _services.Replace(ServiceDescriptor.Singleton(jsonOptions));
    }

    // Add more stuff here.

    public async Task<ScrapingContext> Build(CancellationToken cancellationToken)
    {
        var sp = _services.BuildServiceProvider();
        try
        {
            var http = sp.GetRequiredService<HttpClientContext>();
            var auth = sp.GetRequiredService<IAuthHandler>();
            var ret = ScrapingContext.Create(http, auth);
            ret.Services = sp;

            var lazyBrowser = sp.GetRequiredService<BrowsingContextProvider>();
            lazyBrowser.Value = ret.Browser;

            // if (auth != null)
            {
                await auth.Authenticate(cancellationToken);
            }

            return ret;
        }
        catch
        {
            await sp.DisposeAsync();
            throw;
        }
    }
}
