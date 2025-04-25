using static ScheduleLib.UnreachableHelper;

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using HttpMethod = System.Net.Http.HttpMethod;

namespace ScheduleLib.OnlineRegistry;

public enum Session
{
    Ses1,
    Ses2,
}

internal sealed class HttpClientContext : IDisposable
{
    public required HttpClient Client { get; init; }
    public required HttpMessageHandler Handler { get; init; }
    public required MemoryCookieProvider CookieProvider { get; init; }

    public CookieContainer Cookies => CookieProvider.Container;

    public static HttpClientContext Create()
    {
        var cookieProvider = new MemoryCookieProvider();
        var cookieContainer = cookieProvider.Container;

        HttpClientHandler? mainHandler = null;
        try
        {
#pragma warning disable CA2000 // Wrong dispose warning.
            mainHandler = new HttpClientHandler();
            mainHandler.CookieContainer = cookieContainer;
            mainHandler.UseCookies = true;
            mainHandler.AllowAutoRedirect = false;

#pragma warning restore CA2000

            var handler = mainHandler;

            var httpClient = new HttpClient(handler);
            return new()
            {
                CookieProvider = cookieProvider,
                Client = httpClient,
                Handler = handler,
            };
        }
        catch
        {
            if (mainHandler is not null)
            {
                mainHandler.Dispose();
                throw;
            }
            throw;
        }
    }

    public void Dispose()
    {
        // Because it might be null
        Client?.Dispose();
        Handler?.Dispose();
    }
}

internal readonly struct RegistryScrapingContext : IDisposable
{
    // Takes ownership of everything.
    public required HttpClientContext Http { get; init; }
    public HttpClient HttpClient => Http.Client;
    public required IBrowsingContext Browser { get; init; }

    public static RegistryScrapingContext Create(
        HttpClientContext http,
        TokenRetrievalContext tokenContext)
    {
        var config = Configuration.Default;

        var authHandler = new AuthHandler(tokenContext);
        var requester = new HttpClientRequester(http.Client, authHandler);
        config = config.With<IRequester>(_ => requester);

        config = config.WithDefaultLoader();
        config = config.With<ICookieProvider>(_ => http.CookieProvider);

        var browsingContext = BrowsingContext.New(config);
        return new()
        {
            Http = http,
            Browser = browsingContext,
        };
    }

    public void Dispose()
    {
        Browser.Dispose();
        Http.Dispose();
    }
}

public struct AddLessonsToOnlineRegistryParams()
{
    public required CancellationToken CancellationToken;
    public required Credentials Credentials;
    /// <summary>
    /// Will be initialized to the default config if not provided.
    /// </summary>
    public JsonSerializerOptions? JsonOptions;
    /// <summary>
    /// Will be initialized to the default values if not provided.
    /// </summary>
    public NamesConfig? Names = null;

    public required Session Session;
    public required Schedule Schedule;
    public required IRegistryErrorHandler ErrorHandler;
    public required CourseNameUnifierModule CourseNameUnifier;
    public required GroupParseContext GroupParseContext;
    public required LookupModule LookupModule;
    public required IAllScheduledDateProvider DateProvider;
    public required LessonTimeConfig TimeConfig;
    public CommandProcessingConfig ProcessingFlags = CommandProcessingConfig.DryRun;
}

public static partial class RegistryScraping
{
    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Names ??= NamesConfig.Default;

        using var context = await CreateContext();
        var lists = new MatchingLists();

