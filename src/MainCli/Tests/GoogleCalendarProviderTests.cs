using System.Net;
using System.Text;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Google.Apis.Services;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.Cli;
using ScheduleLib.Dates;

public sealed class GoogleCalendarProviderTests
{
    /// <summary>One HTTP request observed by the test transport.</summary>
    private readonly record struct CalendarRequest(string Method, string Host, string Path);

    [Theory]
    [InlineData("calendar", HttpStatusCode.Unauthorized)]
    [InlineData("calendar", HttpStatusCode.ServiceUnavailable)]
    [InlineData("calendar", HttpStatusCode.TemporaryRedirect)]
    [InlineData("event", HttpStatusCode.Unauthorized)]
    [InlineData("event", HttpStatusCode.ServiceUnavailable)]
    [InlineData("event", HttpStatusCode.TemporaryRedirect)]
    [InlineData("delete", HttpStatusCode.Unauthorized)]
    [InlineData("delete", HttpStatusCode.ServiceUnavailable)]
    [InlineData("delete", HttpStatusCode.TemporaryRedirect)]
    public async Task SdkMutationsNeverReplayOrFollowRedirects(string action, HttpStatusCode status)
    {
        using var transport = new Transport((request, _) =>
        {
            // A replay or response-triggered refresh would succeed, concealing the first failure.
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                HttpResponseMessage CalculateResult()
                {
                    var result = Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
                    return result;
                }

                return Task.FromResult(
                    CalculateResult());
            }

            var response = Error(status);
            response.Headers.Location = new Uri("https://www.googleapis.com/redirected");
            return Task.FromResult(response);
        });
        using var flow = Flow(transport);
        var credential = new UserCredential(
            flow,
            "fixture",
            Authorization());
        using var service = Service(transport, credential);
        using var provider = new GoogleCalendarSyncProvider(service);
        await Assert.ThrowsAsync<Google.GoogleApiException>(async () =>
        {
            if (action == "calendar")
            {
                await provider.CreateCalendar("Lessons", default);
            }
            else if (action == "event")
            {
                await provider.CreateEvent(
                    "calendar-id",
                    new(),
                    default);
            }
            else
            {
                await provider.DeleteCalendar("calendar-id", default);
            }
        });
        var sent = Assert.Single(transport.Requests);
        Assert.Equal(expected: "www.googleapis.com", actual: sent.Host);
        string expected2;
        if (action == "delete")
        {
            expected2 = "DELETE";
        }
        else
        {
            expected2 = "POST";
        }

