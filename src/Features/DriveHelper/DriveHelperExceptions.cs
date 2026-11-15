using Google.Apis.Download;

namespace ScheduleLib.Theses.Parsing;

public sealed class MissingGoogleApiKeyConfigurationException(string configurationKey)
    : InvalidOperationException($"Expected to find {configurationKey} in the configuration")
{
    public string ConfigurationKey { get; } = configurationKey;
}

public sealed class DriveDownloadFailedException(DownloadStatus status, Exception? innerException)
    : InvalidOperationException($"Failed download (status: {status}).", innerException)
{
    public DownloadStatus Status { get; } = status;
}