        var courseLinks = await QueryCourseLinks();
        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(groupsUrl);
            foreach (var group in groups)
            {
                var (existingLessonInstances, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);
                var lessons = MissingLessonDetection.MatchLessonsInSchedule(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

                // Figure out the exact dates the lessons will occur on.
                var times = MissingLessonDetection.GetDateTimesOfScheduledLessons(new()
                {
                    Lessons = lessons,
                    Schedule = p.Schedule,
                    DateProvider = p.DateProvider,
                    TimeConfig = p.TimeConfig,
                });

                var equationCommands = MissingLessonDetection.GetLessonEquationCommands(new()
                {
                    Lists = lists,
                    Schedule = p.Schedule,
                    AllLessons = times,
                    ExistingLessons = existingLessonInstances,
                });
                foreach (var command in equationCommands)
                {
                    if (p.ProcessingFlags.HasDryRun(command.Type))
                    {
                        DryRun(command);
                        continue;
                    }

                    if (p.ProcessingFlags.HasProcess(command.Type))
                    {
                        await HandleCommand(command);
                        continue;
                    }
                }
                continue;

                void DryRun(LessonEquationCommand command)
                {
                    var commandName = command.Type switch
                    {
                        LessonEquationCommandType.Create => "Create",
                        LessonEquationCommandType.Update => "Update",
                        LessonEquationCommandType.Delete => "Delete",
                        _ => throw Unreachable(),
                    };
                    var date = command.HasAll ? command.All.DateTime : command.Existing.DateTime;
                    var dateString = date.ToString("dd.MM.yy");
                    var course = p.Schedule.Get(courseLink.CourseId);
                    var lessonName = course.FullName;
                    Console.WriteLine($"{commandName}: {dateString} - {lessonName}");
                }

                async ValueTask HandleCommand(LessonEquationCommand command)
                {
                    switch (command.Type)
                    {
                        case LessonEquationCommandType.Create:
                        {
                            await Create(command.All);
                            break;
                        }
                        case LessonEquationCommandType.Update:
                        {
                            await Update(command.Existing.EditUri, command.All);
                            break;
                        }
                        case LessonEquationCommandType.Delete:
                        {
                            await HandleExtraLesson(command.Existing);
                            break;
                        }
                        default:
                        {
                            Debug.Fail("Unreachable");
                            break;
                        }
                    }
                }

                async ValueTask HandleExtraLesson(LessonInstanceLink x)
                {
                    var action = p.ErrorHandler.ExtraLessonInstanceFound(x.DateTime);
                    if (action == ExtraLessonInstanceAction.Delete)
                    {
                        await Delete(x.ViewUri);
                    }
                    if (action == ExtraLessonInstanceAction.DeleteWithoutDataLoss)
                    {
                        throw new NotImplementedException("This will need some more scanning");
                    }
                }

                // ReSharper disable once AccessToDisposedClosure
                async Task Update(Uri editUri, LessonInstance lessonInstance)
                {
                    await CreateOrUpdate1(editUri, lessonInstance);
                }

                // ReSharper disable once AccessToDisposedClosure
                async Task Create(LessonInstance lessonInstance)
                {
                    await CreateOrUpdate1(addLessonUri!, lessonInstance);
                }

                async Task CreateOrUpdate(
                    Uri uri,
                    LessonInstance lesson,
                    Schedule schedule,
                    HttpClient client)
                {
                    var doc = await GetHtml(uri);
                    _ = client;
                    await SendUpdatedForm(new()
                    {
                        // HttpClient = client,
                        // Target = uri,
                        Document = doc,
                        Lesson = lesson,
                        Schedule = schedule,
                    });
                }

                Task CreateOrUpdate1(Uri uri, LessonInstance lesson)
                {
                    // ReSharper disable once AccessToDisposedClosure
                    return CreateOrUpdate(uri, lesson, p.Schedule, context.HttpClient);
                }

                async Task Delete(Uri detailsUri)
                {
                    var doc = await GetHtml(detailsUri);
                    var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
                    await form.SubmitAsync();
                }

                static async Task SendUpdatedForm(SendUpdatedFormParams p)
                {
                    var lessonDateBox = (IHtmlInputElement) p.Document.GetElementById("LessonDate")!;
                    lessonDateBox.Value = p.Lesson.DateTime.ToString("yyyy-MM-ddTHH:mm");
                    Debug.Assert(lessonDateBox.Value is not null and not "");

                    var lessonTypeBox = (IHtmlSelectElement) p.Document.GetElementById("LessonMode")!;
                    var lessonType = p.Schedule.Get(p.Lesson.LessonId).Lesson.Type;
                    var lessonName = GetLessonTypeName(lessonType);
                    foreach (var option in lessonTypeBox.Options)
                    {
                        if (lessonName is null)
                        {
                            option.IsSelected = false;
                            continue;
                        }
                        if (option.Value.Equals(lessonName, StringComparison.Ordinal))
                        {
                            option.IsSelected = true;
                            continue;
                        }
                        option.IsSelected = false;
                    }
                    var form = lessonDateBox.Form!;
                    var ret = await form.SubmitAsync();
                    var validationErrors = ret.QuerySelectorAll<IHtmlDivElement>(".validation-summary-errors")
                        .SelectMany(x => x.Children)
                        .SelectMany(x => x.Children)
                        .Select(x => x.Text())
                        .ToArray();
                    if (validationErrors.Length != 0)
                    {
                        throw new InvalidOperationException($"Validation errors: {string.Concat("\n", validationErrors)}");
                    }
                }
            }
        }

