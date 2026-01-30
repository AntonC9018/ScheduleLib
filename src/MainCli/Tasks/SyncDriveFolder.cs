using System.Diagnostics.CodeAnalysis;
using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core.Config.Impl.Impl;
using ScheduleLib.Application.Core.Helper;

namespace ScheduleLib.Application.Core;


[AutoConstructor]
public sealed partial class SyncDriveFolderTaskHandler
{
    private readonly IOptions<GoogleDriveOptions> _options;
    private readonly ConfigProvider<BuiltGoogleDriveConfig> _configProvider;
    private readonly GoogleApiHelper _helper;

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
        foreach (var deleteTask in batchDeleteOperation.Tasks)
        {
            runner.Add(c =>
            {
                _ = c;
                return deleteTask();
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
                await driveService.UploadFile(
                    stream,
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cancellationToken);
            });
        }
        foreach (var file in cloudFilesToUpdate)
        {
            runner.Add([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async (cancellationToken) =>
            {
                await using var stream = File(file.Name);
                await driveService.UpdateFile(
                    stream,
                    fileId: file.Id,
                    cancellationToken: cancellationToken);
            });
        }
        await runner.WhenDone();
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
