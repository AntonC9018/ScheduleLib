using System.Diagnostics.CodeAnalysis;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Helper;

namespace ScheduleLib.Application.Core;


[AutoConstructor]
public sealed partial class SyncDriveFolderTaskHandler
{
    private readonly IOptions<GoogleDriveOptions> _options;
    private readonly DataProvider<BuiltGoogleDriveConfig> _configProvider;
    private readonly GoogleApiHelper _helper;
    private readonly Microsoft.Extensions.Logging.ILogger _logger;

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

        var credential = await _helper.CredentialResolver.Resolve(config.Credentials, Scopes, p.CancellationToken);
        using var driveService = new DriveService(_helper.CreateServiceInitializer(credential));
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

        using var runner = _helper.RunnerProvider.Create(p.CancellationToken);

        var batchDeleteOperation = DriveApiHelper.ExecuteBatchDeleteAsync(
            driveService,
            cloudFilesToDelete,
            runner.CancellationToken);
        if (batchDeleteOperation.BatchCount != 0)
        {
            _logger.LogInformation("Started file deletion");
        }
        foreach (var deleteTask in batchDeleteOperation.Tasks)
        {
            runner.Add(async c =>
            {
                _ = c;
                await deleteTask();
                // _logger.LogInformation("File deletion complete");
            });
        }
        Stream File(string path)
        {
            var stream = p.FilesProvider.OpenForReading(new(path));
            return stream;
        }
        foreach (var fileName in cloudFilesToCreate)
        {
            runner.Add([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async (cancellationToken) =>
            {
                await using var stream = File(fileName);
                _logger.LogInformation("Uploading file '{FileName}'", fileName);
                await driveService.UploadFile(
                    stream,
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cancellationToken);
                // _logger.LogInformation("Finished upload of file '{FileName}'", fileName);
            });
        }
        foreach (var file in cloudFilesToUpdate)
        {
            runner.Add([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async (cancellationToken) =>
            {
                await using var stream = File(file.Name);
                _logger.LogInformation("Updating file '{FileName}'", file.Name);
                await driveService.UpdateFile(
                    stream,
                    fileId: file.Id,
                    cancellationToken: cancellationToken);
                // _logger.LogInformation("Finished update of file '{FileName}'", file.Name);
            });
        }
        await runner.WhenDone();
    }
}

public interface IFilesProvider
{
    public IEnumerable<RelativeFilePath> GetFilePaths();
    public Stream OpenForReading(RelativeFilePath relativeFile);
}

public sealed class OutputDirectoryFilesProvider : IFilesProvider
{
    private readonly OutputDirectory _directory;

    public OutputDirectoryFilesProvider(OutputDirectory directory)
    {
        _directory = directory;
    }

    public IEnumerable<RelativeFilePath> GetFilePaths()
    {
        return _directory.FilePaths("*", new()
        {
            RecurseSubdirectories = true,
        });
    }

    public Stream OpenForReading(RelativeFilePath relativeFile)
    {
        return _directory.OpenFile(relativeFile.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}
