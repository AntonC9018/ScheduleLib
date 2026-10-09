using System.Net;
using System.Text;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using ScheduleLib.Cli;
using Xunit;

public sealed class GoogleDriveProviderTests
{
    [Theory]
    [InlineData("create", HttpStatusCode.ServiceUnavailable)]
    [InlineData("update", HttpStatusCode.BadRequest)]
    [InlineData("delete", HttpStatusCode.ServiceUnavailable)]
    [InlineData("create", HttpStatusCode.Unauthorized)]
    public async Task ProductionMutationsSendExactlyOnceAndSurfaceHttpFailure(string action, HttpStatusCode status)
    {
        using var handler = new Transport((request, _) => Task.FromResult(new HttpResponseMessage(status)
        { Content = new StringContent("{\"error\":{\"code\":" + (int)status + ",\"message\":\"Rejected\"}}", Encoding.UTF8, "application/json") }));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("artifact bytes"));
        await Assert.ThrowsAsync<Google.GoogleApiException>(async () =>
        {
            if (action == "delete") await provider.Delete("remote", default);
            else if (action == "create") await provider.Create("folder", "schedule.pdf", input, default);
            else await provider.Update("remote", input, default);
        });
        Assert.Single(handler.Requests);
        Assert.Equal(action == "create" ? "POST" : action == "update" ? "PATCH" : "DELETE", handler.Requests[0].Method);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCreateLostOrCancelledResponseHasNoReplay(bool cancel)
    {
        using var handler = new Transport((_, _) => cancel ? throw new OperationCanceledException() : throw new HttpRequestException("Response lost"));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream([1, 2, 3]);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.Create("folder", "schedule.pdf", input, default));
        else await Assert.ThrowsAnyAsync<HttpRequestException>(() => provider.Create("folder", "schedule.pdf", input, default));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProductionMultipartUploadsIncludeMetadataAndBytesAndRequireReturnedId()
    {
        using var handler = new Transport((_, _) => Task.FromResult(Json("{\"id\":\"created-id\"}")));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("fixture artifact bytes"));
        Assert.Equal("created-id", await provider.Create("folder-id", "schedule.pdf", input, default));
        var sent = Assert.Single(handler.Requests);
        Assert.Contains("uploadType=multipart", sent.Uri);
        Assert.Contains("\"name\":\"schedule.pdf\"", sent.Body);
        Assert.Contains("\"parents\":[\"folder-id\"]", sent.Body);
        Assert.Contains("fixture artifact bytes", sent.Body);
        using var missingHandler = new Transport((_, _) => Task.FromResult(Json("{}")));
        using var missingService = Service(missingHandler);
        using var missing = new GoogleDriveSyncProvider(missingService);
        using var missingInput = new MemoryStream([1]);
        await Assert.ThrowsAsync<IOException>(() => missing.Create("folder", "name", missingInput, default));
    }

    [Fact]
    public async Task ProductionReadsActualIdentityEscapesFolderNamesAndFollowsFilePagination()
    {
        var call = 0;
        using var handler = new Transport((_, _) => Task.FromResult(Json(++call switch
        {
            1 => "{\"user\":{\"permissionId\":\"account-id\",\"emailAddress\":\"actual@example.test\"}}",
            2 => "{\"files\":[{\"id\":\"folder-id\",\"name\":\"Actual folder\"}]}",
            3 => "{\"nextPageToken\":\"next\",\"files\":[{\"id\":\"one\",\"name\":\"a.pdf\"}]}",
            _ => "{\"files\":[{\"id\":\"two\",\"name\":\"b.pdf\"}]}"
        })));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        var account = await provider.GetAccount(default);
        Assert.Equal("account-id", account.Id);
        Assert.Equal("actual@example.test", account.Email);
        Assert.Equal("folder-id", (await provider.FindFolder("O'Brien", default)).Id);
        Assert.Equal(2, (await provider.ListFiles("folder-id", default)).Count);
        Assert.Contains("O\\'Brien", Uri.UnescapeDataString(handler.Requests[1].Uri));
        Assert.Contains("pageToken=next", handler.Requests[3].Uri);
        Assert.All(handler.Requests, x => Assert.Equal("GET", x.Method));
    }

    private static DriveService Service(Transport handler) => new(new BaseClientService.Initializer
    { HttpClientFactory = new Factory(handler), ApplicationName = "fixture" });
    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Factory(Transport handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }
    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(string Method, string Uri, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString(), request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await respond(request, cancellationToken);
        }
    }
}
