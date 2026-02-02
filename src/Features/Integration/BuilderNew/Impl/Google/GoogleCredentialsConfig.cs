using System.Net;
using Anton.LayeredConfig;
using Anton.LayeredConfig.Options;
using AutoConstructor.Attributes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Polly;
using Polly.Extensions.Http;
using ScheduleLib.Helper;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace ScheduleLib.Application.Config;

public sealed class GoogleCredentialsConfig
{
    public string? CredentialsPath { get; set; }
    // user = teacher name
    public bool? SaveCredentials { get; set; }
    public IGoogleApiKeysSource? ApiKeysSource { get; set; }

    public static void Register(IServiceCollection services)
    {
        var hierarchy = services.AddOpenHierarchy<IGoogleApiKeysSource>();
        hierarchy
            .AddDerived<ManualGoogleApiKeysSource>()
            .SetImmutable();
        hierarchy
            .AddDerived<MarkedConfigurationApiKeysSource>()
            .SetImmutable();
        hierarchy
            .AddDerived<GlobalConfigurationApiKeysSource>()
            .SetImmutable();

        services.RegisterBasicOperationsAndMergers<GoogleCredentialsConfig>();
    }

    public BuiltGoogleCredentialsConfig Build()
    {
        string? credentialsPath = null;
        if (SaveCredentials is true)
        {
            if (CredentialsPath == null)
            {
                throw new InvalidOperationException("No credentials path with SaveCredentials configured");
            }
            credentialsPath = CredentialsPath;
        }
        return new()
        {
            ApiKeysSource = ApiKeysSource ?? throw new InvalidOperationException("No API keys source configured"),
            CredentialsPath = credentialsPath,
        };
    }
}

public interface IGoogleApiKeysSource
{
    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken);
}

[AutoConstructor]
public sealed partial class GlobalConfigurationApiKeysSource : IGoogleApiKeysSource
{
    private readonly string _serviceKey;

    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var binder = sp.GetRequiredService<DynamicOptionsBinder<ClientSecrets>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var section = config.GetRequiredSection(_serviceKey);
        var ret = binder.Get(section);
        return ValueTask.FromResult(ret);
    }
}

[AutoConstructor]
public sealed partial class MarkedConfigurationApiKeysSource : IGoogleApiKeysSource
{
    private readonly string _serviceKey;

    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var secretsResolver = sp.GetRequiredService<MarkedDynamicOptionsResolver<ClientSecrets>>();
        var value = secretsResolver.Resolve(_serviceKey);
        if (value == null)
        {
            throw new InvalidOperationException("Secrets not contained in IConfiguration");
        }
        return ValueTask.FromResult(value);
    }
}

public sealed class ManualGoogleApiKeysSource : IGoogleApiKeysSource
{
    public required ClientSecrets Value { get; set; }

    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Value);
    }
}

public readonly struct BuiltGoogleCredentialsConfig
{
    public required string? CredentialsPath { get; init; }
    public required IGoogleApiKeysSource ApiKeysSource { get; init; }
}

[AutoConstructor]
public sealed partial class GoogleCredentialResolver
{
    private readonly CurrentUserNameProvider _userNameProvider;
    private readonly IServiceProvider _sp;

    public async Task<UserCredential> Resolve(
        BuiltGoogleCredentialsConfig config,
        string[] scopes,
        CancellationToken cancellationToken)
    {
        var clientSecrets = await config.ApiKeysSource.Get(_sp, cancellationToken);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets: clientSecrets,
            scopes: scopes,
            user: _userNameProvider.Get(),
            taskCancellationToken: CancellationToken.None,
            dataStore: config.CredentialsPath is { } credPath
                ? new FileDataStore(credPath, fullPath: true)
                : null);
        return credential;
    }
}

public sealed class GoogleHttpClientProvider : Google.Apis.Http.HttpClientFactory
{
    private readonly IAsyncPolicy<HttpResponseMessage> _policy = CreatePolicy();

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<GoogleHttpClientProvider>();
    }

    public static IAsyncPolicy<HttpResponseMessage> CreatePolicy()
    {
        // var bulkhead = Policy.BulkheadAsync<HttpResponseMessage>(maxParallelization: 20, maxQueuingActions: 20);
        var retry = HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(ex => ex.StatusCode == HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 20,
                sleepDurationProvider: retryAttempt =>
                {
                    var seconds = Math.Pow(2, retryAttempt);
                    return TimeSpan.FromSeconds(seconds);
                });
        // var policy = Policy.WrapAsync(bulkhead, retry);
        var policy = retry;
        return policy;
    }

    protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args)
    {
        var handler = base.CreateHandler(args);
        return new PolicyHttpMessageHandler(_policy)
        {
            InnerHandler = handler,
        };
    }
}

public sealed class GoogleTaskRunnerProvider
{
    public const string Key = "Google";

    private readonly LimitedTaskRunnerProvider _provider;

    public GoogleTaskRunnerProvider([FromKeyedServices(Key)] LimitedTaskRunnerProvider provider)
    {
        _provider = provider;
    }

    public LimitedTaskRunner Create(CancellationToken cancellationToken)
    {
        return _provider.Create(cancellationToken);
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<GoogleTaskRunnerProvider>();
        services.AddKeyedSingleton(Key, (sp, key) =>
        {
            _ = sp;
            _ = key;
            return new LimitedTaskRunnerProvider(maxConcurrentTasks: 20);
        });
    }
}

[AutoConstructor]
public sealed partial class GoogleApiHelper
{
    public readonly GoogleTaskRunnerProvider RunnerProvider;
    public readonly GoogleCredentialResolver CredentialResolver;
    private readonly GoogleHttpClientProvider _httpClientProvider;

    public BaseClientService.Initializer CreateServiceInitializer(UserCredential credential)
    {
        return new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "Schedule",
            HttpClientFactory = _httpClientProvider,
        };
    }

    public static void Register(IServiceCollection services)
    {
        GoogleHttpClientProvider.Register(services);
        GoogleTaskRunnerProvider.Register(services);
        services.AddScoped<GoogleApiHelper>();
        services.AddScoped<GoogleCredentialResolver>();
    }
}

