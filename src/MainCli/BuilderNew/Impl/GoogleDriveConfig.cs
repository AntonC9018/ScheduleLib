using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli.BuilderNew.Impl;

public sealed class GoogleDriveConfig : IConfig<GoogleDriveConfig>
{
    public static LayerConfigKey<GoogleDriveConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleDriveConfig>();
    public string? CredentialsPath { get; set; }
    // user = teacher name
    public string? DriveFolderName { get; set; }
    public bool? SaveCredentials { get; set; }
    public IDriveApiKeysSource? ApiKeysSource { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddOpenHierarchy<IDriveApiKeysSource>();
        services.SetImmutable<ManualDriveApiKeysSource>();
        services.SetImmutable<ConfigurationApiKeysSource>();
        services.RegisterBasicOperationsAndMergers<GoogleDriveConfig>();
        services.AddMapper<GoogleDriveConfigMapper>();
        services.AddConfigProvider(GoogleDriveConfig.Key);
        services.AddConfigProvider(new LayerConfigKey<BuiltGoogleDriveConfig>(Key.Value));
    }
}

public sealed class BuiltGoogleDriveConfig
{
    public required string? CredentialsPath { get; set; }
    public required string DriveFolderName { get; set; }
    public required IDriveApiKeysSource ApiKeysSource { get; set; }

    public bool SaveCredentials => CredentialsPath != null;
}

// TODO: This should be better.
public sealed class GoogleDriveConfigMapper : IConfigMapper<GoogleDriveConfig, BuiltGoogleDriveConfig>
{
    public BuiltGoogleDriveConfig Map(GoogleDriveConfig input)
    {
        string? credentialsPath = null;
        if (input.SaveCredentials is true)
        {
            if (input.CredentialsPath == null)
            {
                throw new InvalidOperationException("No credentials path with SaveCredentials configured");
            }
            credentialsPath = input.CredentialsPath;
        }
        return new()
        {
            ApiKeysSource = input.ApiKeysSource ?? throw new InvalidOperationException("No API keys source configured"),
            CredentialsPath = credentialsPath,
            DriveFolderName = input.DriveFolderName ?? throw new InvalidOperationException("No drive folder configured"),
        };
    }
}

public interface IDriveApiKeysSource
{
    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken);
}

public sealed class ConfigurationApiKeysSource : IDriveApiKeysSource
{
    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        var serviceKey = "Google";
        var secretsResolver = sp.GetRequiredService<DynamicOptionsResolver<ClientSecrets>>();
        var value = secretsResolver.Resolve(serviceKey);
        if (value == null)
        {
            throw new InvalidOperationException("Secrets not contained in IConfiguration");
        }
        return ValueTask.FromResult(value);
    }
}

public sealed class ManualDriveApiKeysSource : IDriveApiKeysSource
{
    public required ClientSecrets Value { get; set; }

    public ValueTask<ClientSecrets> Get(IServiceProvider sp, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult(Value);
    }
}

public sealed class GoogleDriveOptions
{
    public string ApplicationName { get; set; } = "ScheduleLib";
}
