using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using DriveFile = Google.Apis.Drive.v3.Data.File;

namespace ScheduleLib.Cli;

/// <summary>CLI adapter deliberately bypasses the legacy retry/batch helpers. Every mutation response must indicate success.</summary>
public sealed class GoogleDriveSyncProvider : IDriveSyncProvider
{
    private readonly DriveService _service;

    public GoogleDriveSyncProvider(DriveService service)
    {
        _service = service;
        // NumTries does not suppress UserCredential's 401 refresh handler. Keep pre-send
        // refresh, but never let response refresh replace a known rejection or replay a write.
        if (service.HttpClient.MessageHandler.Credential is { } credential)
        {
            service.HttpClient.MessageHandler.Credential = new GoogleSingleAttemptCredential(credential, "drive");
        }
        // No HTTP replay or redirect during mutations.
        service.HttpClient.MessageHandler.NumTries = 1;
        service.HttpClient.MessageHandler.FollowRedirect = false;
    }

    public static async Task<IDriveSyncProvider> Connect(
        IServiceProvider services,
        BuiltGoogleDriveConfig config,
        CancellationToken token)
    {
        var helper = services.GetRequiredService<GoogleApiHelper>();
        var credential = await helper.CredentialResolver.Resolve(
            config.Credentials,
            [DriveService.Scope.DriveFile, DriveService.Scope.Drive],
            token);
        var initializer = helper.CreateServiceInitializer(credential);
        // Disable SDK exponential backoff too: lost create responses must not cause duplicate files.
        initializer.DefaultExponentialBackOffPolicy = Google.Apis.Http.ExponentialBackOffPolicy.None;
        global::Google.Apis.Drive.v3.DriveService service = new DriveService(initializer);
        global::ScheduleLib.Cli.GoogleDriveSyncProvider googleDriveSyncProvider = new GoogleDriveSyncProvider(service);
        return googleDriveSyncProvider;
    }

    public async Task<DriveAccount> GetAccount(CancellationToken token)
    {
        var request = _service.About.Get();
        request.Fields = "user(permissionId,emailAddress)";
        var user = (await request.ExecuteAsync(token)).User;
        if (string.IsNullOrWhiteSpace(user?.PermissionId))
        {
            throw new IOException("Drive did not return an actual account identity.");
        }

        return new(Id: user.PermissionId, Email: user.EmailAddress);
    }

    public async Task<DriveDestination> FindFolder(string name, CancellationToken token)
    {
        DriveFile? folder = null;
        string? page = null;
        do
        {
            var request = _service.Files.List();
            global::Google.Apis.Drive.v3.FilesResource.ListRequest listRequest = request;
            string escape = Escape(name);
            string text = $"mimeType='application/vnd.google-apps.folder' and name='{escape}' and trashed=false";
            listRequest.Q = text;
            request.Fields = "nextPageToken,files(id,name)";
            request.PageSize = 2;
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            foreach (var match in response.Files ?? [])
            {
                if (folder is not null)
                {
                    throw new DuplicateDriveFolderNameException(name);
                }

                folder = match;
            }
            page = response.NextPageToken;
        } while (!string.IsNullOrEmpty(page));
        if (folder is null)
        {
            throw new DriveFolderNotFoundException(name);
        }

        return new(Id: folder.Id, Name: folder.Name);
    }

    public async Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string folderId, CancellationToken token)
    {
        var files = new List<DriveRemoteFile>();
        string? page = null;
        do
        {
            var request = _service.Files.List();
            global::Google.Apis.Drive.v3.FilesResource.ListRequest listRequest = request;
            string escape = Escape(folderId);
            string text = $"'{escape}' in parents and trashed=false";
            listRequest.Q = text;
            request.Fields = "nextPageToken,files(id,name)";
            request.PageSize = 1000;
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            files.AddRange((response.Files ?? []).Select(x => new DriveRemoteFile(Id: x.Id, Name: x.Name)));
            page = response.NextPageToken;
        } while (page is not null);
        return files;
    }

    public async Task Delete(string fileId, CancellationToken token)
    {
        await _service.Files.Delete(fileId)
            .ExecuteAsync(token);
    }

    public Task<string> Create(
        string folderId,
        string name,
        Stream input,
        CancellationToken token)
    {
        return Upload(
            HttpMethod.Post,
            null,
            new DriveFile
            {
                Name = name,
                Parents = [folderId],
            },
            input,
            token);
    }

    public Task<string> Update(
        string fileId,
        Stream input,
        CancellationToken token)
    {
        return Upload(
            HttpMethod.Patch,
            fileId,
            new DriveFile(),
            input,
            token);
    }

    private async Task<string> Upload(
        HttpMethod method,
        string? fileId,
        DriveFile metadata,
        Stream input,
        CancellationToken token)
    {
        // A single multipart request avoids ResumableUpload's separate chunk recovery/retry machinery.
        using var multipart = new MultipartContent("related");
        using var jsonContent = new StringContent(
            _service.Serializer.Serialize(metadata),
            Encoding.UTF8,
            "application/json");
        multipart.Add(jsonContent);
        using var content = new StreamContent(input);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(content);
        string text;
        if (fileId is null)
        {
            text = "";
        }
        else
        {
            string escapeDataString = Uri.EscapeDataString(fileId);
            string text2 = $"/{escapeDataString}";
            text = (string) text2;
        }

        string text3 = $"https://www.googleapis.com/upload/drive/v3/files{(text)}?uploadType=multipart&fields=id";
        var uri = (string) text3;
        using var request = new HttpRequestMessage(method, uri) { Content = multipart };
        using var response = await _service.HttpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            throw new Google.GoogleApiException(serviceName: "drive", message: "Drive rejected the upload.")
            {
                HttpStatusCode = response.StatusCode,
            };
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        bool conditionValue = document.RootElement.TryGetProperty("id", out var id);
        if (!conditionValue)
        {
            throw new IOException("Drive upload completed without a file identity; outcome is uncertain.");
        }
        if (string.IsNullOrWhiteSpace(id.GetString()))
        {
            throw new IOException("Drive upload completed without a file identity; outcome is uncertain.");
        }

        return id.GetString()!;
    }

    private static string Escape(string value)
    {
        return value.Replace(
                "\\",
                "\\\\",
                StringComparison.Ordinal)
            .Replace(
                "'",
                "\\'",
                StringComparison.Ordinal);
    }

    public void Dispose() => _service.Dispose();


    internal sealed class DuplicateDriveFolderNameException : IOException
    {
        internal DuplicateDriveFolderNameException(string folderName)
            : base(
                $"More than one Drive folder is named '{folderName}'. Use a unique folder name before synchronizing.")
            => FolderName = folderName;

        public string FolderName { get; }
    }

    internal sealed class DriveFolderNotFoundException : DirectoryNotFoundException
    {
        internal DriveFolderNotFoundException(string folderName)
            : base($"Drive folder does not exist: {folderName}")
            => FolderName = folderName;

        public string FolderName { get; }
    }
}
