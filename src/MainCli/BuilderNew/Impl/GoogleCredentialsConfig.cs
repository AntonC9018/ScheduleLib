using Anton.LayeredConfig;
using AutoConstructor.Attributes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

public sealed class GoogleCredentialsConfig
{
    public string? CredentialsPath { get; set; }
    // user = teacher name
    public bool? SaveCredentials { get; set; }
    public IGoogleApiKeysSource? ApiKeysSource { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddOpenHierarchy<IGoogleApiKeysSource>();
        services.SetImmutable<ManualGoogleApiKeysSource>();
        services.SetImmutable<ConfigurationApiKeysSource>();
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
public sealed partial class ConfigurationApiKeysSource : IGoogleApiKeysSource
{
    private readonly string _serviceKey;

    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var secretsResolver = sp.GetRequiredService<DynamicOptionsResolver<ClientSecrets>>();
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

public sealed class BuiltGoogleCredentialsConfig
{
    public required string? CredentialsPath { get; set; }
    public required IGoogleApiKeysSource ApiKeysSource { get; set; }
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
