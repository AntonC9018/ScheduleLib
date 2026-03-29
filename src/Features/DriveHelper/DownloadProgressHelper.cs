using Google.Apis.Download;

namespace ScheduleLib.Theses.Parsing;

public static class DownloadProgressHelper
{
    public static void ThrowIfNotComplete(this IDownloadProgress progress)
    {
        if (progress.Status != DownloadStatus.Completed)
        {
            throw new InvalidOperationException("Failed download", progress.Exception);
        }
    }
}
