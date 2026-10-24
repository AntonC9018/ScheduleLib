using System.Net;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Cli;

public sealed class GoogleDriveProviderTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task AmbiguousFolderStopsPreviewAndApplyBeforeListingOrMutation(bool apply, bool splitPages)
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        var call = 0;
        using var handler = new Transport((request, _) =>
        {
            Assert.Equal(expected: HttpMethod.Get, actual: request.Method);
            if (request.RequestUri!.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
            {
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Json("{\"user\":{\"permissionId\":\"account\"}}");
                    return result;
                }

                return Task.FromResult(CalculateResult());
            }

            string expectedSubstring2 = "mimeType";
            string actualString2 = Uri.UnescapeDataString(request.RequestUri.Query);
            Assert.Contains(expectedSubstring: expectedSubstring2, actualString: actualString2);
            call++;
            string json;
            if (!splitPages)
            {
                json = "{\"files\":[{\"id\":\"one\",\"name\":\"Configured\"},{\"id\":\"two\",\"name\":\"Configured\"}]}";
            }
            else
            {
                string text;
                if (call == 1)
                {
                    text = "{\"nextPageToken\":\"next\",\"files\":[{\"id\":\"one\",\"name\":\"Configured\"}]}";
                }
                else
                {
                    text = "{\"files\":[{\"id\":\"two\",\"name\":\"Configured\"}]}";
                }

                json = text;
            }

            global::System.Net.Http.HttpResponseMessage result2 = Json(
            json);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
            result2);
            return fromResult;
        });
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        try
        {
            await Assert.ThrowsAsync<IOException>(() => DriveSync.Run(
                provider,
                folderName: "Configured",
                [],
                apply,
                default,
                lockRoot: directory));
            int expected2;
            if (splitPages)
            {
                expected2 = 2;
            }
            else
            {
                expected2 = 1;
            }

            int actual2 = call;
            Assert.Equal(expected: expected2, actual: actual2);
            if (splitPages)
            {
                Assert.Contains(expectedSubstring: "pageToken=next", actualString: handler.Requests.Last().Uri);
            }

            Assert.All(handler.Requests, x => Assert.Equal(expected: "GET", actual: x.Method));
            string[] collection = Directory.GetFiles(
                    path: directory,
                    searchPattern: "*.json",
                    SearchOption.AllDirectories);
            global::System.Collections.IEnumerable collection2 = collection;
            Assert.Empty(
                collection2);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderLookupScansRemainingPagesForSingleOrMissingMatch(bool found)
    {
        var call = 0;
        using var handler = new Transport((_, _) =>
        {
            int int32 = ++call;
            string json;
            if (int32 == 1)
            {
                json = "{\"nextPageToken\":\"next\",\"files\":[]}";
            }
            else
            {
                string text;
                if (found)
                {
                    text = "{\"files\":[{\"id\":\"only\",\"name\":\"Configured\"}]}";
                }
                else
                {
                    text = "{\"files\":[]}";
                }

                json = text;
            }

            global::System.Net.Http.HttpResponseMessage result = Json(
            json);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
            result);
            return fromResult;
        });
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        if (found)
        {
            Assert.Equal(expected: "only", actual: (await provider.FindFolder("Configured", default)).Id);
        }
        else
        {
            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => provider.FindFolder("Configured", default));
        }

        Assert.Equal(expected: 2, actual: call);
        Assert.Contains(expectedSubstring: "pageToken=next", actualString: handler.Requests.Last().Uri);
    }

    [Theory]
    [InlineData("create", HttpStatusCode.ServiceUnavailable)]
    [InlineData("update", HttpStatusCode.BadRequest)]
    [InlineData("delete", HttpStatusCode.ServiceUnavailable)]
    [InlineData("create", HttpStatusCode.Unauthorized)]
    public async Task ProductionMutationsSendExactlyOnceAndSurfaceHttpFailure(string action, HttpStatusCode status)
    {
        using var handler = new Transport((request, _) =>
        {
            string CalculateContent()
            {
                string text = $"{("{\"error\":{\"code\":" + (int) status)},\"message\":\"Rejected\"}}}}";
                string content = (string) text;
                return content;
            }

            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(
                                CalculateContent(),
                                Encoding.UTF8,
                                "application/json"),
            });
        });
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        byte[] CalculateBuffer()
        {
            byte[] buffer = Encoding.UTF8.GetBytes("artifact bytes");
            return buffer;
        }

        using var input = new MemoryStream(CalculateBuffer());
        await Assert.ThrowsAsync<Google.GoogleApiException>(async () =>
        {
            if (action == "delete")
            {
                await provider.Delete("remote", default);
            }
            else if (action == "create")
            {
                await provider.Create(
                    folderId: "folder",
                    name: "schedule.pdf",
                    input,
                    default);
            }
            else
            {
                await provider.Update(
                    "remote",
                    input,
                    default);
            }
        });
        Assert.Single(handler.Requests);
        string expected2;
        if (action == "create")
        {
            expected2 = "POST";
        }
        else
        {
            string text2;
            if (action == "update")
            {
                text2 = "PATCH";
            }
            else
            {
                text2 = "DELETE";
            }

            expected2 = text2;
        }

        string actual2 = handler.Requests[0].Method;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionCreateLostOrCancelledResponseHasNoReplay(bool cancel)
    {
        using var handler = new Transport((_, _) =>
            cancel ? throw new OperationCanceledException() : throw new HttpRequestException("Response lost"));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream([1, 2, 3]);
        if (cancel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.Create(
                folderId: "folder",
                name: "schedule.pdf",
                input,
                default));
        }
        else
        {
            await Assert.ThrowsAnyAsync<HttpRequestException>(() => provider.Create(
                folderId: "folder",
                name: "schedule.pdf",
                input,
                default));
        }

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task ProductionMultipartUploadsIncludeMetadataAndBytesAndRequireReturnedId()
    {
        using var handler = new Transport((_, _) =>
        {
            global::System.Net.Http.HttpResponseMessage CalculateResult()
            {
                global::System.Net.Http.HttpResponseMessage result = Json("{\"id\":\"created-id\"}");
                return result;
            }

            return Task.FromResult(CalculateResult());
        });
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        byte[] CalculateBuffer()
        {
            byte[] buffer = Encoding.UTF8.GetBytes("fixture artifact bytes");
            return buffer;
        }

        using var input = new MemoryStream(CalculateBuffer());
        Assert.Equal(
            expected: "created-id",
            actual: await provider.Create(
                folderId: "folder-id",
                name: "schedule.pdf",
                input,
                default));
        var sent = Assert.Single(handler.Requests);
        Assert.Contains(expectedSubstring: "uploadType=multipart", actualString: sent.Uri);
        Assert.Contains(expectedSubstring: "\"name\":\"schedule.pdf\"", actualString: sent.Body);
        Assert.Contains(expectedSubstring: "\"parents\":[\"folder-id\"]", actualString: sent.Body);
        Assert.Contains(expectedSubstring: "fixture artifact bytes", actualString: sent.Body);
        using var missingHandler = new Transport((_, _) =>
        {
            global::System.Net.Http.HttpResponseMessage CalculateResult()
            {
                global::System.Net.Http.HttpResponseMessage result = Json("{}");
                return result;
            }

            return Task.FromResult(CalculateResult());
        });
        using var missingService = Service(missingHandler);
        using var missing = new GoogleDriveSyncProvider(missingService);
        using var missingInput = new MemoryStream([1]);
        await Assert.ThrowsAsync<IOException>(() => missing.Create(
            folderId: "folder",
            name: "name",
            missingInput,
            default));
    }

    [Fact]
    public async Task ProductionReadsActualIdentityEscapesFolderNamesAndFollowsFilePagination()
    {
        var call = 0;
        using var handler = new Transport((_, _) => Task.FromResult(
            Json(
                ++call switch
                {
                    1 => "{\"user\":{\"permissionId\":\"account-id\",\"emailAddress\":\"actual@example.test\"}}",
                    2 => "{\"files\":[{\"id\":\"folder-id\",\"name\":\"Actual folder\"}]}",
                    3 => "{\"nextPageToken\":\"next\",\"files\":[{\"id\":\"one\",\"name\":\"a.pdf\"}]}",
                    _ => "{\"files\":[{\"id\":\"two\",\"name\":\"b.pdf\"}]}",
                })));
        using var service = Service(handler);
        using var provider = new GoogleDriveSyncProvider(service);
        var account = await provider.GetAccount(default);
        Assert.Equal(expected: "account-id", actual: account.Id);
        Assert.Equal(expected: "actual@example.test", actual: account.Email);
        Assert.Equal(expected: "folder-id", actual: (await provider.FindFolder("O'Brien", default)).Id);
        Assert.Equal(expected: 2, actual: (await provider.ListFiles("folder-id", default)).Count);
        string CalculateActualString()
        {
            string actualString2 = Uri.UnescapeDataString(handler.Requests[1].Uri);
            return actualString2;
        }

        Assert.Contains(expectedSubstring: "O\\'Brien", actualString: CalculateActualString());
        Assert.Contains(expectedSubstring: "pageToken=next", actualString: handler.Requests[3].Uri);
        Assert.All(handler.Requests, x => Assert.Equal(expected: "GET", actual: x.Method));
    }

    [Theory]
    [InlineData(
        "read-sent-real-deadline",
        5,
        "failed",
        1,
        0)]
    [InlineData(
        "write-sent-real-deadline",
        6,
        "partial",
        5,
        0)]
    [InlineData(
        "expired-refresh-real-deadline",
        5,
        "failed",
        0,
        1)]
    [InlineData(
        "apply-expired-refresh-real-deadline",
        5,
        "failed",
        4,
        1)]
    [InlineData(
        "apply-after-create-expired-refresh-real-deadline",
        6,
        "partial",
        5,
        1)]
    [InlineData(
        "expired-refresh-real-cancel",
        130,
        "cancelled",
        0,
        -1)]
    [InlineData(
        "apply-expired-refresh-real-cancel",
        130,
        "cancelled",
        4,
        -1)]
    [InlineData(
        "apply-after-create-expired-refresh-real-cancel",
        130,
        "cancelled",
        5,
        -1)]
    [InlineData(
        "expired-refresh-timeout",
        5,
        "failed",
        0,
        3)]
    [InlineData(
        "expired-refresh-connection",
        5,
        "failed",
        0,
        1)]
    [InlineData(
        "expired-refresh-cancel",
        130,
        "cancelled",
        0,
        -1)]
    [InlineData(
        "apply-expired-refresh-timeout",
        5,
        "failed",
        4,
        3)]
    [InlineData(
        "apply-expired-refresh-connection",
        5,
        "failed",
        4,
        1)]
    [InlineData(
        "apply-expired-refresh-cancel",
        130,
        "cancelled",
        4,
        -1)]
    [InlineData(
        "apply-after-create-expired-refresh-timeout",
        6,
        "partial",
        5,
        3)]
    [InlineData(
        "apply-after-create-expired-refresh-connection",
        6,
        "partial",
        5,
        1)]
    [InlineData(
        "apply-after-create-expired-refresh-cancel",
        130,
        "cancelled",
        5,
        -1)]
    [InlineData(
        "preview-401",
        4,
        "failed",
        1,
        0)]
    [InlineData(
        "apply-401",
        4,
        "failed",
        5,
        0)]
    [InlineData(
        "expired",
        4,
        "failed",
        0,
        1)]
    [InlineData(
        "apply-expired",
        4,
        "failed",
        4,
        1)]
    [InlineData(
        "refresh-success",
        0,
        "applied",
        1,
        1)]
    [InlineData(
        "read-cancel",
        130,
        "cancelled",
        1,
        0)]
    [InlineData(
        "write-cancel",
        130,
        "cancelled",
        5,
        0)]
    [InlineData(
        "read-timeout",
        5,
        "failed",
        1,
        0)]
    [InlineData(
        "write-timeout",
        6,
        "partial",
        5,
        0)]
    [InlineData(
        "connection",
        5,
        "failed",
        1,
        0)]
    public async Task ProductionHttpFailuresHaveCorrectCliResults(
        string scenario,
        int exit,
        string status,
        int driveRequests,
        int tokenRequests)
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        var original = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        UserCredential? credential = null;
        using var handler = new Transport(async (request, requestToken) =>
        {
            // The API HttpClient cancels the interceptor's linked token when its deadline
            // expires. Wait on that real token inside OAuth; do not synthesize an exception.
            bool CheckCondition()
            {
                bool conditionResult;
                if (request.RequestUri!.Host == "oauth2.googleapis.com")
                {
                    if (scenario.EndsWith("real-deadline", StringComparison.Ordinal))
                    {
                        conditionResult = true;
                    }
                    else
                    {
                        conditionResult = scenario.EndsWith("real-cancel", StringComparison.Ordinal);
                    }
                }
                else
                {
                    conditionResult = false;
                }

                if (conditionResult)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                if (scenario.EndsWith("real-cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                }

                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("Refresh must be interrupted before the API request is sent.");
            }
            if (request.RequestUri!.Host == "oauth2.googleapis.com"
                && scenario.Contains("expired-refresh", StringComparison.Ordinal))
            {
                if (scenario.EndsWith("connection", StringComparison.Ordinal))
                {
                    throw new HttpRequestException("private-provider-diagnostic");
                }

                if (scenario.EndsWith("cancel", StringComparison.Ordinal))
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                throw new TaskCanceledException("private-provider-diagnostic", new TimeoutException());
            }
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                if (scenario == "refresh-success")
                {
                    return Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
                }

                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                           "{\"error\":\"invalid_grant\",\"error_description\":\"private-provider-diagnostic\"}",
                           Encoding.UTF8,
                           "application/json"),
                };
            }

            bool CheckCondition2()
            {
                bool conditionResult2;
                if (scenario == "read-sent-real-deadline")
                {
                    conditionResult2 = true;
                }
                else
                {
                    if (scenario == "write-sent-real-deadline")
                    {
                        conditionResult2 = request.Method == HttpMethod.Post;
                    }
                    else
                    {
                        conditionResult2 = false;
                    }
                }

                if (conditionResult2)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition2())
            {
                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("The sent API request must time out.");
            }
            bool CheckCondition3()
            {
                bool conditionResult3;
                if (scenario == "read-cancel")
                {
                    conditionResult3 = true;
                }
                else
                {
                    if (scenario == "write-cancel")
                    {
                        conditionResult3 = request.Method == HttpMethod.Post;
                    }
                    else
                    {
                        conditionResult3 = false;
                    }
                }

                if (conditionResult3)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition3())
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }
            bool CheckCondition4()
            {
                bool conditionResult4;
                if (scenario == "read-timeout")
                {
                    conditionResult4 = true;
                }
                else
                {
                    if (scenario == "write-timeout")
                    {
                        conditionResult4 = request.Method == HttpMethod.Post;
                    }
                    else
                    {
                        conditionResult4 = false;
                    }
                }

                if (conditionResult4)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition4())
            {
                throw new TaskCanceledException("private-provider-diagnostic", new TimeoutException());
            }

            if (scenario == "connection")
            {
                throw new HttpRequestException("private-provider-diagnostic");
            }

            bool CheckCondition5()
            {
                bool conditionResult5;
                if (scenario == "preview-401")
                {
                    conditionResult5 = true;
                }
                else
                {
                    if (scenario == "apply-401")
                    {
                        conditionResult5 = request.Method == HttpMethod.Post;
                    }
                    else
                    {
                        conditionResult5 = false;
                    }
                }

                if (conditionResult5)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition5())
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        "{\"error\":{\"code\":401,\"message\":\"rejected\"}}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
            {
                return Json("{\"user\":{\"permissionId\":\"account\"}}");
            }

            if (request.Method == HttpMethod.Post)
            {
                if (scenario.StartsWith("apply-after-create-expired", StringComparison.Ordinal))
                {
                    credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                }

                return Json("{\"id\":\"created\"}");
            }
            var folderQuery = Uri.UnescapeDataString(request.RequestUri.Query)
                .Contains("mimeType", StringComparison.Ordinal);
            if (!folderQuery && scenario.StartsWith("apply-expired", StringComparison.Ordinal))
            {
                credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            }
            // One create then stop on failures; success uses an empty remote/local plan.
            string json;
            if (folderQuery)
            {
                json = "{\"files\":[{\"id\":\"folder\",\"name\":\"Configured\"}]}";
            }
            else
            {
                json = "{\"files\":[]}";
            }

            global::System.Net.Http.HttpResponseMessage json2 = Json(json);
            return json2;
        });
        using var flow = new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = "fixture",
                    ClientSecret = "fixture",
                },
                HttpClientFactory = new Factory(handler),
            });
        credential = new UserCredential(
            flow,
            "fixture",
            new TokenResponse
            {
                AccessToken = "fixture",
                RefreshToken = "fixture",
                ExpiresInSeconds = 3600,
                IssuedUtc = scenario.StartsWith("expired", StringComparison.Ordinal)
                    ? DateTime.UtcNow.AddHours(-2)
                    : DateTime.UtcNow,
            });
        using var service = new DriveService(
            new BaseClientService.Initializer
            {
                HttpClientFactory = new Factory(handler),
                HttpClientInitializer = credential,
                ApplicationName = "fixture",
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
            });
        if (scenario.EndsWith("real-deadline", StringComparison.Ordinal))
        {
            service.HttpClient.Timeout = TimeSpan.FromMilliseconds(200);
        }

        using var provider = new GoogleDriveSyncProvider(service);
        // Successful refresh and mutation are also exercised directly without generating a second bundle.
        if (scenario == "refresh-success")
        {
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var input = new MemoryStream([1]);
            Assert.Equal(
                expected: "created",
                actual: await provider.Create(
                    folderId: "folder",
                    name: "a.pdf",
                    input,
                    default));
            Assert.Equal(expected: driveRequests, actual: handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com"));
            Assert.Equal(expected: tokenRequests, actual: handler.Requests.Count(x => new Uri(x.Uri).Host == "oauth2.googleapis.com"));
            Directory.Delete(directory, true);
            return;
        }
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var actual = await new DriveSyncTests.FixtureDrive(provider).Publish(
                new()
                {
                    NoCache = true,
                    DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                },
                new() { Profile = "Curmanschii Anton" },
                new() { Directory = directory },
                new() { Json = true },
                new()
                {
                    Apply = scenario.StartsWith("apply", StringComparison.Ordinal)
                        || scenario.StartsWith("write-", StringComparison.Ordinal),
                },
                cancellation.Token);
            Assert.Equal(expected: exit, actual: actual);
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(expected: status, actual: json.RootElement.GetProperty("status").GetString());
            if (scenario.StartsWith("apply", StringComparison.Ordinal)
                || scenario.StartsWith("write-", StringComparison.Ordinal))
            {
                var outcomes = json.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
                var completed = scenario.StartsWith("apply-after-create-expired", StringComparison.Ordinal) ? 1 : 0;
                Assert.All(
                    outcomes.Take(completed),
                    x => Assert.Equal(expected: "completed", actual: x.GetProperty("state").GetString()));
                bool CheckCondition6()
                {
                    if (scenario.StartsWith("write-", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (scenario.EndsWith("refresh-cancel", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (scenario.EndsWith("real-cancel", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return false;
                }

                Assert.Equal(
                                    expected: CheckCondition6()
                                        ? "uncertain"
                                        : "failed",
                                    actual: outcomes[completed].GetProperty("state").GetString());
                Assert.All(
                    outcomes.Skip(completed + 1),
                    x => Assert.Equal(expected: "not-attempted", actual: x.GetProperty("state").GetString()));
            }
            if (scenario.Contains("expired-refresh", StringComparison.Ordinal))
            {
                Assert.DoesNotContain(expectedSubstring: "auth login google", actualString: stdout.ToString() + stderr);
                Assert.Equal(expected: "fixture", actual: credential.Token.RefreshToken);
            }
            if (exit == 4)
            {
                Assert.Contains(expectedSubstring: "auth login google", actualString: stderr.ToString());
            }

            Assert.DoesNotContain(expectedSubstring: "private-provider-diagnostic", actualString: stdout.ToString() + stderr);
            Assert.Equal(expected: driveRequests, actual: handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com"));
            if (scenario.Contains("real-", StringComparison.Ordinal))
            {
                bool condition;
                if (scenario.StartsWith("apply-after-create", StringComparison.Ordinal))
                {
                    condition = true;
                }
                else
                {
                    condition = scenario == "write-sent-real-deadline";
                }

                int expected2;
                if (condition)
                {
                    expected2 = 1;
                }
                else
                {
                    expected2 = 0;
                }

                int actual2 = handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com" && x.Method != "GET");
                Assert.Equal(
                    expected: expected2, actual: actual2);
            }

            var refreshes = handler.Requests.Count(x => new Uri(x.Uri).Host == "oauth2.googleapis.com");
            // SDK refresh cancellation races its timeout retry loop; API sends remain exact.
            if (tokenRequests == -1)
            {
                Assert.InRange(
                    actual: refreshes,
                    low: 1,
                    high: 3);
            }
            else
            {
                Assert.Equal(expected: tokenRequests, actual: refreshes);
            }
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("Unrelated credential invariant.")]
    [InlineData("The access token has expired and could not be refreshed. Errors: timeout, timeout, timeout")]
    public async Task UnrelatedCredentialInvalidOperationIsNotAuthenticationFailure(string message)
    {
        using var handler = new Transport((_, _) => throw new Xunit.Sdk.XunitException("Request must not be sent."));
        using var service = Service(handler);
        service.HttpClient.MessageHandler.Credential = new BrokenCredential(message);
        using var provider = new GoogleDriveSyncProvider(service);
        using var input = new MemoryStream([1]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.Create(
            folderId: "folder",
            name: "a.pdf",
            input,
            default));
        Assert.Empty(handler.Requests);
    }

    private sealed class BrokenCredential(string message) : IHttpExecuteInterceptor
    {
        public Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException(message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://www.googleapis.com/auth/drive")]
    [InlineData("")]
    public async Task SdkRefreshPersistsOmittedScopesAndHonorsExplicitGrants(string? responseScope)
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        using var handler = new Transport((request, _) =>
        {
            string json;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                string text;
                if (responseScope is null)
                {
                    text = "";
                }
                else
                {
                    string text2 = $",\"scope\":\"{responseScope}\"";
                    text = (string) text2;
                }

                string text3 = $"{{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"{(text)}}}";
                json = (string) text3;
            }
            else
            {
                json = "{\"id\":\"created\"}";
            }

            global::System.Net.Http.HttpResponseMessage result = Json(
            json);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
            result);
            return fromResult;
        });
        using var tokens = new SdkTokens(handler);
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            await auth.Login(
                AuthConfig,
                "fixture",
                services,
                default);
            var credential = await auth.Resolve(
                AuthConfig,
                GoogleAuthentication.LoginScopes,
                "fixture",
                services,
                default);
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = CredentialService(handler, credential);
            using var provider = new GoogleDriveSyncProvider(service);
            using var input = new MemoryStream([1]);
            Assert.Equal(
                expected: "created",
                actual: await provider.Create(
                    folderId: "folder",
                    name: "a.pdf",
                    input,
                    default));
            var expected = responseScope ?? string.Join(' ', GoogleAuthentication.LoginScopes);
            Assert.Equal(expected: expected, actual: credential.Token.Scope);
            string[] collection = Directory.GetFiles(
                    path: directory,
                    searchPattern: "*.json",
                    SearchOption.AllDirectories);
            global::System.Collections.Generic.IEnumerable<string> collection2 = collection;
            var file = Assert.Single(
                collection2);
            var persisted = await new AtomicTokenStore(file).GetAsync<TokenResponse>("fixture");
            Assert.Equal(expected: expected, actual: persisted.Scope);
            Assert.Equal(expected: "refresh", actual: persisted.RefreshToken);
            if (responseScope is null)
            {
                var second = await auth.Resolve(
                    AuthConfig,
                    GoogleAuthentication.LoginScopes,
                    "fixture",
                    services,
                    default);
                Assert.Equal(expected: expected, actual: second.Token.Scope);
                Assert.Equal(expected: "fresh", actual: second.Token.AccessToken);
                using var secondService = CredentialService(handler, second);
                using var secondProvider = new GoogleDriveSyncProvider(secondService);
                using var secondInput = new MemoryStream([2]);
                Assert.Equal(
                    expected: "created",
                    actual: await secondProvider.Create(
                        folderId: "folder",
                        name: "b.pdf",
                        secondInput,
                        default));
            }
            else
            {
                await Assert.ThrowsAsync<AuthenticationRequiredException>(() => auth.Resolve(
                    AuthConfig,
                    GoogleAuthentication.LoginScopes,
                    "fixture",
                    services,
                    default));
            }

            int expected2;
            if (responseScope is null)
            {
                expected2 = 2;
            }
            else
            {
                expected2 = 1;
            }

            int actual2 = handler.Requests.Count(x => new Uri(x.Uri).Host != "oauth2.googleapis.com");
            Assert.Equal(
                expected: expected2, actual: actual2);
            Assert.Single(handler.Requests.Where(x => new Uri(x.Uri).Host == "oauth2.googleapis.com"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(
        "planning",
        4,
        0,
        0)]
    [InlineData(
        "first-mutation",
        4,
        4,
        0)]
    [InlineData(
        "after-completed",
        6,
        5,
        1)]
    public async Task ExpiryAfterResolutionWithoutRefreshTokenFailsBeforeSending(
        string stage,
        int exit,
        int readsAndWrites,
        int writes)
    {
        string CalculatePath2()
        {
            string path2 = Guid.NewGuid().ToString("N");
            return path2;
        }

        var directory = Path.Combine(Path.GetTempPath(), CalculatePath2());
        Directory.CreateDirectory(directory);
        var original = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        UserCredential? credential = null;
        using var handler = new Transport((request, _) =>
        {
            Assert.NotEqual(expected: "oauth2.googleapis.com", actual: request.RequestUri!.Host);
            if (request.RequestUri.AbsolutePath.EndsWith("/about", StringComparison.Ordinal))
            {
                global::System.Net.Http.HttpResponseMessage CalculateResult()
                {
                    global::System.Net.Http.HttpResponseMessage result = Json("{\"user\":{\"permissionId\":\"account\"}}");
                    return result;
                }

                return Task.FromResult(CalculateResult());
            }

            if (request.Method == HttpMethod.Delete)
            {
                credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                global::System.Net.Http.HttpResponseMessage CalculateResult2()
                {
                    global::System.Net.Http.HttpResponseMessage result2 = new HttpResponseMessage(HttpStatusCode.NoContent);
                    return result2;
                }

                return Task.FromResult(CalculateResult2());
            }
            var folderQuery = Uri.UnescapeDataString(request.RequestUri.Query)
                .Contains("mimeType", StringComparison.Ordinal);
            if (!folderQuery && stage == "first-mutation")
            {
                credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            }

            string json;
            if (folderQuery)
            {
                json = "{\"files\":[{\"id\":\"folder\",\"name\":\"Configured\"}]}";
            }
            else
            {
                string text;
                if (stage == "after-completed")
                {
                    text = "{\"files\":[{\"id\":\"obsolete\",\"name\":\"obsolete.pdf\"}]}";
                }
                else
                {
                    text = "{\"files\":[]}";
                }

                json = text;
            }

            global::System.Net.Http.HttpResponseMessage result3 = Json(
            json);
            global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage> fromResult = Task.FromResult(
            result3);
            return fromResult;
        });
        using var tokens = new SdkTokens(handler, refreshToken: null);
        using var auth = new GoogleAuthentication(Path.Combine(directory, "auth"), tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        try
        {
            await auth.Login(
                AuthConfig,
                "fixture",
                services,
                default);
            credential = await auth.Resolve(
                AuthConfig,
                GoogleAuthentication.LoginScopes,
                "fixture",
                services,
                default);
            if (stage == "planning")
            {
                credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            }

            using var service = CredentialService(handler, credential);
            using var provider = new GoogleDriveSyncProvider(service);
            Console.SetOut(stdout);
            Console.SetError(stderr);
            Assert.Equal(
                expected: exit,
                actual: await new DriveSyncTests.FixtureDrive(provider).Publish(
                    new()
                    {
                        NoCache = true,
                        DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                    },
                    new() { Profile = "Curmanschii Anton" },
                    new() { Directory = Path.Combine(directory, "output") },
                    new() { Json = true },
                    new() { Apply = stage != "planning" }));
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(expected: exit, actual: json.RootElement.GetProperty("exitCode").GetInt32());
            if (stage != "planning")
            {
                var outcomes = json.RootElement.GetProperty("data").GetProperty("outcomes").EnumerateArray().ToArray();
                var failedIndex = stage == "after-completed" ? 1 : 0;
                if (failedIndex == 1)
                {
                    Assert.Equal(expected: "completed", actual: outcomes[0].GetProperty("state").GetString());
                }

                Assert.Equal(expected: "failed", actual: outcomes[failedIndex].GetProperty("state").GetString());
                Assert.All(
                    outcomes.Skip(failedIndex + 1),
                    x => Assert.Equal(expected: "not-attempted", actual: x.GetProperty("state").GetString()));
            }
            if (exit == 4)
            {
                Assert.Contains(expectedSubstring: "auth login google", actualString: stderr.ToString());
            }

            Assert.Equal(expected: readsAndWrites, actual: handler.Requests.Count);
            Assert.Equal(expected: writes, actual: handler.Requests.Count(x => x.Method != "GET"));
            Assert.DoesNotContain(expectedSubstring: "access-secret", actualString: stdout.ToString() + stderr);
        }
        finally
        {
            Console.SetOut(original);
            Console.SetError(originalError);
            Directory.Delete(directory, true);
        }
    }

    private static BuiltGoogleCredentialsConfig AuthConfig
    {
        get
        {
            return new()
            {
                CredentialsPath = "unused",
                ApiKeysSource =
                    new ManualGoogleApiKeysSource
                    {
                        Value = new()
                        {
                            ClientId = "fixture",
                            ClientSecret = "fixture",
                        },
                    },
            };
        }
    }

    private static DriveService CredentialService(Transport handler, UserCredential credential)
    {
        return new(
            new BaseClientService.Initializer
            {
                HttpClientFactory = new Factory(handler),
                HttpClientInitializer = credential,
                ApplicationName = "fixture",
                DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None,
            });
    }

    private sealed class SdkTokens(Transport handler, string? refreshToken = "refresh")
        : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];

        public Task<TokenResponse> Consent(
            ClientSecrets secrets,
            string teacher,
            string[] scopes,
            CancellationToken token)
        {
            return Task.FromResult(new TokenResponse
            {
                AccessToken = "access-secret",
                RefreshToken = refreshToken,
                Scope = string.Join(' ', scopes),
                IssuedUtc = DateTime.UtcNow,
                ExpiresInSeconds = 3600,
            });
        }

        public Task<TokenResponse?> Refresh(
            ClientSecrets secrets,
            string teacher,
            string[] scopes,
            TokenResponse authorization,
            CancellationToken token)
            => throw new Xunit.Sdk.XunitException("Refresh must use the production SDK after resolution.");

        public UserCredential CreateCredential(
            ClientSecrets secrets,
            string teacher,
            string[] scopes,
            TokenResponse authorization,
            IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(
                new()
                {
                    ClientSecrets = secrets,
                    Scopes = scopes,
                    DataStore = store,
                    HttpClientFactory = new Factory(handler),
                });
            _flows.Add(flow);
            return new(
                flow,
                teacher,
                authorization);
        }

        public void Dispose()
        {
            foreach (var flow in _flows)
            {
                flow.Dispose();
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    public async Task InvalidOutputPathFailsBeforeScheduleOrAuthentication(string path)
    {
        var original = Console.Out;
        using var stdout = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Assert.Equal(
                expected: 2,
                actual: await new UninitializedDrive().Publish(
                    new() { DataDirectory = "missing-source" },
                    new(),
                    new() { Directory = path },
                    new() { Json = true },
                    new()));
            using var json = System.Text.Json.JsonDocument.Parse(stdout.ToString());
            Assert.Equal(expected: 2, actual: json.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Empty(json.RootElement.GetProperty("outputs").EnumerateArray());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    private sealed class UninitializedDrive : DriveCommands
    {
        protected override void ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
            => throw new Xunit.Sdk.XunitException("Output arguments must fail before initialization.");
    }

    private static DriveService Service(Transport handler)
    {
        return new(
            new BaseClientService.Initializer
            {
                HttpClientFactory = new Factory(handler),
                ApplicationName = "fixture",
            });
    }

    private static HttpResponseMessage Json(string json)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                json,
                Encoding.UTF8,
                "application/json"),
        };
    }

    private sealed class Factory(Transport handler) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => handler;
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<(string Method, string Uri, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(
                (request.Method.Method, request.RequestUri!.ToString(),
                    request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await respond(request, cancellationToken);
        }
    }
}
