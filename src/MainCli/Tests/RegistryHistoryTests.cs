using System.Net;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Logging;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;

public sealed class RegistryHistoryTests
{
    private static readonly Uri Index = new("https://registry.test/studregistry/LessonAttendance");

    private const string Years = """
                                 <a href='/studregistry/Account/Logout'>Exit</a>
                                 <form id='search'><select name='AnUniv' id='AnUniv'>
                                 <option selected value='2025/26'>2025/26</option><option value='2024/25'>2024/25</option>
                                 <option value='2024/25'>duplicate</option><option disabled value='2023/24'>disabled</option>
                                 </select></form>
                                 """;

    [Fact]
    public void TokenOnlyHistoricalLessonFormHasNoAssignedGroups()
    {
        using var document = new HtmlParser().ParseDocument(
            """
            <h1>Synthetic course</h1><h3>Groups</h3>
            <form name='lesson'><input type='hidden' name='__RequestVerificationToken' value='canary-token'></form>
            """);
        var collection = RegistryHistoryHtml.Groups(document, Index);
        System.Collections.IEnumerable collection2 = collection;
        Assert.Empty(collection2);
    }

    [Theory]
    [InlineData("<form name='lesson'></form>")]
    [InlineData("<form name='lesson'><input type='hidden' name='unknown' value='canary-token'></form>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'><div>Changed group structure</div></form>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'></form><a href='/studregistry/LessonAttendance/LessonsList/1'>Synthetic group</a>")]
    [InlineData(
        "<form name='lesson'><input type='hidden' name='__RequestVerificationToken'></form><a href='https://foreign.test/other/LessonAttendance/LessonsList/1'>Synthetic group</a>")]
    public void UnrecognizedEmptyOrMovedGroupLayoutStillFails(string html)
    {
        using var document = new HtmlParser().ParseDocument(html);
        Assert.Throws<RegistryHistoryException>(() => RegistryHistoryHtml.Groups(document, Index));
    }