        return;

        async Task<(IEnumerable<LessonInstanceLink> Lessons, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(Uri groupUri)
        {
            var doc = await GetHtml(groupUri);
            var lessons = HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = p.ErrorHandler,
            });
            var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
            return (lessons, addLessonLink);
        }

        async Task<IEnumerable<GroupLink>> QueryGroupLinksOfCourse(Uri courseUrl)
        {
            var doc = await GetHtml(courseUrl);
            var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
            {
                Document = doc,
                GroupParseContext = p.GroupParseContext,
                Schedule = p.Schedule,
                ErrorHandler = p.ErrorHandler,
            });
            return ret;
        }

        async Task<IEnumerable<CourseLink>> QueryCourseLinks()
        {
            var doc = await GetHtml(p.Names.LessonsUrl);
            var ret = HtmlSearch.ScanCoursesDocumentForLinks(new()
            {
                Document = doc,
                Session = p.Session,
                ErrorHandler = p.ErrorHandler,
                LookupModule = p.LookupModule,
                CourseNameUnifier = p.CourseNameUnifier,
            });
            return ret;
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<IDocument> GetHtml(Uri uri)
        {
            var document = await context.Browser.OpenAsync(address: uri.ToString(), p.CancellationToken);
            return document;
        }
        async Task<RegistryScrapingContext> CreateContext()
        {
            var http = HttpClientContext.Create();
            try
            {
                var tokenContext = new TokenRetrievalContext(new()
                {
                    Credentials = p.Credentials,
                    Names = p.Names,
                    CookieContainer = http.CookieProvider.Container,
                    HttpClient = http.Client,
                    JsonOptions = p.JsonOptions,
                });
                await tokenContext.InitializeToken(p.CancellationToken);

                var c = RegistryScrapingContext.Create(http, tokenContext);
                return c;
            }
            catch
            {
                http.Dispose();
                throw;
            }
        }
    }
}

