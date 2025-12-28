using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using MainCli.BuilderNew.Impl;
using MainCli.Helper;
using Microsoft.Extensions.Options;
using ScheduleLib.Helper;

namespace MainCli;

[AutoConstructor]
public sealed partial class SyncDriveFolderTaskHandler
{
    private readonly IOptions<GoogleDriveOptions> _options;
    private readonly ConfigProvider<BuiltGoogleDriveConfig> _configProvider;
    private readonly CurrentUserNameProvider _userNameProvider;
    private readonly IServiceProvider _sp;

    public struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required IFilesProvider FilesProvider { get; init; }
    }

    private static string[] Scopes =>
    [
        DriveService.Scope.DriveFile,
        DriveService.Scope.Drive,
    ];
    public async Task Run(RunParams p)
    {
        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("No google drive config found.");
        }
        var clientSecrets = await config.ApiKeysSource.Get(_sp, p.CancellationToken);
        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets: clientSecrets,
            scopes: Scopes,
            user: _userNameProvider.Get(),
            taskCancellationToken: CancellationToken.None,
            dataStore: config.CredentialsPath is { } credPath
                ? new FileDataStore(credPath, fullPath: true)
                : null);

        using var driveService = new DriveService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = _options.Value.ApplicationName,
            });
        _ = driveService;

        var folderId = await driveService.FindFolderId(config.DriveFolderName, p.CancellationToken);
        var files = await driveService.GetFiles(folderId, p.CancellationToken);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var existingLocalFiles = p.FilesProvider
            .GetFilePaths()
            .Select(x => x.Path)
            .ToHashSet(comparer);
        var existingCloudFiles = files
            .Select(x => x.Name)
            .ToHashSet(comparer);
        var cloudFilesToDelete = new List<BasicDriveFile>();
        var cloudFilesToUpdate = new List<BasicDriveFile>();
        var cloudFilesToCreate = new List<string>();
        foreach (var file in files)
        {
            if (existingLocalFiles.Contains(file.Name))
            {
                cloudFilesToUpdate.Add(file);
            }
            else
            {
                cloudFilesToDelete.Add(file);
            }
        }
        foreach (var local in existingLocalFiles)
        {
            if (!existingCloudFiles.Contains(local))
            {
                cloudFilesToCreate.Add(local);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(p.CancellationToken);
        var batchDeleteOperation = DriveApiHelper.ExecuteBatchDeleteAsync(
            driveService,
            cloudFilesToDelete,
            cts.Token);
        var taskBuilder = ArrayBuilder.Create<Task>(
            cloudFilesToCreate.Count
            + cloudFilesToUpdate.Count
            + batchDeleteOperation.BatchCount);
        try
        {
            foreach (var deleteTask in batchDeleteOperation.Tasks)
            {
                taskBuilder.Add(deleteTask);
            }
            Stream File(string path)
            {
                var stream = p.FilesProvider.OpenForReading(new(path));
                return stream;
            }
            foreach (var fileName in cloudFilesToCreate)
            {
                await using var stream = File(fileName);
                var t = driveService.UploadFile(
                    stream,
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            foreach (var file in cloudFilesToUpdate)
            {
                await using var stream = File(file.Name);
                var t = driveService.UpdateFile(
                    stream,
                    fileId: file.Id,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            await Task.WhenAll(taskBuilder.Complete());
        }
        catch (Exception)
        {
            await cts.CancelAsync();
            throw;
        }
    }
}

public interface IFilesProvider
{
    public IEnumerable<FilePath> GetFilePaths();
    public Stream OpenForReading(FilePath file);
}

public sealed class OutputDirectoryFilesProvider : IFilesProvider
{
    private readonly OutputDirectory _directory;

    public OutputDirectoryFilesProvider(OutputDirectory directory)
    {
        _directory = directory;
    }

    public IEnumerable<FilePath> GetFilePaths()
    {
        return _directory.FilePaths("*", new()
        {
            RecurseSubdirectories = true,
        });
    }

    public Stream OpenForReading(FilePath file)
    {
        return _directory.OpenFile(file.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
