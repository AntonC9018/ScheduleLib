using Google;
using Google.Apis.Drive.v3;
using Google.Apis.Requests;

namespace ScheduleLib.Application.Core;

using File = Google.Apis.Drive.v3.Data.File;

public record struct BasicDriveFile(string Name, FileId Id);
public record struct FileId(string Value);
public record struct FolderId(string Value);

public static class GoogleApiHelper1
{
    // Actually doesn't seem possible to implement without wrapping each call...
    public static async Task ExecuteWithRetryAsync(Func<Task> action)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await action();
            return true;
        });
    }

    public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        const int maxRetries = 5;
        var random = new Random();

        int attempt = 0;

        while (true)
        {
            try
            {
                return await action();
            }
            catch (GoogleApiException ex)
            {
                if (!IsRetryable(ex))
                {
                    throw;
                }
                if (attempt == maxRetries)
                {
                    throw;
                }

                // Exponential backoff with jitter
                const double maxJitter = 1.0;
                const double backoffScalar = 1.0;
                double jitter = random.NextDouble() * maxJitter;
                var backoff = Math.Pow(2, attempt) * backoffScalar;
                var delay = TimeSpan.FromSeconds(jitter + backoff);
                await Task.Delay(delay);

                attempt++;
            }
        }

        bool IsRetryable(GoogleApiException ex)
        {
            if (ex.Error?.Errors == null)
            {
                return false;
            }

            return ex.Error.Errors.Any(e =>
                e.Reason == "rateLimitExceeded"
                || e.Reason == "userRateLimitExceeded"
                || e.Reason == "backendError");
        }
    }

}

public static class DriveApiHelper
{
    private const int MaxBatchSize = 100; // Drive limitation per batch
    private const int MaxPageSize = 1000;

    public record struct BatchDeleteOperation(IEnumerable<Func<Task>> Tasks, int BatchCount);

    private static int CeilDiv(int x, int y) => (x + y - 1) / y;

    public static BatchDeleteOperation ExecuteBatchDeleteAsync(
        DriveService driveService,
        List<BasicDriveFile> fileIdsToDelete,
        CancellationToken cancellationToken)
    {
        var batchCount = CeilDiv(fileIdsToDelete.Count, MaxBatchSize);
        return new(Tasks(), batchCount);

        IEnumerable<Func<Task>> Tasks()
        {
            for (int i = 0; i < fileIdsToDelete.Count; i += MaxBatchSize)
            {
                var chunk = fileIdsToDelete
                    .Skip(i)
                    .Take(MaxBatchSize)
                    .ToList();

                yield return () =>
                {
                    var batch = new BatchRequest(driveService);
                    var callback = new BatchRequest.OnResponse<FilesResource.DeleteRequest>(
                        (content, error, index, message) =>
                        {
                            _ = content;
                            _ = error;
                            _ = index;
                            _ = message;
                            if (error != null)
                            {
                                Console.WriteLine($"Delete failed for file {chunk[index]}");
                            }
                        });

                    foreach (var f in chunk)
                    {
                        var deleteReq = driveService.Files.Delete(f.Id.Value);
                        batch.Queue(deleteReq, callback);
                    }

                    return GoogleApiHelper1.ExecuteWithRetryAsync(() => batch.ExecuteAsync(cancellationToken));
                };
            }
        }
    }

    public static Task<List<BasicDriveFile>> GetFiles(
        this DriveService driveService,
        FolderId folderId,
        CancellationToken cancellationToken)
    {
        return GoogleApiHelper1.ExecuteWithRetryAsync(Impl);

        async Task<List<BasicDriveFile>> Impl()
        {
            List<BasicDriveFile> result = new();
            string? pageToken = null;
            while (true)
            {
                var request = driveService.Files.List();
                request.Q = $"'{folderId.Value}' in parents and trashed = false";
                request.Fields = "nextPageToken, files(id, name)";
                request.PageSize = MaxPageSize;
                request.PageToken = pageToken;

                var response = await request.ExecuteAsync(cancellationToken);
                foreach (var f in response.Files)
                {
                    var basic = new BasicDriveFile(f.Name, new(f.Id));
                    result.Add(basic);
                }

                pageToken = response.NextPageToken;
                if (pageToken == null)
                {
                    return result;
                }
            }
        }
    }

    public static Task<FolderId> FindFolderId(
        this DriveService driveService,
        string name,
        CancellationToken cancellationToken)
    {
        return GoogleApiHelper1.ExecuteWithRetryAsync(Impl);

        async Task<FolderId> Impl()
        {
            var request = driveService.Files.List();
            request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{name}'";
            request.Fields = "files(id)";
            var response = await request.ExecuteAsync(cancellationToken);
            return new(response.Files[0].Id);
        }
    }

    public static Task UploadFile(
        this DriveService driveService,
        Stream inputFile,
        string outputFileName,
        FolderId folderId,
        CancellationToken cancellationToken)
    {
        return GoogleApiHelper1.ExecuteWithRetryAsync(Impl);

        async Task Impl()
        {
            var fileMetadata = new File
            {
                Name = outputFileName,
                Parents = [folderId.Value],
            };

            var request = driveService.Files.Create(fileMetadata, inputFile, "application/octet-stream");
            request.Fields = "id";
            await request.UploadAsync(cancellationToken);
        }
    }

    public static Task UpdateFile(
        this DriveService driveService,
        Stream inputFile,
        FileId fileId,
        CancellationToken cancellationToken)
    {
        return GoogleApiHelper1.ExecuteWithRetryAsync(Impl);

        async Task Impl()
        {
            var request = driveService.Files.Update(null, fileId.Value, inputFile, "application/octet-stream");
            await request.UploadAsync(cancellationToken);
        }
    }
}

