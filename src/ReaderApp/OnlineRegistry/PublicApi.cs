using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using ReaderApp.OnlineRegistry;
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
                var (existingLessonInstances, addLessonUri) = await QueryLessonInstances(group.Uri);
                var orderedLessonInstances = existingLessonInstances
                    .OrderBy(x => x.DateTime);

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

                var lists = new MatchingLists();

                // ReSharper disable once GenericEnumeratorNotDisposed
                using var allEnumerator = times.GetEnumerator().RememberIsDone();
                // ReSharper disable once GenericEnumeratorNotDisposed
                using var existingEnumerator = orderedLessonInstances.GetEnumerator().RememberIsDone();

                allEnumerator.MoveNext();
                existingEnumerator.MoveNext();

                while (true)
                {
                    if (allEnumerator.IsDone)
                    {
                        break;
                    }
                    if (existingEnumerator.IsDone)
                    {
                        break;
                    }

                    var all = allEnumerator.Current;
                    var existing = existingEnumerator.Current;

                    var allDate = DateOnly.FromDateTime(all.DateTime);
                    var existingDate = DateOnly.FromDateTime(existing.DateTime);
                    var smallestDate = allDate < existingDate ? allDate : existingDate;

                    if (allDate == smallestDate)
                    {
                        while (true)
                        {
                            lists.AllToday.Add(all);
                            if (!allEnumerator.MoveNext())
                            {
                                break;
                            }
                            var nextDate = DateOnly.FromDateTime(allEnumerator.Current.DateTime);
                            if (nextDate != smallestDate)
                            {
                                break;
                            }
                            lists.AllToday.Add(allEnumerator.Current);
                        }
                    }
                    if (existingDate == smallestDate)
                    {
                        while (true)
                        {
                            lists.ExistingToday.Add(existing);
                            if (!existingEnumerator.MoveNext())
                            {
                                break;
                            }
                            var nextDate = DateOnly.FromDateTime(existingEnumerator.Current.DateTime);
                            if (nextDate != smallestDate)
                            {
                                break;
                            }
                            lists.ExistingToday.Add(existingEnumerator.Current);
                        }
                    }

                    Debug.Assert(!TwoLessonAtSameTime());
                    var matchingContext = lists.CreateContext();

                    UseUpExactMatches();
                    AddPartialMatches();

                    // TODO: Run the updates in parallel.
                    foreach (var match in lists.Matches)
                    {
                        var t = matchingContext.Get(match);
                        await Update(t.Existing.EditUri, t.All);
                    }
                    foreach (var x in matchingContext.UnusedExisting())
                    {
                        await HandleExtraLesson(x);
                    }
                    foreach (var x in matchingContext.UnusedAll())
                    {
                        await Create(x);
                    }
                    lists.Clear();

                    continue;

                    void AddPartialMatches()
                    {
                        foreach (var x in matchingContext.IteratePotentialMappings())
                        {
                            if (!x.DatesEqual())
                            {
                                continue;
                            }
                            matchingContext.AddMatch(x.Mapping);
                        }
                        foreach (var x in matchingContext.IteratePotentialMappings())
                        {
                            if (!x.LessonTypesEqual(p.Schedule))
                            {
                                continue;
                            }
                            matchingContext.AddMatch(x.Mapping);
                        }
                    }

                    void UseUpExactMatches()
                    {
                        foreach (var x in matchingContext.IteratePotentialMappings())
                        {
                            if (!x.DatesEqual())
                            {
                                continue;
                            }
                            if (!x.LessonTypesEqual(p.Schedule))
                            {
                                continue;
                            }

                            matchingContext.UseUpMatch(x.Mapping);
                        }
                    }

                    bool TwoLessonAtSameTime()
                    {
                        var dates = new HashSet<DateTime>();
                        foreach (var a in lists.AllToday)
                        {
                            if (!dates.Add(a.DateTime))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                }

                while (!allEnumerator.IsDone)
                {
                    await Create(allEnumerator.Current);
                    allEnumerator.MoveNext();
                }

                while (!existingEnumerator.IsDone)
                {
                    await HandleExtraLesson(existingEnumerator.Current);
                    existingEnumerator.MoveNext();
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

        async Task<(IEnumerable<LessonInstanceLink> Lessons, Uri AddLessonLink)> QueryLessonInstances(Uri groupUri)
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


internal struct Matches
{
    public Matches(int allLen, int existingLen)
    {
        if (allLen > BitArray32.MaxLength)
        {
            throw new NotSupportedException("At most 32 lessons per day are supported.");
        }
        if (existingLen > BitArray32.MaxLength)
        {
            throw new NotSupportedException("At most 32 lessons per day are supported.");
        }
        AllMapped = BitArray32.Empty(allLen);
        ExistingMapped = BitArray32.Empty(existingLen);
    }

    public BitArray32 AllMapped;
    public BitArray32 ExistingMapped;

    public void Set(Mapping mapping)
    {
        Debug.Assert(!AllMapped.IsSet(mapping.AllIndex));
        Debug.Assert(!ExistingMapped.IsSet(mapping.ExistingIndex));
        AllMapped.Set(mapping.AllIndex);
        ExistingMapped.Set(mapping.ExistingIndex);
    }
}

internal readonly record struct Mapping(int AllIndex, int ExistingIndex);

internal static class MatchingContextHelper
{
    public static MatchingContext.PotentialMappingEnumerable IteratePotentialMappings(
        this ref MatchingContext c)
    {
        return new(ref c);
    }

    public static MatchingContext CreateContext(this MatchingLists lists)
    {
        return new(lists);
    }
}

internal readonly struct MatchingLists()
{
    public readonly List<Mapping> Matches = new();
    public readonly List<LessonInstance> AllToday = new();
    public readonly List<LessonInstanceLink> ExistingToday = new();

    public readonly void Clear()
    {
        Matches.Clear();
        AllToday.Clear();
        ExistingToday.Clear();
    }
}

internal struct MatchingContext
{
    private readonly MatchingLists _lists;
    private Matches _matches;

    public MatchingContext(MatchingLists lists)
    {
        _lists = lists;
        _matches = new(lists.AllToday.Count, _lists.ExistingToday.Count);
    }

    public void AddMatch(Mapping m)
    {
        _lists.Matches.Add(m);
        UseUpMatch(m);
    }

    public void UseUpMatch(Mapping m)
    {
        _matches.Set(m);
    }

    public readonly Item Get(Mapping m)
    {
        return new()
        {
            Mapping = m,
            All = _lists.AllToday[m.AllIndex],
            Existing = _lists.ExistingToday[m.ExistingIndex],
        };
    }

    public readonly UnusedEnumerable<LessonInstance> UnusedAll()
    {
        return new(_matches.AllMapped, _lists.AllToday);
    }

    public readonly UnusedEnumerable<LessonInstanceLink> UnusedExisting()
    {
        return new(_matches.ExistingMapped, _lists.ExistingToday);
    }

    public struct Item
    {
        public required Mapping Mapping;
        public required LessonInstance All;
        public required LessonInstanceLink Existing;

        public readonly bool DatesEqual() => All.DateTime.Date == Existing.DateTime.Date;
        public readonly bool LessonTypesEqual(Schedule s)
        {
            if (Existing.LessonType == LessonType.Unspecified)
            {
                return true;
            }

            var lesson = s.Get(All.LessonId).Lesson;
            return lesson.Type == Existing.LessonType;
        }
    }

    public readonly ref struct PotentialMappingEnumerable
    {
        private readonly ref MatchingContext _context;
        public PotentialMappingEnumerable(ref MatchingContext context) => _context = ref context;
        public PotentialMappingEnumerator GetEnumerator() => new(ref _context);
    }

    public ref struct PotentialMappingEnumerator
    {
        private int _allIndex;
        private int _existingIndex;
        private readonly ref MatchingContext _context;

        public PotentialMappingEnumerator(ref MatchingContext context)
        {
            _allIndex = -1;
            _existingIndex = 0;
            _context = ref context;
        }

        public Item Current => _context.Get(new(AllIndex: _allIndex, ExistingIndex: _existingIndex));

        public bool MoveNext()
        {
            var unusedAllIndex = _context._matches.AllMapped.GetUnsetAtOrAfter(_allIndex);

            // There's no more available bits to iterate
            if (unusedAllIndex == -1)
            {
                return false;
            }
            if (_context._matches.ExistingMapped.AreAllSet)
            {
                return false;
            }

            // The current All index is unused.
            if (unusedAllIndex != _allIndex)
            {
                _allIndex = unusedAllIndex;
                _existingIndex = _context._matches.ExistingMapped.UnsetBitIndicesLowToHigh.First();
                return true;
            }

            var nextUnusedExistingIndex = _context._matches.ExistingMapped.GetUnsetAfter(_existingIndex);

            // There's no more unused existing indices.
            if (nextUnusedExistingIndex == -1)
            {
                int nextAll = _context._matches.AllMapped.GetUnsetAfter(_allIndex);
                if (nextAll == -1)
                {
                    return false;
                }
                _allIndex = nextAll;
                _existingIndex = _context._matches.ExistingMapped.UnsetBitIndicesLowToHigh.First();
                return true;
            }

            _existingIndex = nextUnusedExistingIndex;
            return true;

        }
    }

    public readonly struct UnusedEnumerable<T>
    {
        private readonly BitArray32 _bits;
        private readonly List<T> _items;
        public UnusedEnumerable(BitArray32 bits, List<T> items)
        {
            _bits = bits;
            _items = items;
        }
        public UnusedEnumerator<T> GetEnumerator() => new(_bits, _items);
    }

    public struct UnusedEnumerator<T>
    {
        private SetBitIndicesEnumerator _e;
        private readonly List<T> _items;

        public UnusedEnumerator(BitArray32 isUsed, List<T> items)
        {
            _e = isUsed.UnsetBitIndicesLowToHigh.GetEnumerator();
            _items = items;
        }

        public T Current => _items[_e.Current];
        public bool MoveNext() => _e.MoveNext();
    }
}