public readonly struct CommandProcessingConfig
{
    private int Bits { get; init; }

    public readonly CommandProcessingConfig WithProcess(LessonEquationCommandTypes types)
    {
        var newBits = Bits | ((int) types << ProcessOffset);
        return new()
        {
            Bits = newBits,
        };
    }

    public readonly CommandProcessingConfig WithDryRun(LessonEquationCommandTypes types)
    {
        var newBits = Bits | ((int) types << DryRunOffset);
        return new()
        {
            Bits = newBits,
        };
    }

    private const int ProcessOffset = 0;
    private const int ProcessMask = (1 << (int) LessonEquationCommandType.Count) - 1;
    private const int DryRunOffset = (int) 16;
    private const int DryRunMask = ProcessMask << DryRunOffset;


    public static CommandProcessingConfig None => new();
    public static CommandProcessingConfig Process => None.WithProcess(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig DryRun => None.WithDryRun(LessonEquationCommandTypes.All);

    /// <summary>
    /// Masks out the "process" that are also on "dry run".
    /// </summary>
    /// <value></value>
    public readonly CommandProcessingConfig Normalized
    {
        get
        {
            int dryRunBits = DryRunMask & Bits;
            int doNotProcessMask = dryRunBits >> DryRunOffset;
            int doProcessMask = ~doNotProcessMask;
            int bits = (doProcessMask & Bits) | ((~ProcessMask) & Bits);
            return new()
            {
                Bits = bits,
            };
        }
    }

    public readonly bool HasProcess(LessonEquationCommandType type)
    {
        var mask = 1 << ((int) type + ProcessOffset);
        return (Bits & mask) != 0;
    }

    public readonly bool HasAnyProcess(LessonEquationCommandTypes types)
    {
        var mask = (int) types << ProcessOffset;
        return (Bits & mask) != 0;
    }

    public readonly bool HasDryRun(LessonEquationCommandType type)
    {
        var mask = 1 << ((int) type + DryRunOffset);
        return (Bits & mask) != 0;
    }

    public readonly bool HasAnyDryRun(LessonEquationCommandTypes types)
    {
        var mask = (int) types << DryRunOffset;
        return (Bits & mask) != 0;
    }
}

file struct SendUpdatedFormParams
{
    public required IDocument Document { get; init; }
    public required LessonInstance Lesson { get; init; }
    // public required HttpClient HttpClient { get; init; }
    // public required Uri Target { get; init; }
    public required Schedule Schedule { get; init; }
}

// Just an abstraction over the token context.
file sealed class AuthHandler
{
    private readonly TokenRetrievalContext _tokenContext;

    public AuthHandler(TokenRetrievalContext tokenContext)
    {
        _tokenContext = tokenContext;
    }

    public Task Authenticate(CancellationToken cancellationToken)
    {
        return _tokenContext.QueryTokenAndSave(cancellationToken: cancellationToken);
    }
}

file sealed class HttpClientRequester : BaseRequester
{
    private readonly HttpClient _httpClient;
    private readonly AuthHandler _authHandler;

    public HttpClientRequester(
        HttpClient client,
        AuthHandler authHandler)
    {
        _httpClient = client;
        _authHandler = authHandler;
    }

    public override bool SupportsProtocol(string protocol) => true;

    protected override async Task<IResponse?> PerformRequestAsync(Request request, CancellationToken cancel)
    {
        bool failedOnce = false;
        while (true)
        {
            using var httpRequest = ToHttpRequest(request);
            var httpResponse = await _httpClient.SendAsync(
                request: httpRequest,
                completionOption: HttpCompletionOption.ResponseHeadersRead,
                cancellationToken: cancel);

            bool ShouldAuthenticate()
            {
                if (httpResponse.StatusCode is HttpStatusCode.Unauthorized)
                {
                    return true;
                }
                if (httpRequest.Method == HttpMethod.Get
                    && httpResponse.StatusCode == HttpStatusCode.Redirect)
                {
                    return true;
                }
                return false;
            }

            // if invalid token
            if (ShouldAuthenticate())
            {
                if (failedOnce)
                {
                    throw new InvalidOperationException("Failed to use the password to log in once.");
                }

                await _authHandler.Authenticate(cancel);
                failedOnce = true;
                continue;
            }

            var response = await ToResponse(request.Address, httpResponse, cancel);
            return response;
        }
    }

    private static HttpRequestMessage ToHttpRequest(Request request)
    {
        var method = request.Method switch
        {
            AngleSharp.Io.HttpMethod.Get => System.Net.Http.HttpMethod.Get,
            AngleSharp.Io.HttpMethod.Post => System.Net.Http.HttpMethod.Post,
            AngleSharp.Io.HttpMethod.Put => System.Net.Http.HttpMethod.Put,
            AngleSharp.Io.HttpMethod.Delete => System.Net.Http.HttpMethod.Delete,
            AngleSharp.Io.HttpMethod.Options => System.Net.Http.HttpMethod.Options,
            AngleSharp.Io.HttpMethod.Head => System.Net.Http.HttpMethod.Head,
            AngleSharp.Io.HttpMethod.Trace => System.Net.Http.HttpMethod.Trace,
            AngleSharp.Io.HttpMethod.Connect => System.Net.Http.HttpMethod.Connect,
            _ => throw new ArgumentOutOfRangeException(nameof(request.Method)),
        };

        var ret = new HttpRequestMessage(
            method: method,
            requestUri: new Uri(request.Address.ToString()));
        try
        {
            if (request.Content != null)
            {
                ret.Content = new StreamContent(request.Content);
            }

            foreach (var header in request.Headers)
            {
                bool added = ret.Headers.TryAddWithoutValidation(header.Key, header.Value);
                if (added)
                {
                    continue;
                }

                if (ret.Content is { } c)
                {
                    bool addedToContent = c.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    Debug.Assert(addedToContent);
                    continue;
                }

                Debug.Fail("Some header ignored");
            }
        }
        catch
        {
            ret.Dispose();
        }
        return ret;
    }

    private static async Task<DefaultResponse> ToResponse(
        Url requestUrl,
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var ret = new DefaultResponse();
        try
        {
            ret.Address = requestUrl;
            ret.Content = stream;
            ret.Headers = response.Headers.ToDictionary(x => x.Key, x => string.Join(", ", x.Value));
            ret.StatusCode = response.StatusCode;
        }
        catch
        {
            ((IDisposable) ret).Dispose();
            throw;
        }
        return ret;
    }
}
