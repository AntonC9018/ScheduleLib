using Microsoft.Graph;

namespace ScheduleLib.Curriculum.Download;

public sealed record CurriculaSource(string Owner, string Path);
public sealed record CurriculaFile(string Group, string Name, string Id, string DriveId);

public static class CurriculaDownloadTasks
{
    public static readonly string[] ApiRequiredScopes = ["Files.Read.All", "Files.Read", "User.Read", "User.ReadBasic.All"];
    // Existing source configuration is intentionally retained; this slice does not discover newer academic years.
    public static CurriculaSource ConfiguredSource { get; } = new(Owner: "titu.capcelea@usm.md",
        Path: "Curricula DI anul universitar 2024-2025/Curricula per program studii");

    public static Task PullCurriculaToDisk(MicrosoftAuthConfig config, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Use schedulelib curricula download --profile TEACHER after explicit schedulelib auth login microsoft --profile TEACHER. Legacy implicit Microsoft consent is disabled.");
}

public interface ICurriculaProvider : IDisposable
{
    Task<IReadOnlyList<CurriculaFile>> List(CurriculaSource source, CancellationToken token);
    Task<Stream> Download(CurriculaFile file, CancellationToken token);
}

/// <summary>Read-only Graph adapter. Pagination is followed for both program directories and documents.</summary>
public sealed class GraphCurriculaProvider(GraphServiceClient graph, IDisposable? owner = null) : ICurriculaProvider
{
    public async Task<IReadOnlyList<CurriculaFile>> List(CurriculaSource source, CancellationToken token)
    {
        var root = await graph.Users[source.Owner].Drive.Root.ItemWithPath(source.Path).Request()
            .Select("folder,parentReference,id").GetAsync(token);
        bool CheckCondition()
        {
            if (root.Folder is null)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(root.Id))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(root.ParentReference?.DriveId))
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new CurriculaSourceUnavailableException("The coded curricula source is unavailable or is not a folder; update its owner/path in C# configuration if required.");
        }

        var driveId = root.ParentReference.DriveId;
        var files = new List<CurriculaFile>();
        var groups = await graph.Drives[driveId].Items[root.Id].Children.Request().Select("name,id,folder,file").GetAsync(token);
        while (groups is not null)
        {
            foreach (var group in groups)
            {
                token.ThrowIfCancellationRequested();
                if (group.Folder is null)
                {
                    continue;
                }

                var documents = await graph.Drives[driveId].Items[group.Id].Children.Request().Select("name,id,folder,file").GetAsync(token);
                while (documents is not null)
                {
                    foreach (var document in documents.Where(x => x.File is not null))
                    {
                        global::System.Collections.Generic.List<global::ScheduleLib.Curriculum.Download.CurriculaFile> list = files;
                        global::ScheduleLib.Curriculum.Download.CurriculaFile item = new(Group: group.Name, Name: document.Name, Id: document.Id, DriveId: driveId);
                        list.Add(item);
                    }

                    documents = documents.NextPageRequest is { } nextDocuments ? await nextDocuments.GetAsync(token) : null;
                }
            }
            groups = groups.NextPageRequest is { } nextGroups ? await nextGroups.GetAsync(token) : null;
        }
        return files;
    }
    public Task<Stream> Download(CurriculaFile file, CancellationToken token) =>
        graph.Drives[file.DriveId].Items[file.Id].Content.Request().GetAsync(token);
    public void Dispose() => owner?.Dispose();
}

public sealed class CurriculaSourceUnavailableException(string message) : IOException(message);
