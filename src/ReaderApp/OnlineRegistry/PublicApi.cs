using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using ScheduleLib;

namespace ReaderApp.OnlineRegistry;

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

public static partial class RegistryScraping
{
    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Credentials ??= OnlineRegistryCredentialsHelper.GetCredentials();
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

        var courseLinks = await QueryCourseLinks();
        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(groupsUrl);
            foreach (var group in groups)
            {
                var existingLessonInstances = await QueryLessonInstances(group.Uri);
                var orderedLessonInstances = existingLessonInstances
                    .OrderBy(x => x.DateTime)
                    .ToArray();
                _ = orderedLessonInstances;

                var lessons = MissingLessonDetection.MatchLessons(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

                // Figure out the exact dates that the lessons will occur on.
                var times = MissingLessonDetection.GetDateTimesOfScheduledLessons(new()
                {
                    Lessons = lessons,
                    Schedule = p.Schedule,
                    DateProvider = p.DateProvider,
                    TimeConfig = p.TimeConfig,
                }).OrderBy(x => x.DateTime);
            }
        }

        return;

        async Task<IEnumerable<LessonInstanceLink>> QueryLessonInstances(Uri groupUri)
        {
            var doc = await GetHtml(groupUri);
            var ret = HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = p.ErrorHandler,
            });
            return ret;
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

