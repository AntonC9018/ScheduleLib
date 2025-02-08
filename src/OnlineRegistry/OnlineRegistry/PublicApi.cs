using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;

public enum Session
{
    Ses1,
    Ses2,
}

internal struct RegistryScrapingContext : IDisposable
{
    public required HttpClientHandler Handler { get; init; }
    public required CookieContainer CookieContainer { get; init; }
    public required HttpClient HttpClient { get; init; }
    public required IBrowsingContext Browser { get; init; }

    public static RegistryScrapingContext Create()
    {
        var cookieProvider = new MemoryCookieProvider();
        var cookieContainer = cookieProvider.Container;

        HttpClientHandler? handler = null;
        HttpClient? httpClient = null;

        try
        {
            handler = new HttpClientHandler();
            handler.CookieContainer = cookieContainer;
            handler.UseCookies = true;
            handler.AllowAutoRedirect = false;

            httpClient = new HttpClient(handler);

            var config = Configuration.Default;
            config = config.WithDefaultLoader();
            config = config.With<ICookieProvider>(_ => cookieProvider);
            config = config.With(httpClient);

            var browsingContext = BrowsingContext.New(config);
            return new()
            {
                Handler = handler,
                CookieContainer = cookieContainer,
                HttpClient = httpClient,
                Browser = browsingContext,
            };
        }
        catch
        {
            if (httpClient != null)
            {
                httpClient.Dispose();
            }
            if (handler != null)
            {
                handler.Dispose();
            }
            throw;
        }
    }

    public void Dispose()
    {
        Browser.Dispose();
        HttpClient.Dispose();
        Handler.Dispose();
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
}


public static partial class RegistryScraping
{
    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Names ??= NamesConfig.Default;

        using var context = RegistryScrapingContext.Create();
        var tokenContext = new TokenRetrievalContext(new()
        {
            Credentials = p.Credentials,
            Names = p.Names,
            CookieContainer = context.CookieContainer,
            HttpClient = context.HttpClient,
            JsonOptions = p.JsonOptions,
        });
        await tokenContext.InitializeToken(p.CancellationToken);

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

                async Task Update(Uri editUri, LessonInstance lessonInstance)
                {
                    var doc = await GetHtml(editUri);
                    await SendUpdatedForm(doc, lessonInstance);
                }

                async Task Create(LessonInstance lessonInstance)
                {
                    var doc = await GetHtml(addLessonUri);
                    await SendUpdatedForm(doc, lessonInstance);
                }

                async Task Delete(Uri detailsUri)
                {
                    var doc = await GetHtml(detailsUri);
                    var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
                    await form.SubmitAsync();
                }

                Task SendUpdatedForm(IDocument doc, LessonInstance lessonInstance)
                {
                    var lessonDateBox = (IHtmlInputElement) doc.GetElementById("LessonDate")!;
                    lessonDateBox.ValueAsDate = lessonInstance.DateTime;

                    var lessonTypeBox = (IHtmlSelectElement) doc.GetElementById("LessonMode")!;
                    var lessonType = p.Schedule.Get(lessonInstance.LessonId).Lesson.Type;
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
                    var ret = lessonDateBox.Form!.SubmitAsync(p.CancellationToken);
                    return ret;
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
            bool failedOnce = false;
            while (true)
            {
                using var req = await context.HttpClient.GetAsync(uri, cancellationToken: p.CancellationToken);
                // if invalid token
                if (req.StatusCode
                    is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Redirect)
                {
                    if (failedOnce)
                    {
                        throw new InvalidOperationException("Failed to use the password to log in once.");
                    }

                    await tokenContext.QueryTokenAndSave(p.CancellationToken);
                    failedOnce = true;
                    continue;
                }
                await using var res = await req.Content.ReadAsStreamAsync(p.CancellationToken);
                var document = await context.Browser.OpenAsync(r => r.Content(res).Address(uri));
                return document;
            }
        }
    }
}
