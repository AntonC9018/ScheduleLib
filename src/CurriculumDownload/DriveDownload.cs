using System.Diagnostics;
using System.Web;
using Azure.Identity;
using ConvertDocToDocx;
using Microsoft.Graph;
using ScheduleLib.Helper;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Process = System.Diagnostics.Process;

namespace ScheduleLib.Curriculum.Download;

public sealed class ItemRef
{
    public required string Id { get; init; }
    public required string DriveId { get; init; }
}

public static class CurriculaDownloadTasks
{
    public static readonly string[] ApiRequiredScopes = [
        // Querying folder contents and downloading files.
        "Files.Read.All",
        "Files.Read",
        // Querying for the user drive.
        "User.Read",
        "User.ReadBasic.All",
    ];

    public static async Task PullCurriculaToDisk(
        MicrosoftAuthConfig config,
        CancellationToken cancellationToken)
    {
        var credential = await CredentialHelper.CreateMaybeLoadCredential(
            new InteractiveBrowserCredentialOptions
            {
                TenantId = config.TenantId,
                ClientId = config.ClientId,
                TokenCachePersistenceOptions = new()
                {
                },
            },
            () => new(scopes: ApiRequiredScopes),
            cancellationToken);
        await using var x_ = credential.AsDisposable();

        using var httpProvider = DriveCurriculaRequestHelper.CreateHttp();
        var graphClient = new GraphServiceClient(credential, httpProvider: httpProvider);
        {
        }
        await PullCurriculaToDiskImpl(graphClient, cancellationToken);
    }

    private static async Task PullCurriculaToDiskImpl(
        GraphServiceClient graphClient,
        CancellationToken cancellationToken)
    {
        var rootDir = Path.GetFullPath(CurriculumDirectoryHelper.DefaultRootDirName);
        if (!Directory.Exists(rootDir))
        {
            Directory.CreateDirectory(rootDir);
        }

        var rootFolderRef = await graphClient.GetCurrentCurriculaRootFolder(
            cancellationToken: cancellationToken);
        var childrenWithKeys = await graphClient.Children(
            rootFolderRef,
            cancellationToken: cancellationToken);

        foreach (var curriculumRef in childrenWithKeys)
        {
            var directory = Path.Combine(rootDir, curriculumRef.Name);
            Directory.CreateDirectory(directory);

            var children = await graphClient
                .Drives[rootFolderRef.DriveId]
                .Items[curriculumRef.Id]
                .Children
                .Request()
                .Select("name,id")
                .GetAsync(cancellationToken);

            foreach (var file in children)
            {
                var filePath = Path.Combine(directory, file.Name);
                var convertedFilePath = PathHelper.WithExtension(file.Name, ".docx");

                if (File.Exists(convertedFilePath))
                {
                    continue;
                }

                {
                    var fileStream = await graphClient
                        .Drives[rootFolderRef.DriveId]
                        .Items[file.Id]
                        .Content
                        .Request()
                        .GetAsync(cancellationToken);

                    await using var outputFile = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.Write);
                    await fileStream.CopyToAsync(outputFile, cancellationToken: cancellationToken);
                    outputFile.SetLength(outputFile.Position);
                }

                {
                    bool shouldConvertToNewerWord = HasOldWordExtension(file.Name);
                    if (!shouldConvertToNewerWord)
                    {
                        continue;
                    }
                }

                var converted = await DocToDocxConversionHelper.TryConvertFile(
                    inputPath: filePath,
                    outputPath: convertedFilePath,
                    cancellationToken: cancellationToken);
                if (!converted)
                {
                    throw new InvalidOperationException("Conversion to docx failed");
                }
                continue;

                static bool HasOldWordExtension(string name)
                {
                    var extension = Path.GetExtension(name);
                    if (extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    if (extension.Equals(".docx", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    throw new NotSupportedException("Extension not supported");
                }
            }
        }
    }
}

public static class DriveCurriculaRequestHelper
{
    public static async Task<(string Id, string Name)[]> Children(
        this GraphServiceClient graphClient,
        ItemRef rootFolderRef,
        CancellationToken cancellationToken)
    {
        var children = await graphClient
            .Drives[rootFolderRef.DriveId]
            .Items[rootFolderRef.Id]
            .Children
            .Request()
            .Select("name,id")
            .GetAsync(cancellationToken);
        var ret = children
            .Select(child =>
            {
                return (
                    Id: child.Id,
                    Name: child.Name);
            })
            .ToArray();
        return ret;
    }

    public static async Task<ItemRef> GetCurrentCurriculaRootFolder(
        this GraphServiceClient graphClient,
        CancellationToken cancellationToken)
    {
        var result = await graphClient
            .Users["titu.capcelea@usm.md"]
            .Drive
            .Root
            .ItemWithPath("Curricula DI anul universitar 2024-2025/Curricula per program studii")
            .Request()
            .Select("folder,parentReference,id")
            .GetAsync(cancellationToken: cancellationToken);
        var folder = result.Folder;
        var itemId = result.Id;
        var driveId = result.ParentReference.DriveId;
        if (folder is null)
        {
            throw new InvalidOperationException("Folder null");
        }

        return new()
        {
            Id = itemId,
            DriveId = driveId,
        };
    }
#pragma warning disable CS8321 // Local function is declared but never used
    public static async Task<string> FindCurrentCurriculumItemId(
        this GraphServiceClient graphClient,
        CancellationToken cancellationToken)
#pragma warning restore CS8321 // Local function is declared but never used
    {
        var sharedWithMeBuilder = graphClient.Me.Drive.SharedWithMe();
        var sharedItemsResponse = await sharedWithMeBuilder
            .Request()
            .Select("remoteItem")
            .GetAsync(cancellationToken: cancellationToken);
// sharedItems.Where(x => x.Name == "Curricula per program studii")
        var curriculaId = sharedItemsResponse
            .Select(x => x.RemoteItem)
            .Where(x =>
            {
                const string path = "/Curricula DI anul universitar 2024-2025/Curricula per program studii";
                _ = path;

                var url = HttpUtility.UrlDecode(x.WebUrl);

                if (!url.EndsWith(path))
                {
                    return false;
                }
                // The filter odata thing seems bugged
                if (x.Shared.SharedBy.User.Id != "titu.capcelea@usm.md")
                {
                    return false;
                }

                return true;
            })
            .Select(x =>
            {
                return x.Id;
            })
            .Single();
        return curriculaId;
    }

    public static HttpProvider CreateHttp()
    {
        HttpClientHandler? defaultHandler = null;
        try
        {
#pragma warning disable CA2000
            defaultHandler = new HttpClientHandler();
            var handler = defaultHandler;
#pragma warning restore CA2000

            var ret = new HttpProvider(handler, disposeHandler: true, serializer: null);
            return ret;
        }
        catch
        {
            if (defaultHandler != null)
            {
                defaultHandler.Dispose();
            }
            throw;
        }
    }

}