    [Fact]
    public async Task HistoricalNavigationReselectsCurrentBeforeReadingItsGroups()
    {
        var activeYear = "2025/26";
        var groupYears = new List<string>();
        var requests = new List<string>();
        using var inner = new FakeHandler((request, _) =>
        {
            var uri = request.RequestUri!;
            requests.Add(uri.PathAndQuery);
            string html;
            if (uri.AbsolutePath == Index.AbsolutePath)
            {
                if (uri.Query.Length > 0)
                {
                    activeYear = Uri.UnescapeDataString(uri.Query[8..]);
                }

                html =
                    $"<form><select id='AnUniv' name='AnUniv'>{$"<option {(activeYear == "2024/25" ? "selected" : "")} value='2024/25'>2024/25</option>"}{$"<option {(activeYear == "2025/26" ? "selected" : "")} value='2025/26'>2025/26</option>"}</select></form><div id='nav-1'><div><span>1</span><span> <a href='/studregistry/LessonAttendance/groups/1'>Synthetic</a></span></div></div><div id='nav-2'></div>";
            }
            else
            {
                groupYears.Add(activeYear);
                string text2 = $"<a href='/studregistry/LessonAttendance/LessonsList/1'>{activeYear}</a>";
                string text3 =
                    $"<form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>{text2}<a href='/studregistry/Evaluation/Index/1'>grades</a></div></div></div></form>";
                html =
                    text3;
            }

            string CalculateContent()
            {
                string text = $"<a href='/studregistry/Account/Logout'>Exit</a>{html}";
                string content = text;
                return content;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CalculateContent()),
            });
        });
        using var client = new HttpClient(inner);
        var summaries = await new RegistryHistoryReader(client, Index).Discover(null, default);
        Assert.Equal(
            expected: new[]
            {
                "2024/25",
                "2025/26",
            },
            actual: groupYears);
        Assert.Equal(
            expected: new[]
            {
                "2024/25",
                "2025/26",
            },
            actual: summaries.Select(x => x.Year));
        string expected2 = "2025/26";
        var single = Assert.Single(summaries[1].Courses);
        IEnumerable<string> collection = single.Groups;
        string actual2 = Assert.Single(collection);
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Contains("/studregistry/LessonAttendance?AnUniv=2025%2F26", requests);
    }

    [Fact]
    public void YearNavigationUsesObservedGetFormAndEncodedValue()
    {
        AngleSharp.Html.Dom.IHtmlDocument CalculateDocument()
        {
            var document = new HtmlParser().ParseDocument(Years);
            return document;
        }

        var years = RegistryHistoryHtml.Years(CalculateDocument(), Index);
        Assert.Equal(
            expected: new[]
            {
                "2025/26",
                "2024/25",
            },
            actual: years.Select(x => x.Value));
        Assert.True(years[0].Selected);
        Assert.Equal(expected: "https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25",
            actual: years[1].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData(
        "<form method='post'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<form action='https://other.test/'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Unknown,
        RegistryHistoryDiagnostic.RequestPolicy)]
    [InlineData(
        "<form><input name='token' value='private'><select id='AnUniv' name='AnUniv'><option>2024/25</option></select></form>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<select id='AnUniv' name='AnUniv'><option>2024/25</option></select>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    [InlineData(
        "<html>no year selector</html>",
        RegistryHistoryPhase.Index,
        RegistryHistoryDiagnostic.YearSelector)]
    public void UnsupportedYearMarkupFailsExplicitly(
        string html,
        RegistryHistoryPhase phase,
        RegistryHistoryDiagnostic diagnostic)
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
        {
            AngleSharp.Html.Dom.IHtmlDocument CalculateDocument()
            {
                var document = new HtmlParser().ParseDocument(html);
                return document;
            }

            return RegistryHistoryHtml.Years(CalculateDocument(), Index);
        });
        Assert.Equal(expected: phase, actual: error.Phase);
        Assert.Equal(expected: diagnostic, actual: error.Diagnostic);
    }

    [Theory]
    [InlineData("<form name='lesson'></form>")]
    [InlineData(
        "<form name='lesson'><div class='row'></div><div class='row'><div class='col'><a href='/studregistry/LessonAttendance/LessonsList/1'>changed layout</a></div></div></form>")]
    public void ChangedGroupContainerDoesNotSilentlyProduceAnEmptySummary(string html)
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
        {
            AngleSharp.Html.Dom.IHtmlDocument CalculateDocument()
            {
                var document = new HtmlParser().ParseDocument(html);
                return document;
            }

            return RegistryHistoryHtml.Groups(CalculateDocument(), Index);
        });
        Assert.Equal(expected: RegistryHistoryPhase.Groups, actual: error.Phase);
        Assert.Contains(
            error.Diagnostic,
            new[]
            {
                RegistryHistoryDiagnostic.GroupContainer,
                RegistryHistoryDiagnostic.GroupLayout,
            });
    }

    [Fact]
    public void ChangedCourseContainerDoesNotSilentlyProduceAnEmptySummary()
    {
        var error = Assert.Throws<RegistryHistoryException>(() =>
        {
            AngleSharp.Html.Dom.IHtmlDocument CalculateDocument()
            {
                var document = new HtmlParser().ParseDocument(
                    """
                    <div id='nav-1'><a href='/studregistry/LessonAttendance/groups/1'>changed layout</a></div><div id='nav-2'></div>
                    """);
                return document;
            }

            return RegistryHistoryHtml.Courses(
                CalculateDocument(),
                Index);
        });
        Assert.Equal(expected: RegistryHistoryPhase.Index, actual: error.Phase);
        Assert.Equal(expected: RegistryHistoryDiagnostic.CourseLayout, actual: error.Diagnostic);
    }

    [Fact]
    public void RawHistoricalNamesSurviveWithoutScheduleMatchingAndUnsafeLinksFail()
    {
        var document = new HtmlParser().ParseDocument(
            """
            <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'> Old &amp; unmapped, </a></span></div></div>
            <div id='nav-2'><div><span>2</span><span><a href='/studregistry/LessonAttendance/groups/2'>Other,</a></span></div></div>
            """);
        var courses = RegistryHistoryHtml.Courses(document, Index);
        Assert.Equal(
            expected: new[]
            {
                "Old & unmapped,",
                "Other,",
            },
            actual: courses.Select(x => x.Name));
        Assert.Equal(
            expected: new[]
            {
                1,
                2,
            },
            actual: courses.Select(x => x.Semester));

        AngleSharp.Html.Dom.IHtmlDocument CalculateDocument()
        {
            var document2 = new HtmlParser().ParseDocument(
                """
                <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Repeat / unknown notation</a>
                <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a>
                </div></div></div></form>
                """);
            return document2;
        }

        var groups = RegistryHistoryHtml.Groups(
            CalculateDocument(),
            Index);
        string expected2 = "Repeat / unknown notation";
        var single = Assert.Single(groups);
        string actual2 = single.Name;
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Throws<RegistryHistoryException>(() =>
        {
            var htmlParser = new HtmlParser();
            string source = document.DocumentElement.OuterHtml.Replace(
                "/studregistry/LessonAttendance/groups/1",
                "https://other.test/studregistry/LessonAttendance/groups/1");
            var document2 = htmlParser.ParseDocument(
                source);
            AngleSharp.Dom.IDocument document3 = document2;
            var address = Index;
            var courses = RegistryHistoryHtml.Courses(
                document3,
                address);
            return courses;
        });
    }

    [Theory]
    [InlineData("https://registry.test/studregistry/LessonAttendance", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=1999%2F00", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F26", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%2F25&AnUniv=2023%2F24", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?AnUniv=2024%252F25", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance?action=Delete", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance#Edit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1", true)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1?groupId=2", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1?action=Delete", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1/Edit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/Create", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1%2FEdit", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1/", false)]
    [InlineData("https://registry.test/studregistry/LessonAttendance/groups/1#Edit", false)]
    [InlineData("https://other.test/studregistry/LessonAttendance/groups/1", false)]
    [InlineData("https://registry.test/other/LessonAttendance/groups/1", false)]
    public async Task OnlyExactIndexAndObservedNumericGroupReadsReachTransport(string address, bool allowed)
    {
        using var inner = new FakeHandler((_, _) =>
        {
            HttpResponseMessage CalculateResult()
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK);
                return result;
            }

            return Task.FromResult(CalculateResult());
        });

        Uri CalculateLogin()
        {
            Uri login2 = new(Index, "../Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        if (allowed)
        {
            using var response = await client.GetAsync(address);
        }
        else
        {
            await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(address));
        }

        int expected2;
        if (allowed)
        {
            expected2 = 1;
        }
        else
        {
            expected2 = 0;
        }

        int actual2 = inner.Calls;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(302)]
    public async Task RejectionStopsWithoutReplayOrFollowingRedirect(int status)
    {
        using var inner = new FakeHandler((_, _) =>
        {
            HttpResponseMessage CalculateResult()
            {
                var result = new HttpResponseMessage((HttpStatusCode) status);
                return result;
            }

            return Task.FromResult(CalculateResult());
        });

        Uri CalculateLogin()
        {
            Uri login2 = new(Index, "../Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.DoesNotContain(expectedSubstring: "registry.test", actualString: error.Message);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(expected: 1, actual: inner.Calls);
    }

    [Theory]
    [InlineData(
        "login",
        400,
        RegistryHistoryPhase.Login)]
    [InlineData(
        "login",
        403,
        RegistryHistoryPhase.Login)]
    [InlineData(
        "index",
        302,
        RegistryHistoryPhase.Index)]
    [InlineData(
        "index",
        429,
        RegistryHistoryPhase.Index)]
    [InlineData(
        "groups",
        403,
        RegistryHistoryPhase.Groups)]
    public async Task RejectionCarriesOnlyFixedPhaseAndNumericStatus(
        string route,
        int status,
        RegistryHistoryPhase phase)
    {
        using var inner = new FakeHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage((HttpStatusCode) status)
            {
                Content = new StringContent("canary-response-body"),
            }));
        var login = new Uri("https://registry.test/studregistry/Account/Login");

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: login,
            CalculateSpacing());
        using var client = new HttpClient(guard);
        using var content = new StringContent("canary-password");

        Uri CalculateRequestUri()
        {
            var requestUri = new Uri("https://registry.test/studregistry/LessonAttendance/groups/1");
            return requestUri;
        }

        var request = route switch
        {
            "login" => client.PostAsync(login, content),
            "index" => client.GetAsync(Index),
            "groups" => client.GetAsync(CalculateRequestUri()),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() => request);
        Assert.Equal(expected: phase, actual: error.Phase);
        Assert.Equal(expected: status, actual: error.StatusCode);
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        Assert.Equal(expected: 1, actual: inner.Calls);
    }

    [Fact]
    public async Task ReadsRetryAtMostOnceAndMutationNeverReachesTransport()
    {
        using var inner = new FakeHandler((_, _) =>
        {
            HttpResponseMessage CalculateResult()
            {
                var result = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                return result;
            }

            return Task.FromResult(CalculateResult());
        });

        Uri CalculateLogin()
        {
            Uri login2 = new(Index, "../Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.GetAsync(Index));
        Assert.Equal(expected: 2, actual: inner.Calls);
        using var content = new StringContent("canary-password");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        Assert.Equal(expected: 2, actual: inner.Calls);
    }

    [Fact]
    public async Task FreshHistorySessionBlocksMutationAndLessonEditRoutes()
    {
        using var inner = new FakeHandler((_, _) =>
        {
            HttpResponseMessage CalculateResult()
            {
                var result = new HttpResponseMessage(HttpStatusCode.OK);
                return result;
            }

            return Task.FromResult(CalculateResult());
        });

        Uri CalculateLogin()
        {
            Uri login2 = new("https://registry.test/studregistry/Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        using var content = new StringContent("synthetic");
        await Assert.ThrowsAsync<RegistryHistoryException>(() => client.PostAsync(Index, content));
        await Assert.ThrowsAsync<RegistryHistoryException>(() =>
        {
            Uri CalculateRequestUri()
            {
                string text = $"{Index.AbsoluteUri}/groups/1";
                string uriString = text;
                var requestUri = new Uri(uriString);
                return requestUri;
            }

            return client.DeleteAsync(CalculateRequestUri());
        });
        await Assert.ThrowsAsync<RegistryHistoryException>(() =>
        {
            Uri CalculateRequestUri()
            {
                string text = $"{Index.AbsoluteUri}/Edit/1";
                string uriString = text;
                var requestUri = new Uri(uriString);
                return requestUri;
            }

            return client.GetAsync(CalculateRequestUri());
        });
        Assert.Equal(expected: 0, actual: inner.Calls);
    }

    [Fact]
    public async Task GlobalGateSerializesDifferentAccountsAndSpacesRequests()
    {
        var active = 0;
        var maximum = 0;
        var starts = new List<DateTimeOffset>();

        async Task<HttpResponseMessage> Respond(HttpRequestMessage request, CancellationToken token)
        {
            var current = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, current);
            starts.Add(DateTimeOffset.UtcNow);
            await Task.Delay(20, token);
            Interlocked.Decrement(ref active);
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent("safe"),
            };
        }

        using var innerA = new FakeHandler(Respond);
        using var innerB = new FakeHandler(Respond);

        Uri CalculateLogin()
        {
            Uri login2 = new(Index, "../Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(30);
            return spacing;
        }

        using var guardA = new RegistryHistoryRequestHandler(
            innerA,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());

        Uri CalculateLogin2()
        {
            Uri login3 = new(Index, "../Account/Login");
            return login3;
        }

        TimeSpan CalculateSpacing2()
        {
            var spacing2 = TimeSpan.FromMilliseconds(30);
            return spacing2;
        }

        using var guardB = new RegistryHistoryRequestHandler(
            innerB,
            index: Index,
            login: CalculateLogin2(),
            CalculateSpacing2());
        using var first = new HttpClient(guardA);
        using var second = new HttpClient(guardB);
        // Intentionally race only synthetic transports to prove the shared gate.
        var a = first.GetAsync(Index);
        var b = second.GetAsync(Index);
        using var responseA = await a;
        using var responseB = await b;
        Assert.Equal(expected: 1, actual: maximum);
        Assert.True(starts[1] - starts[0] >= TimeSpan.FromMilliseconds(45));
    }

    [Fact]
    public void ErrorLoggerNeverEnumeratesOrLogsPrivateStudentValuesOrContext()
    {
        var log = new CaptureLogger();
        var logger = new RegistryErrorLogger(log);

        StudentsInGroup CalculateStudents()
        {
            StudentsInGroup students = new(
                PrivateStudents(),
                null!,
                default,
                default);
            return students;
        }

        logger.StudentsNotInDbButInRegistry(CalculateStudents());

        ErroneousLesson CalculateLesson()
        {
            ErroneousLesson lesson = new("canary-student canary-password canary-token");
            return lesson;
        }

        logger.LessonsDecidedErroneous(CalculateLesson());

        GroupParseErrorContext CalculateContext()
        {
            var exception = new GroupParsingException("canary-password canary-student");
            string stringValue = "canary-student canary-token";
            GroupParseErrorContext context = new(exception, stringValue);
            return context;
        }

        logger.GroupParsingError(CalculateContext());
        logger.GroupNotFound("canary-student canary-token");
        Assert.NotEmpty(log.Messages);

        string CalculateActualString()
        {
            string actualString2 = string.Join("\n", log.Messages);
            return actualString2;
        }

        Assert.DoesNotContain(expectedSubstring: "canary", actualString: CalculateActualString());

        static IEnumerable<ScheduleLib.Parsing.Name> PrivateStudents()
        {
            return new[]
            {
                "canary-student",
            }.Select<string, ScheduleLib.Parsing.Name>(_ =>
                throw new Exception("Private students must not be enumerated by logging."));
        }
    }

    [Fact]
    public void DuplicateStudentDiagnosticDoesNotIncludeTheirName()
    {
        var builder = new StudentAttendanceListBuilder();
        var name = ScheduleLib.Parsing.NameHelper.Parse("Canary Student");
        builder.Student(name);
        var error = Assert.Throws<InvalidOperationException>(() => builder.Student(name));
        Assert.DoesNotContain(expectedSubstring: "Canary", actualString: error.Message);
    }

    [Fact]
    public async Task ProductionLoginBuilderAndYearDiscoveryUseOnlySyntheticCredentialsAndSerialReads()
    {
        var log = new CaptureLogger();
        using var logs = LoggerFactory.Create(x =>
        {
            CaptureProvider CalculateProvider()
            {
                var provider = new CaptureProvider(log);
                return provider;
            }

            x.AddProvider(CalculateProvider());
        });
        var login = new Uri("https://registry.test/studregistry/Account/Login");
        var requests = new List<string>();
        using var inner = new FakeHandler(async (request, token) =>
        {
            var list = requests;
            string text = $"{request.Method + " "}{request.RequestUri!.PathAndQuery}";
            string item = text;
            list.Add(item);
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(token);
                Assert.Contains(expectedSubstring: "canary-password", actualString: body);
                return new(HttpStatusCode.Redirect);
            }
            var historical = request.RequestUri.Query.Contains("2024%2F25", StringComparison.Ordinal);
            var years = historical
                ? Years.Replace("selected value='2025/26'", "value='2025/26'")
                    .Replace(
                        "value='2024/25'>2024/25",
                        "selected value='2024/25'>2024/25")
                : Years;
            var html = request.RequestUri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal)
                ? """
                  <a href='/studregistry/Account/Logout'>Exit</a>
                  <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                  <a href='/studregistry/LessonAttendance/LessonsList/1?groupId=2'>Unknown historical group</a>
                  <a href='/studregistry/Evaluation/Index/1?groupId=2'>grades</a></div></div></div></form>
                  <table><tr><td>canary-student</td><td>private grade</td></tr></table>
                  """
                : $"{years}<div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped historical course,</a></span></div></div><div id='nav-2'></div>";
            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
            };
        });

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: login,
            CalculateSpacing());
        using var client = new HttpClient(guard);
        using var transport = new HttpClientContext(
            new AngleSharp.Io.MemoryCookieProvider(),
            client,
            guard);
        var builder = new ScrapingContextBuilder();
        builder.AddLogging(logs);
        builder.TokenAuth(
            new TokenNamesConfig
            {
                BaseUrl = new("https://registry.test/studregistry/"),
                LoginUrl = login,
                TokenCookieName = "synthetic",
            },
            x => x.PasswordLoginCall(
                new()
                {
                    Login = "canary-login",
                    Password = "canary-password",
                },
                new()
                {
                    Login = "UserLogin",
                    Password = "UserPassword",
                }));
        using var context = await builder.Build(default, transport);
        guard.CompleteAuthentication();
        var summary = await new RegistryHistoryReader(context.HttpClient, Index).Discover("2024/25", default);
        string expected2 = "2024/25";
        var single = Assert.Single(summary);
        string actual2 = single.Year;
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = "Unknown historical group";
        var single2 = Assert.Single(summary[0].Courses);
        IEnumerable<string> collection = single2.Groups;
        string actual3 = Assert.Single(collection);
        Assert.Equal(expected: expected3, actual: actual3);
        Assert.Equal(
            expected: new[]
            {
                "POST /studregistry/Account/Login",
                "GET /studregistry/LessonAttendance",
                "GET /studregistry/LessonAttendance?AnUniv=2024%2F25",
                "GET /studregistry/LessonAttendance/groups/1",
            },
            actual: requests);

        string CalculateActualString()
        {
            string actualString2 = string.Join("\n", log.Messages);
            return actualString2;
        }

        Assert.DoesNotContain(expectedSubstring: "canary", actualString: CalculateActualString());
        string expectedSubstring2 = "canary-student";
        string actualString3 = System.Text.Json.JsonSerializer.Serialize(summary);
        Assert.DoesNotContain(expectedSubstring: expectedSubstring2, actualString: actualString3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LoginPageOrIgnoredYearStopsBeforeGroupReads(bool loginPage)
    {
        using var inner = new FakeHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                loginPage
                    ? "<input type='password' value='canary-password'>"
                    : Years + "<div id='nav-1'></div><div id='nav-2'></div>"),
        }));

        Uri CalculateLogin()
        {
            Uri login2 = new("https://registry.test/studregistry/Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var error = await Assert.ThrowsAsync<RegistryHistoryException>(() =>
            new RegistryHistoryReader(client, Index).Discover("2024/25", default));
        Assert.DoesNotContain(expectedSubstring: "canary", actualString: error.Message);
        int expected2;
        if (loginPage)
        {
            expected2 = 1;
        }
        else
        {
            expected2 = 2;
        }

        int actual2 = inner.Calls;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public async Task CurrentYearUsesSelectedRegistryValueWithoutHistoricalRequests()
    {
        var queries = new List<string>();
        using var inner = new FakeHandler((request, _) =>
        {
            queries.Add(request.RequestUri!.PathAndQuery);
            var html = request.RequestUri.AbsolutePath.EndsWith("/groups/1", StringComparison.Ordinal)
                ? """
                  <a href='/studregistry/Account/Logout'>Exit</a>
                  <form name='lesson'><div class='row'></div><div class='row'><div class='col'><div class='row'>
                  <a href='/studregistry/LessonAttendance/LessonsList/1'>Unmapped group</a><a href='/studregistry/Evaluation/Index/1'>grades</a>
                  </div></div></div></form>
                  """
                : Years
                + """
                  <div id='nav-1'><div><span>1</span><span><a href='/studregistry/LessonAttendance/groups/1'>Unmapped course</a></span></div></div><div id='nav-2'></div>
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html),
            });
        });

        Uri CalculateLogin()
        {
            Uri login2 = new("https://registry.test/studregistry/Account/Login");
            return login2;
        }

        TimeSpan CalculateSpacing()
        {
            var spacing = TimeSpan.FromMilliseconds(1);
            return spacing;
        }

        using var guard = new RegistryHistoryRequestHandler(
            inner,
            index: Index,
            login: CalculateLogin(),
            CalculateSpacing());
        guard.CompleteAuthentication();
        using var client = new HttpClient(guard);
        var summary = Assert.Single(await new RegistryHistoryReader(client, Index).Discover("current", default));
        Assert.Equal(expected: "2025/26", actual: summary.Year);
        string expected2 = "Unmapped group";
        var single = Assert.Single(summary.Courses);
        IEnumerable<string> collection = single.Groups;
        string actual2 = Assert.Single(collection);
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(
            expected: new[]
            {
                "/studregistry/LessonAttendance",
                "/studregistry/LessonAttendance/groups/1",
            },
            actual: queries);
    }

    private sealed class CaptureProvider(CaptureLogger logger) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void Dispose()
        {
        }
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            return respond(request, token);
        }
    }

    private sealed class CaptureLogger : ILogger<RegistryErrorLogger>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Messages.Add(state.ToString()!);
            return null;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string CalculateItem()
            {
                string invoke = formatter(state, exception);
                string item = invoke + exception;
                return item;
            }

            Messages.Add(CalculateItem());
        }
    }
}
