using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Config;

public sealed class GoogleDriveConfig : INodeData<GoogleDriveConfig>
{
    public static NodeDataKey<GoogleDriveConfig> Key { get; } = NodeDataKey.Registry.Register<GoogleDriveConfig>();
    public string? DriveFolderName { get; set; }
    public GoogleCredentialsConfig? Credentials { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.RegisterBasicOperationsAndMergers<GoogleDriveConfig>();
        services.AddMapper<GoogleDriveConfigMapper>();
        services.AddConfigProvider(GoogleDriveConfig.Key);
        services.AddConfigProvider(new NodeDataKey<BuiltGoogleDriveConfig>(Key.Value));
    }
}

public sealed class BuiltGoogleDriveConfig
{
    public required BuiltGoogleCredentialsConfig Credentials { get; set; }
    public required string DriveFolderName { get; set; }
}

// TODO: This should be better.
public sealed class GoogleDriveConfigMapper : IDataMapper<GoogleDriveConfig, BuiltGoogleDriveConfig>
{
    public BuiltGoogleDriveConfig Map(GoogleDriveConfig input)
    {
        if (input.Credentials is null)
        {
            throw new InvalidOperationException("No credentials config");
        }
        return new()
        {
            Credentials = input.Credentials.Build(),
            DriveFolderName = input.DriveFolderName ?? throw new InvalidOperationException("No drive folder configured"),
        };
    }
}

public sealed class GoogleDriveOptions
{
    public string ApplicationName { get; set; } = "ScheduleLib";
}