        string actual2 = sent.Method;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.DoesNotContain(expectedSubstring: "redirected", actualString: sent.Path);
    }

    [Fact]
    public async Task ProvisionedCredentialRefreshesBeforeSendAndPersistsAuthorization()
    {
        using var temp = new TestTempDirectory("schedulelib-tests");
        var directory = temp.Path;
        using var transport = new Transport((request, _) =>
        {
            string value;
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                value = "{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}";
            }
            else
            {
                value = "{\"id\":\"created\"}";
            }

            var result = Json(
                value);
            var fromResult = Task.FromResult(
                result);
            return fromResult;
        });
        using var tokens = new SdkTokens(transport);
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new BuiltGoogleCredentialsConfig
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
        try
        {
            // Provision fixture state only; no browser or live authorization.
            await auth.Login("fixture", services)
                .WithCredentials(config)
                .Execute(default);
            var credential = await auth.Resolve(
                new GoogleCredentialRequest(
                    config,
                    [CalendarService.Scope.Calendar],
                    "fixture",
                    services),
                default);
            credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            using var service = Service(transport, credential);
            using var provider = new GoogleCalendarSyncProvider(service);
            Assert.Equal(expected: "created", actual: await provider.CreateCalendar("Lessons", default));
            Assert.Equal(
                expected: new[]
                {
                    "oauth2.googleapis.com",
                    "www.googleapis.com",
                },
                actual: transport.Requests.Select(x => x.Host));
            var second = await auth.Resolve(
                new GoogleCredentialRequest(
                    config,
                    [CalendarService.Scope.Calendar],
                    "fixture",
                    services),
                default);
            Assert.Equal(expected: "fresh", actual: second.Token.AccessToken);
            Assert.Equal(expected: "fixture", actual: second.Token.RefreshToken);
            Assert.Contains(expectedSubstring: CalendarService.Scope.Calendar, actualString: second.Token.Scope);
            Assert.Equal(expected: 2, actual: transport.Requests.Count);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static GoogleAuthorizationCodeFlow Flow(Transport transport)
    {
        return new(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = "fixture",
                    ClientSecret = "fixture",
                },
                HttpClientFactory = new Factory(transport),
            });
    }

    private static TokenResponse Authorization()
    {
        return new()
        {
            AccessToken = "fixture",
            RefreshToken = "fixture",
            ExpiresInSeconds = 3600,
            IssuedUtc = DateTime.UtcNow,
            Scope = string.Join(' ', GoogleAuthentication.LoginScopes),
        };
    }

    private static CalendarService Service(Transport transport, UserCredential credential)
    {
        return new(
            new BaseClientService.Initializer
            {
                HttpClientFactory = new Factory(transport),
                HttpClientInitializer = credential,
                ApplicationName = "fixture",
            });
    }

    private sealed class SdkTokens(Transport transport, string? refreshToken = "fixture")
        : IGoogleTokenProvider, IDisposable
    {
        private readonly List<GoogleAuthorizationCodeFlow> _flows = [];

        public Task<TokenResponse> Consent(
            GoogleTokenContext context,
            CancellationToken token)
        {
            var authorization = Authorization();
            authorization.RefreshToken = refreshToken;
            return Task.FromResult(authorization);
        }

        public Task<TokenResponse?> Refresh(
            GoogleTokenContext context,
            TokenResponse authorization,
            CancellationToken token)
            => throw new Xunit.Sdk.XunitException("Refresh must happen in the production SDK after resolution.");

        public UserCredential CreateCredential(
            GoogleTokenContext context,
            TokenResponse authorization,
            Google.Apis.Util.Store.IDataStore store)
        {
            var flow = new GoogleAuthorizationCodeFlow(new()
            {
                ClientSecrets = context.Secrets,
                Scopes = context.Scopes,
                DataStore = store,
                HttpClientFactory = new Factory(transport),
            });
            _flows.Add(flow);
            return new(
                flow,
                context.Teacher,
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
    [InlineData(
        "read-sent-real-deadline",
        5,
        0,
        0,
        1,
        null)]
    [InlineData(
        "create-sent-real-deadline",
        6,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "read-expired-refresh-real-deadline",
        5,
        0,
        1,
        0,
        null)]
    [InlineData(
        "create-expired-refresh-real-deadline",
        5,
        0,
        1,
        2,
        "failed")]
    [InlineData(
        "event-expired-refresh-real-deadline",
        6,
        1,
        1,
        2,
        "failed")]
    [InlineData(
        "read-expired-refresh-real-cancel",
        130,
        0,
        -1,
        0,
        null)]
    [InlineData(
        "create-expired-refresh-real-cancel",
        130,
        0,
        -1,
        2,
        "uncertain")]
    [InlineData(
        "event-expired-refresh-real-cancel",
        130,
        1,
        -1,
        2,
        "uncertain")]
    [InlineData(
        "read-expired-refresh-timeout",
        5,
        0,
        3,
        0,
        null)]
    [InlineData(
        "read-expired-refresh-connection",
        5,
        0,
        1,
        0,
        null)]
    [InlineData(
        "read-expired-refresh-unavailable",
        5,
        0,
        3,
        0,
        null)]
    [InlineData(
        "read-expired-refresh-cancel",
        130,
        0,
        -1,
        0,
        null)]
    [InlineData(
        "create-expired-refresh-timeout",
        5,
        0,
        3,
        2,
        "failed")]
    [InlineData(
        "create-expired-refresh-connection",
        5,
        0,
        1,
        2,
        "failed")]
    [InlineData(
        "create-expired-refresh-unavailable",
        5,
        0,
        3,
        2,
        "failed")]
    [InlineData(
        "create-expired-refresh-cancel",
        130,
        0,
        -1,
        2,
        "uncertain")]
    [InlineData(
        "event-expired-refresh-timeout",
        6,
        1,
        3,
        2,
        "failed")]
    [InlineData(
        "event-expired-refresh-connection",
        6,
        1,
        1,
        2,
        "failed")]
    [InlineData(
        "event-expired-refresh-unavailable",
        6,
        1,
        3,
        2,
        "failed")]
    [InlineData(
        "event-expired-refresh-cancel",
        130,
        1,
        -1,
        2,
        "uncertain")]
    [InlineData(
        "create-503",
        6,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "create-401-refresh",
        4,
        1,
        0,
        2,
        "failed")]
    [InlineData(
        "read-401-refresh-failed",
        4,
        0,
        0,
        1,
        null)]
    [InlineData(
        "read-401-no-refresh",
        4,
        0,
        0,
        1,
        null)]
    [InlineData(
        "create-401-refresh-failed",
        4,
        1,
        0,
        2,
        "failed")]
    [InlineData(
        "create-expired-invalid-grant",
        4,
        0,
        1,
        2,
        "failed")]
    [InlineData(
        "create-expired-no-refresh",
        4,
        0,
        0,
        2,
        "failed")]
    [InlineData(
        "event-expired-invalid-grant",
        6,
        1,
        1,
        2,
        "failed")]
    [InlineData(
        "event-expired-no-refresh",
        6,
        1,
        0,
        2,
        "failed")]
    [InlineData(
        "read-expired-invalid-grant",
        4,
        0,
        1,
        0,
        null)]
    [InlineData(
        "read-expired-no-refresh",
        4,
        0,
        0,
        0,
        null)]
    [InlineData(
        "read-timeout",
        5,
        0,
        0,
        1,
        null)]
    [InlineData(
        "create-timeout",
        6,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "read-connection",
        5,
        0,
        0,
        1,
        null)]
    [InlineData(
        "read-cancel",
        130,
        0,
        0,
        1,
        null)]
    [InlineData(
        "create-cancel",
        130,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "create-missing-id",
        6,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "create-blank-id",
        6,
        1,
        0,
        2,
        "uncertain")]
    [InlineData(
        "preview-pagination",
        0,
        0,
        0,
        5,
        null)]
    [InlineData(
        "create-401-no-refresh",
        4,
        1,
        0,
        2,
        "failed")]
    [InlineData(
        "event-401",
        6,
        2,
        0,
        2,
        "failed")]
    [InlineData(
        "event-timeout",
        6,
        2,
        0,
        2,
        "uncertain")]
    [InlineData(
        "event-second-cancel",
        130,
        3,
        0,
        2,
        "uncertain")]
    [InlineData(
        "event-503",
        6,
        2,
        0,
        2,
        "uncertain")]
    [InlineData(
        "create-timeout-after-delete",
        6,
        2,
        0,
        4,
        "uncertain")]
    [InlineData(
        "event-missing-id",
        6,
        2,
        0,
        2,
        "uncertain")]
    [InlineData(
        "event-blank-id",
        6,
        2,
        0,
        2,
        "uncertain")]
    public async Task ProductionTransportHasCorrectCliOutcomes(
        string scenario,
        int expectedExit,
        int expectedMutations,
        int expectedRefreshes,
        int expectedReads,
        string? expectedState)
    {
        UserCredential? credential = null;
        using var cancel = new CancellationTokenSource();
        var createAttempts = 0;
        using var transport = new Transport(async (request, requestToken) =>
        {
            // The API HttpClient cancels the interceptor's linked token when its deadline
            // expires. Wait on that real token inside OAuth; do not synthesize an exception.
            bool CheckCondition()
            {
                bool CalculateConditionResult()
                {
                    if (request.RequestUri!.Host != "oauth2.googleapis.com")
                    {
                        return false;
                    }

                    if (scenario.EndsWith("real-deadline", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    if (scenario.EndsWith("real-cancel", StringComparison.Ordinal))
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                return conditionResult;
            }

            if (CheckCondition())
            {
                if (scenario.EndsWith("real-cancel", StringComparison.Ordinal))
                {
                    cancel.Cancel();
                }

                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("Refresh must be interrupted before the API request is sent.");
            }

            bool CheckCondition7()
            {
                if (request.RequestUri!.Host != "oauth2.googleapis.com")
                {
                    return false;
                }

                if (!scenario.Contains("-refresh-", StringComparison.Ordinal))
                {
                    return false;
                }

                if (scenario.Contains("expired", StringComparison.Ordinal))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition7())
            {
                if (scenario.EndsWith("unavailable", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    {
                        Content = new StringContent(
                            "{\"error\":\"temporarily_unavailable\",\"error_description\":\"synthetic-private-detail\"}",
                            Encoding.UTF8,
                            "application/json"),
                    };
                }

                if (scenario.EndsWith("connection", StringComparison.Ordinal))
                {
                    throw new HttpRequestException("synthetic-private-detail");
                }

                if (scenario.EndsWith("cancel", StringComparison.Ordinal))
                {
                    cancel.Cancel();
                    throw new OperationCanceledException(cancel.Token);
                }
                throw new TaskCanceledException("synthetic-private-detail", new TimeoutException());
            }
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                if (scenario == "create-401-refresh")
                {
                    return Json("{\"access_token\":\"fresh\",\"expires_in\":3600,\"token_type\":\"Bearer\"}");
                }

                return new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent(
                        "{\"error\":\"invalid_grant\",\"error_description\":\"synthetic-private-detail\"}",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            bool CheckCondition2()
            {
                bool CalculateConditionResult2()
                {
                    if (scenario == "read-sent-real-deadline")
                    {
                        return true;
                    }
                    if (scenario != "create-sent-real-deadline")
                    {
                        return false;
                    }

                    if (request.Method == HttpMethod.Post)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult2 = CalculateConditionResult2();


                return conditionResult2;
            }

            if (CheckCondition2())
            {
                await Task.Delay(Timeout.Infinite, requestToken);
                throw new Xunit.Sdk.XunitException("The sent API request must time out.");
            }
            var path = request.RequestUri.AbsolutePath;
            if (request.Method == HttpMethod.Post)
            {
                createAttempts++;
            }

            bool CheckCondition3()
            {
                bool CalculateConditionResult3()
                {
                    if (scenario == "read-timeout")
                    {
                        return true;
                    }
                    if (!scenario.StartsWith("create-timeout", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (request.Method == HttpMethod.Post)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult3 = CalculateConditionResult3();


                return conditionResult3;
            }

            if (CheckCondition3())
            {
                throw new TaskCanceledException("synthetic-timeout", new TimeoutException());
            }

            bool CheckCondition8()
            {
                if (scenario != "event-timeout")
                {
                    return false;
                }

                if (createAttempts != 2)
                {
                    return false;
                }

                if (request.Method == HttpMethod.Post)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition8())
            {
                throw new TaskCanceledException("synthetic-timeout", new TimeoutException());
            }

            if (scenario == "read-connection")
            {
                throw new HttpRequestException("synthetic-connection-failure");
            }

            bool CheckCondition4()
            {
                bool conditionResult4;
                if (scenario == "read-cancel")
                {
                    conditionResult4 = true;
                }
                else
                {
                    if (scenario == "create-cancel")
                    {
                        conditionResult4 = request.Method == HttpMethod.Post;
                    }
                    else
                    {
                        conditionResult4 = false;
                    }

                    if (conditionResult4)
                    {
                        conditionResult4 = true;
                    }
                    else
                    {
                        if (scenario == "event-second-cancel")
                        {
                            if (createAttempts == 3)
                            {
                                conditionResult4 = request.Method == HttpMethod.Post;
                            }
                            else
                            {
                                conditionResult4 = false;
                            }
                        }
                        else
                        {
                            conditionResult4 = false;
                        }
                    }
                }

                return conditionResult4;
            }

            if (CheckCondition4())
            {
                cancel.Cancel();
                throw new OperationCanceledException(cancel.Token);
            }

            bool CheckCondition5()
            {
                bool CalculateConditionResult5()
                {
                    if (scenario.StartsWith("read-401", StringComparison.Ordinal))
                    {
                        return true;
                    }
                    if (!scenario.StartsWith("create-401", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (!(request.Method == HttpMethod.Post))
                    {
                        return false;
                    }

                    if (createAttempts == 1)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult5 = CalculateConditionResult5();


                return conditionResult5;
            }

            if (CheckCondition5())
            {
                return Error(HttpStatusCode.Unauthorized);
            }

            bool CheckCondition9()
            {
                if (scenario != "event-401")
                {
                    return false;
                }

                if (createAttempts != 2)
                {
                    return false;
                }

                if (request.Method == HttpMethod.Post)
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition9())
            {
                return Error(HttpStatusCode.Unauthorized);
            }

            bool CheckCondition6()
            {
                bool CalculateConditionResult6()
                {
                    if (scenario != "create-503")
                    {
                        return false;
                    }

                    if (createAttempts == 1)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult6 = CalculateConditionResult6();


                if (conditionResult6)
                {
                    conditionResult6 = true;
                }
                else
                {
                    if (scenario == "event-503")
                    {
                        conditionResult6 = createAttempts == 2;
                    }
                    else
                    {
                        conditionResult6 = false;
                    }
                }

                if (conditionResult6)
                {
                    conditionResult6 = request.Method == HttpMethod.Post;
                }
                else
                {
                    conditionResult6 = false;
                }

                return conditionResult6;
            }

            if (CheckCondition6())
            {
                return Error(HttpStatusCode.ServiceUnavailable);
            }

            if (path.EndsWith("/calendars/primary", StringComparison.Ordinal))
            {
                return Json("{\"id\":\"actual@example.test\"}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/calendarList", StringComparison.Ordinal))
            {
                if (scenario.StartsWith("create-expired", StringComparison.Ordinal))
                {
                    credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                }

                if (scenario == "preview-pagination")
                {
                    string value;
                    if (request.RequestUri.Query.Contains("pageToken"))
                    {
                        value = "{\"items\":[{\"id\":\"old\",\"summary\":\"lessons\"}]}";
                    }
                    else
                    {
                        value = "{\"items\":[],\"nextPageToken\":\"next\"}";
                    }

                    var json = Json(
                        value);
                    return json;
                }

                string value2;
                if (scenario == "create-timeout-after-delete")
                {
                    value2 = "{\"items\":[{\"id\":\"old\",\"summary\":\"lessons\"}]}";
                }
                else
                {
                    value2 = "{\"items\":[]}";
                }

                var json2 = Json(
                    value2);
                return json2;
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/events", StringComparison.Ordinal))
            {
                string value3;
                if (request.RequestUri.Query.Contains("pageToken"))
                {
                    value3 = "{\"items\":[{\"id\":\"event2\",\"summary\":\"Second\"}]}";
                }
                else
                {
                    value3 = "{\"items\":[{\"id\":\"event1\",\"summary\":\"First\"}],\"nextPageToken\":\"next\"}";
                }

                var json3 = Json(
                    value3);
                return json3;
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/calendars", StringComparison.Ordinal))
            {
                if (scenario.StartsWith("event-expired", StringComparison.Ordinal))
                {
                    credential!.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
                }

                string value4;
                if (scenario == "create-missing-id")
                {
                    value4 = "{}";
                }
                else
                {
                    string text;
                    if (scenario == "create-blank-id")
                    {
                        text = "{\"id\":\" \"}";
                    }
                    else
                    {
                        text = "{\"id\":\"new-calendar\"}";
                    }

                    value4 = text;
                }

                var json4 = Json(
                    value4);
                return json4;
            }
            if (request.Method == HttpMethod.Post)
            {
                string value5;
                if (scenario == "event-missing-id")
                {
                    value5 = "{}";
                }
                else
                {
                    string text2;
                    if (scenario == "event-blank-id")
                    {
                        text2 = "{\"id\":\" \"}";
                    }
                    else
                    {
                        text2 = "{\"id\":\"new-event\"}";
                    }

                    value5 = text2;
                }

                var json5 = Json(
                    value5);
                return json5;
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        });

        using var temp = new TestTempDirectory("schedulelib-tests");
        var directory = temp.Path;

        string? CalculateRefreshToken()
        {
            if (scenario.EndsWith("no-refresh", StringComparison.Ordinal))
            {
                return null;
            }

            return "fixture";
        }

        using var tokens = new SdkTokens(
            transport,
            CalculateRefreshToken());
        using var auth = new GoogleAuthentication(directory, tokens);
        using var services = new ServiceCollection().BuildServiceProvider();
        var config = new BuiltGoogleCredentialsConfig
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
        try
        {
            await auth.Login("fixture", services)
                .WithCredentials(config)
                .Execute(default);
            credential = await auth.Resolve(
                new GoogleCredentialRequest(
                    config,
                    [CalendarService.Scope.Calendar],
                    "fixture",
                    services),
                default);
            if (scenario.StartsWith("read-expired", StringComparison.Ordinal))
            {
                credential.Token.IssuedUtc = DateTime.UtcNow.AddHours(-2);
            }

            using var service = new CalendarService(
                new BaseClientService.Initializer
                {
                    HttpClientFactory = new Factory(transport),
                    HttpClientInitializer = credential,
                    ApplicationName = "fixture",
                });
            if (scenario.EndsWith("real-deadline", StringComparison.Ordinal))
            {
                service.HttpClient.Timeout = TimeSpan.FromMilliseconds(200);
            }

            using var provider = new GoogleCalendarSyncProvider(service);
            var beforeOut = Console.Out;
            var beforeError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            int exit;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                exit = await new FixtureCalendar(provider).Sync(
                    new()
                    {
                        NoCache = true,
                        DataDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures"),
                    },
                    new()
                    {
                        Profile = "Nartea Nichita",
                    },
                    new()
                    {
                        Json = true,
                    },
                    new()
                    {
                        Apply = !scenario.StartsWith("read-", StringComparison.Ordinal)
                            && scenario != "preview-pagination",
                    },
                    cancel.Token);
            }
            finally
            {
                Console.SetOut(beforeOut);
                Console.SetError(beforeError);
            }
            using var json = JsonDocument.Parse(stdout.ToString());
            var envelope = json.RootElement;
            var data = envelope.GetProperty("data");
            Assert.Equal(expected: expectedExit, actual: exit);

            string CalculateExpected()
            {
                if (expectedExit == 130)
                {
                    return "cancelled";
                }

                if (expectedExit == 0)
                {
                    return "preview";
                }

                if (expectedExit == 6)
                {
                    return "partial";
                }

                return "failed";
            }

            Assert.Equal(
                expected: CalculateExpected(),
                actual: envelope.GetProperty("status")
                    .GetString());
            var mutations = transport.Requests.Where(x =>
                {
                    if (x.Host == "oauth2.googleapis.com")
                    {
                        return false;
                    }

                    if (x.Method != "GET")
                    {
                        return true;
                    }

                    return false;
                })
                .ToArray();
            Assert.Equal(expected: expectedMutations, actual: mutations.Length);
            var refreshes = transport.Requests.Count(x => x.Host == "oauth2.googleapis.com");
            // SDK refresh cancellation races its timeout retry loop; API sends remain exact.
            if (expectedRefreshes == -1)
            {
                Assert.InRange(
                    actual: refreshes,
                    low: 1,
                    high: 3);
            }
            else
            {
                Assert.Equal(expected: expectedRefreshes, actual: refreshes);
            }

            Assert.Equal(
                expected: expectedReads,
                actual: transport.Requests.Count(x =>
                {
                    if (x.Host == "oauth2.googleapis.com")
                    {
                        return false;
                    }

                    if (x.Method == "GET")
                    {
                        return true;
                    }

                    return false;
                }));
            if (expectedState is not null)
            {
                var outcomes = data.GetProperty("outcomes")
                    .EnumerateArray()
                    .ToArray();
                int expected2;
                if (expectedMutations == 0)
                {
                    expected2 = 1;
                }
                else
                {
                    int int32 = expectedMutations;
                    int int322;
                    if (scenario.Contains("expired", StringComparison.Ordinal))
                    {
                        int322 = 1;
                    }
                    else
                    {
                        int322 = 0;
                    }

                    int int323 = int32 + int322;
                    expected2 = int323;
                }

                int actual2 = outcomes.Length;
                Assert.Equal(
                    expected: expected2,
                    actual: actual2);
                Assert.Equal(
                    expected: expectedState,
                    actual: outcomes.Last()
                        .GetProperty("state")
                        .GetString());
                Assert.All(
                    outcomes.SkipLast(1),
                    x => Assert.Equal(
                        expected: "completed",
                        actual: x.GetProperty("state")
                            .GetString()));
                if (outcomes.Last()
                        .GetProperty("action")
                        .GetString()
                    == "create event")
                {
                    Assert.Equal(
                        expected: "new-calendar",
                        actual: data.GetProperty("createdCalendarId")
                            .GetString());

                    int CalculateExpected2()
                    {
                        if (scenario == "event-second-cancel")
                        {
                            return 1;
                        }

                        return 0;
                    }

                    Assert.Equal(
                        expected: CalculateExpected2(),
                        actual: outcomes.Last()
                            .GetProperty("desiredEventIndex")
                            .GetInt32());
                    if (scenario == "event-second-cancel")
                    {
                        Assert.Equal(
                            expected: "new-event",
                            actual: outcomes[1].GetProperty("eventId")
                                .GetString());
                    }
                }
            }

            bool CheckCondition10()
            {
                if (expectedExit == 4)
                {
                    return true;
                }

                if (scenario is "event-expired-invalid-grant" or "event-expired-no-refresh" or "event-401")
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition10())
            {
                Assert.Contains(expectedSubstring: "auth login google", actualString: stderr.ToString());
            }

            Assert.DoesNotContain(
                expectedSubstring: "synthetic-private-detail",
                actualString: stdout.ToString() + stderr);
            if (scenario.Contains("expired-refresh", StringComparison.Ordinal))
            {
                Assert.DoesNotContain(expectedSubstring: "auth login google", actualString: stdout.ToString() + stderr);
                Assert.Equal(expected: "fixture", actual: credential.Token.RefreshToken);
                Assert.Contains(
                    expectedSubstring: CalendarService.Scope.Calendar,
                    actualString: credential.Token.Scope);
            }
            if (scenario == "preview-pagination")
            {
                Assert.Equal(
                    expected: 2,
                    actual: data.GetProperty("deletedEvents")
                        .GetArrayLength());
                Assert.Empty(data.GetProperty("outcomes")
                    .EnumerateArray());
            }
            Assert.Equal(expected: 1, actual: service.HttpClient.MessageHandler.NumTries);
            Assert.False(service.HttpClient.MessageHandler.FollowRedirect);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static HttpResponseMessage Json(string value)
    {
        return new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                value,
                Encoding.UTF8,
                "application/json"),
        };
    }

    private static HttpResponseMessage Error(HttpStatusCode status)
    {
        string CalculateContent()
        {
            string text = $"{"{\"error\":{\"code\":" + (int) status},\"message\":\"synthetic-rejection\"}}}}";
            string content = text;
            return content;
        }

        return new(status)
        {
            Content = new StringContent(
                CalculateContent(),
                Encoding.UTF8,
                "application/json"),
        };
    }

    private sealed class FixtureCalendar(ICalendarSyncProvider provider) : CalendarCommands
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
            services.Configure<ScheduleBuilderInitializerOptions>(x =>
            {
                var scheduleBuilderInitializerOptions = x;
                scheduleBuilderInitializerOptions.EnrichWithFullNames = false;
            });
            services.Configure<StudyYearOptions>(x =>
            {
                x.StudyYear = new(2025);
                x.Semester = Semester.Sem2;
            });
        }

        protected override Task<ICalendarSyncProvider> Connect(
            IServiceProvider services,
            BuiltGoogleCalendarConfig config,
            CancellationToken token) => Task.FromResult(provider);
    }

    private sealed class Factory(Transport transport) : HttpClientFactory
    {
        protected override HttpMessageHandler CreateHandler(CreateHttpClientArgs args) => transport;
    }

    private sealed class Transport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<CalendarRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Requests.Add(new(
                Method: request.Method.Method,
                Host: request.RequestUri!.Host,
                Path: request.RequestUri.AbsolutePath));
            return respond(request, token);
        }
    }
}
