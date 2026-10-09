using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
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
            service.HttpClient.MessageHandler.Credential = new SingleAttemptCredential(credential);
        // No HTTP replay or redirect during mutations.
        service.HttpClient.MessageHandler.NumTries = 1;
        service.HttpClient.MessageHandler.FollowRedirect = false;
    }
    public static async Task<IDriveSyncProvider> Connect(IServiceProvider services, BuiltGoogleDriveConfig config, CancellationToken token)
    {
        var helper = services.GetRequiredService<GoogleApiHelper>();
        var credential = await helper.CredentialResolver.Resolve(config.Credentials, [DriveService.Scope.DriveFile, DriveService.Scope.Drive], token);
        var initializer = helper.CreateServiceInitializer(credential);
        // Disable SDK exponential backoff too: lost create responses must not cause duplicate files.
        initializer.DefaultExponentialBackOffPolicy = Google.Apis.Http.ExponentialBackOffPolicy.None;
        return new GoogleDriveSyncProvider(new DriveService(initializer));
    }

    public async Task<DriveAccount> GetAccount(CancellationToken token)
    {
        var request = _service.About.Get();
        request.Fields = "user(permissionId,emailAddress)";
        var user = (await request.ExecuteAsync(token)).User;
        if (string.IsNullOrWhiteSpace(user?.PermissionId)) throw new IOException("Drive did not return an actual account identity.");
        return new(user.PermissionId, user.EmailAddress);
    }

    public async Task<DriveDestination> FindFolder(string name, CancellationToken token)
    {
        var request = _service.Files.List();
        request.Q = $"mimeType='application/vnd.google-apps.folder' and name='{Escape(name)}' and trashed=false";
        request.Fields = "files(id,name)";
        var files = (await request.ExecuteAsync(token)).Files;
        var folder = files?.FirstOrDefault() ?? throw new DirectoryNotFoundException($"Drive folder does not exist: {name}");
        return new(folder.Id, folder.Name);
    }

    public async Task<IReadOnlyList<DriveRemoteFile>> ListFiles(string folderId, CancellationToken token)
    {
        var files = new List<DriveRemoteFile>();
        string? page = null;
        do
        {
            var request = _service.Files.List();
            request.Q = $"'{Escape(folderId)}' in parents and trashed=false";
            request.Fields = "nextPageToken,files(id,name)";
            request.PageSize = 1000;
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            files.AddRange((response.Files ?? []).Select(x => new DriveRemoteFile(x.Id, x.Name)));
            page = response.NextPageToken;
        } while (page is not null);
        return files;
    }

    public async Task Delete(string fileId, CancellationToken token) => await _service.Files.Delete(fileId).ExecuteAsync(token);

    public Task<string> Create(string folderId, string name, Stream input, CancellationToken token)
        => Upload(HttpMethod.Post, null, new DriveFile { Name = name, Parents = [folderId] }, input, token);

    public Task<string> Update(string fileId, Stream input, CancellationToken token)
        => Upload(HttpMethod.Patch, fileId, new DriveFile(), input, token);

    private async Task<string> Upload(HttpMethod method, string? fileId, DriveFile metadata, Stream input, CancellationToken token)
    {
        // A single multipart request avoids ResumableUpload's separate chunk recovery/retry machinery.
        using var multipart = new MultipartContent("related");
        using var jsonContent = new StringContent(_service.Serializer.Serialize(metadata), Encoding.UTF8, "application/json");
        multipart.Add(jsonContent);
        using var content = new StreamContent(input);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(content);
        var uri = "https://www.googleapis.com/upload/drive/v3/files"
            + (fileId is null ? "" : "/" + Uri.EscapeDataString(fileId)) + "?uploadType=multipart&fields=id";
        using var request = new HttpRequestMessage(method, uri) { Content = multipart };
        using var response = await _service.HttpClient.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
            throw new Google.GoogleApiException("drive", "Drive rejected the upload.") { HttpStatusCode = response.StatusCode };
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        if (!document.RootElement.TryGetProperty("id", out var id) || string.IsNullOrWhiteSpace(id.GetString()))
            throw new IOException("Drive upload completed without a file identity; outcome is uncertain.");
        return id.GetString()!;
    }

    private sealed class SingleAttemptCredential(IHttpExecuteInterceptor credential) : IHttpExecuteInterceptor
    {
        public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            try { await credential.InterceptAsync(request, cancellationToken); }
            catch (TokenResponseException)
            {
                // Credential acquisition failed before sending: this action is known not applied.
                throw new Google.GoogleApiException("drive", "Google authorization failed.")
                    { HttpStatusCode = System.Net.HttpStatusCode.Unauthorized };
            }
        }
    }

    private static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
    public void Dispose() => _service.Dispose();
}
