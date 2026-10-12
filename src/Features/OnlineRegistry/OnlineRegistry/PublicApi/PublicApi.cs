using System.Net;
using Anton.LayeredData.Retrieval;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Scraping.Common;

namespace ScheduleLib.OnlineRegistry;

public interface ILessonTopics
{
    string? Get(in AttendanceLookupKey key);
}

public sealed class NoLessonTopics : ILessonTopics
{
    private NoLessonTopics()
    {
    }

    public static readonly NoLessonTopics Instance = new();
    public string? Get(in AttendanceLookupKey key)
    {
        return null;
    }
}

[AutoConstructor]
public sealed partial class CoursesNavigator
{
    private readonly OnlineRegistryNavigator _navigator;
    private readonly LookupFacade _lookup;
    private readonly DataProvider<BuiltRegistryConfig> _registryConfig;

    public async Task<IEnumerable<CourseLink>> Get(Semester semester)
    {
        var lessonAttendanceUrl = new Uri($"{RegistryScraping.BaseUrl}LessonAttendance");
        var doc = await _navigator.GetHtml(lessonAttendanceUrl);
        var courseNames = _registryConfig.Get()?.CourseNames ?? new Dictionary<string, string>();
        var ret = HtmlSearch.ScanCoursesDocumentForLinks(new()
        {
            Document = doc,
            Semester = semester,
            FindCourse = courseName =>
            {
                if (courseName.Length == 0)
                {
                    _navigator.ErrorHandler.LessonWithoutName();
                    return null;
                }
                var maybeCourseId = RegistryCourseNameLookup.Find(_lookup, courseName, courseNames,
                    _registryConfig.Get()?.CourseNameMaxDistance ?? 2);
                if (maybeCourseId is not { } courseId)
                {
                    _navigator.ErrorHandler.CourseNotFound(courseName);
                    return null;
                }
                return courseId;
            },
        });
        return ret;
    }
}

public sealed class GroupsNavigator
{
    private readonly OnlineRegistryNavigator _navigator;
    private readonly Schedule _schedule;
    private readonly GroupParseContext _groupParseContext;
    private readonly SubGroupNameRemapper _remapper;
    private readonly SpecializationRegistry _specializationRegistry;
    private readonly DataProvider<BuiltRegistryConfig> _registryConfig;

    public GroupsNavigator(
        OnlineRegistryNavigator navigator,
        Schedule schedule,
        GroupParseContext groupParseContext,
        SubGroupNameRemapper remapper,
        SpecializationRegistry specializationRegistry,
        DataProvider<BuiltRegistryConfig> registryConfig)
    {
        _navigator = navigator;
        _schedule = schedule;
        _groupParseContext = groupParseContext;
        _remapper = remapper;
        _specializationRegistry = specializationRegistry;
        _registryConfig = registryConfig;
    }

    public async Task<IEnumerable<GroupLink>> Get(CourseLink courseLink)
    {
        var doc = await _navigator.GetHtml(courseLink.Url);
        var config = _registryConfig.Get();
        var partitionResolver = new GroupPartitionResolver(_specializationRegistry);
        var registryGroups = new List<GroupForSearch>();
        _ = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = _groupParseContext,
            GroupPartitionResolver = partitionResolver,
            SearchGroupId = (ref GroupForSearch group) =>
            {
                if (config?.SkipAttendance == group.AttendanceMode) return new LessonGroups();
                group.SubGroupName = _remapper.RemapName(group.SubGroupName);
                registryGroups.Add(group);
                return new LessonGroups();
            },
            ParseErrorHandler = _ => { },
        }).ToArray();
        var rules = config?.GroupMatchRules ?? [];
        var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = _groupParseContext,
            GroupPartitionResolver = partitionResolver,
            SearchGroupId = (ref GroupForSearch group) =>
            {
                if (config?.SkipAttendance == group.AttendanceMode) return new LessonGroups();
                group.SubGroupName = _remapper.RemapName(group.SubGroupName);
                var target = group;
                var applicableRules = rules.Where(rule => HasUniqueRegistryAudience(registryGroups, target, rule, partitionResolver)).ToArray();
                var ids = FindGroupMatch(_schedule, courseLink.CourseId, partitionResolver.Resolve(group), group, applicableRules);
                // ReSharper disable once PossibleMultipleEnumeration
                if (ids.Count == 0)
                {
                    // TODO: Do this better
                    _navigator.ErrorHandler.GroupNotFound(_schedule.Get(courseLink.CourseId).FullName,
                        group.UnparsedName.Trim());
                }
                if (!group.SubGroupName.IsEmpty && NormalizePartition(_schedule, ids, partitionResolver.Resolve(group), applicableRules) == GroupPartitionKey.All)
                {
                    // The selected parent already represents this language audience as a whole group.
                    group.SubGroupName = ReadOnlyMemory<char>.Empty;
                }
                return ids;
            },
            ParseErrorHandler = c => _navigator.ErrorHandler.GroupParsingError(_schedule.Get(courseLink.CourseId).FullName, c),
        });
        return ret;
    }

    internal static bool HasUniqueRegistryAudience(
        IReadOnlyList<GroupForSearch> groups, GroupForSearch target,
        RegistryGroupMatchRule rule, GroupPartitionResolver resolver)
    {
        var partition = new GroupPartitionKey(new SubGroup(rule.SubGroup), Specialization.All);
        return groups.Count(group => group.FacultyName.Span.Equals(target.FacultyName.Span, StringComparison.OrdinalIgnoreCase)
            && group.Grade == target.Grade && group.AttendanceMode == target.AttendanceMode
            && group.QualificationType == target.QualificationType && group.IsDual == target.IsDual
            && group.IsAdditionalCourses == target.IsAdditionalCourses
            && resolver.Resolve(group) == partition) == 1;
    }

    internal static GroupPartitionKey NormalizePartition(Schedule schedule, in LessonGroups parents, GroupPartitionKey partition,
        IReadOnlyList<RegistryGroupMatchRule>? rules = null)
    {
        if (partition == new GroupPartitionKey(SpecialSubGroups.Eng, Specialization.All)
            && parents.Count != 0 && parents.All(id => schedule.Get(id).Language == Language.En))
            return GroupPartitionKey.All;
        if (rules is not null && parents.Count != 0)
        {
            foreach (var rule in rules)
            {
                if (rule.MatchByLanguage && rule.AllowParentGroup
                    && partition == new GroupPartitionKey(new SubGroup(rule.SubGroup), Specialization.All)
                    && parents.All(id => schedule.Get(id).Language == rule.Language
                        && schedule.Get(id).Faculty.Name.Equals(rule.Faculty, StringComparison.OrdinalIgnoreCase)))
                    return GroupPartitionKey.All;
            }
        }
        return partition;
    }

    internal static LessonGroups FindGroupMatch(
        Schedule schedule,
        CourseId course,
        GroupPartitionKey partition,
        in GroupForSearch group,
        IReadOnlyList<RegistryGroupMatchRule>? rules = null)
    {
        bool combined = RegistryFacultyLabels.IsCombined(group.FacultyName);
        if (!combined)
        {
            if (rules is not null)
            {
                var relaxed = FindLanguageMatch(schedule, course, partition, group, rules);
                if (relaxed.Count != 0)
                    return relaxed;
            }
            return FindGroupMatch(schedule, group);
        }

        // Combined registry labels need actual course/partition lessons as evidence
        // for their parents. Ordinary groups retain their elective fallback behavior.
        var eligibleParents = new HashSet<GroupId>();
        foreach (var lesson in schedule.EnumerateAllLessons())
        {
            if (lesson.Lesson.Course != course || lesson.Lesson.GroupPartitionKey != partition)
            {
                continue;
            }
            foreach (var parent in lesson.Lesson.Groups)
            {
                eligibleParents.Add(parent);
            }
        }

        var ret = new LessonGroups();
        var languages = new HashSet<Language>();
        foreach (var candidate in schedule.EnumerateGroups())
        {
            if (eligibleParents.Contains(candidate.Id) && IsMatch(candidate.Item, group))
            {
                ret.Add(candidate.Id);
                languages.Add(candidate.Item.Language);
            }
        }
        if (combined && group.Language is null && languages.Count != 1)
        {
            return new LessonGroups();
        }
        return ret;
    }

    internal static LessonGroups FindGroupMatch(
        Schedule schedule,
        in GroupForSearch g)
    {
        var ret = new LessonGroups();
        // Combined labels require course and partition evidence from the scoped overload.
        if (RegistryFacultyLabels.IsCombined(g.FacultyName))
        {
            return ret;
        }
        // TODO: reuse
        // var subGroup = HtmlSearch.SubGroupFromString(g);
        foreach (var g1 in schedule.EnumerateGroups())
        {
            if (IsMatch(g1.Item, g))
            {
                ret.Add(g1.Id);
            }
        }
        return ret;
    }

    private static LessonGroups FindLanguageMatch(
        Schedule schedule, CourseId course, GroupPartitionKey partition,
        in GroupForSearch group, IReadOnlyList<RegistryGroupMatchRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!rule.MatchByLanguage || !group.FacultyName.Span.Equals(rule.Faculty, StringComparison.OrdinalIgnoreCase)
                || partition != new GroupPartitionKey(new SubGroup(rule.SubGroup), Specialization.All))
                continue;

            var cohortSearch = group;
            cohortSearch.GroupNumber = null;
            cohortSearch.Language = null;
            var cohort = schedule.EnumerateGroups().Where(candidate => IsMatch(candidate.Item, cohortSearch)).ToArray();
            // Do not infer shared audiences when an intake has multiple parents of one language.
            if (cohort.Any(candidate => candidate.Item.Language == Language.None)
                || cohort.GroupBy(candidate => candidate.Item.Language).Any(parents => parents.Count() > 1))
                continue;

            var eligible = new HashSet<GroupId>();
            foreach (var lesson in schedule.EnumerateAllLessons())
            {
                if (lesson.Lesson.Course != course)
                    continue;
                foreach (var parent in cohort)
                {
                    if (!lesson.Lesson.Groups.Contains(parent.Id))
                        continue;
                    if (lesson.Lesson.GroupPartitionKey == partition
                        || rule.AllowParentGroup && parent.Item.Language == rule.Language
                        && lesson.Lesson.GroupPartitionKey == GroupPartitionKey.All)
                        eligible.Add(parent.Id);
                }
            }
            if (eligible.Count == 1)
                return new LessonGroups { eligible.Single() };
        }
        return new LessonGroups();
    }

    private static bool IsMatch(Group a, in GroupForSearch b)
    {
        bool facultyMatches = RegistryFacultyLabels.Matches(b.FacultyName, a.Faculty.Name);
        if (!facultyMatches)
        {
            return false;
        }

        if (b.IsDual != (a.AttendanceMode == AttendanceMode.Dual))
        {
            return false;
        }

        if (b.GroupNumber is { } num
            && num != a.GroupNumber)
        {
            return false;
        }

        if (a.AttendanceMode != b.AttendanceMode)
        {
            return false;
        }

        if (a.QualificationType != b.QualificationType)
        {
            return false;
        }

        if (a.Grade != b.Grade)
        {
            return false;
        }

        if (b.Language is { } language)
        {
            if (a.Language != language)
            {
                return false;
            }
        }

        return true;
    }

}

[AutoConstructor]
public sealed partial class OnlineRegistryNavigator : IRegistrySyncNavigator, IRegistryGradeNavigator
{
    public readonly IRegistryErrorHandler ErrorHandler;
    public readonly RegistryScrapingContext Context;
    public readonly IServiceProvider ServiceProvider;
    public readonly CancellationToken CancellationToken;

    public Task<IEnumerable<CourseLink>> GetCourses(Semester semester) => Courses().Get(semester);
    public Task<IEnumerable<GroupLink>> GetGroups(CourseLink course) => Groups().Get(course);
    public Task SubmitLesson(IDocument document) => SubmitLesson(document, null);
    public async Task SubmitLesson(IDocument document, Action? onSubmissionStarted)
    {
        var form = HtmlSearch.GetLessonForm(document);
        try
        {
            await SubmitMutation(form, "lesson submission", onSubmissionStarted);
        }
        catch (FormSubmissionRedirectException error) when (error.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther)
        {
            // POST/redirect/GET is normal registry behavior. Confirm persisted values
            // through reads, without allowing AngleSharp to replay the mutation.
            await ConfirmSavedLesson(document, form, error);
        }
    }

    private async Task ConfirmSavedLesson(IDocument document, IHtmlFormElement form, FormSubmissionRedirectException redirect)
    {
        var origin = new Uri(document.Url);
        var listLinks = document.QuerySelectorAll<IHtmlAnchorElement>("a[href]")
            .Select(anchor => new Uri(anchor.Href))
            .Where(uri => SameOrigin(origin, uri) && uri.AbsolutePath.Contains("/LessonAttendance/LessonsList/", StringComparison.Ordinal))
            .Distinct().ToArray();
        if (redirect.Location is null || listLinks.Length != 1)
            throw new IOException("Registry lesson confirmation destination is unavailable.");
        var destination = new Uri(origin, redirect.Location);
        if (destination != listLinks[0])
            throw new IOException("Registry lesson confirmation destination is unexpected.");

        var expectedDate = form.QuerySelector<IHtmlInputElement>("#LessonDate")?.Value;
        var expectedMode = form.QuerySelector<IHtmlSelectElement>("#LessonMode")?.Value;
        var expectedTopic = form.QuerySelector<IHtmlTextAreaElement>("#LessonTopic")?.Value;
        if (!DateTime.TryParse(expectedDate, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var date) || expectedMode is null || expectedTopic is null)
            throw new IOException("Registry lesson confirmation fields are unavailable.");
        var expectedAttendance = form.QuerySelectorAll<IHtmlInputElement>("input[name]")
            .Where(input => input.Name?.EndsWith(".NotaStr", StringComparison.Ordinal) == true)
            .Select(input => (Name: input.Name, Value: input.Value)).ToArray();

        var list = await GetHtml(destination);
        RequireConfirmationPage(list);
        var table = list.QuerySelector<IHtmlTableElement>("table")
            ?? throw new IOException("Registry lesson confirmation table is missing.");
        var dateText = date.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        var editLinks = table.Rows.Where(row => row.Cells.Length >= 3 && row.Cells[0].TextContent.Contains(dateText, StringComparison.Ordinal))
            .SelectMany(row => row.Cells[2].QuerySelectorAll<IHtmlAnchorElement>("a[href]"))
            .Select(anchor => new Uri(anchor.Href))
            .Where(uri => SameOrigin(origin, uri) && uri.AbsolutePath.Contains("/LessonAttendance/Edit/", StringComparison.Ordinal))
            .Distinct().ToArray();
        var matches = 0;
        foreach (var edit in editLinks)
        {
            var saved = await GetHtml(edit);
            RequireConfirmationPage(saved);
            var savedForm = HtmlSearch.GetLessonForm(saved);
            var savedInputs = savedForm.QuerySelectorAll<IHtmlInputElement>("input[name]");
            if (DateTime.TryParse(savedForm.QuerySelector<IHtmlInputElement>("#LessonDate")?.Value,
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var savedDate)
                && savedDate == date
                && savedForm.QuerySelector<IHtmlSelectElement>("#LessonMode")?.Value == expectedMode
                && savedForm.QuerySelector<IHtmlTextAreaElement>("#LessonTopic")?.Value.ReplaceLineEndings("\n") == expectedTopic.ReplaceLineEndings("\n")
                && expectedAttendance.All(input => savedInputs.Count(actual => actual.Name == input.Name) == 1
                    && savedInputs.Any(actual => actual.Name == input.Name && actual.Value == input.Value)))
                matches++;
        }
        if (matches != 1)
            throw new IOException("Registry saved lesson values could not be uniquely confirmed.");

        static bool SameOrigin(Uri first, Uri second) => first.Scheme == second.Scheme && first.Host == second.Host && first.Port == second.Port;
        static void RequireConfirmationPage(IDocument page)
        {
            // A failed read cannot prove rejection of the already-attempted POST.
            if (page.StatusCode != HttpStatusCode.OK || IsLoginAddress(page.Url)
                || page.QuerySelector("input[type=password], .validation-summary-errors") is not null)
                throw new IOException("Registry lesson confirmation read failed.");
        }
    }

    public Task SubmitDelete(IDocument document) => SubmitDelete(document, null);
    public Task SubmitDelete(IDocument document, Action? onSubmissionStarted)
    {
        var form = document.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")
            ?? throw new InvalidOperationException("Registry delete form is missing.");
        return SubmitDeletion();

        async Task SubmitDeletion()
        {
            try { await SubmitMutation(form, "deletion", onSubmissionStarted); }
            catch (FormSubmissionRedirectException error) when (error.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther)
            {
                var origin = new Uri(document.Url);
                var listLinks = document.QuerySelectorAll<IHtmlAnchorElement>("a[href]")
                    .Select(anchor => new Uri(anchor.Href))
                    .Where(uri => uri.Scheme == origin.Scheme && uri.Host == origin.Host && uri.Port == origin.Port
                        && uri.AbsolutePath.Contains("/LessonAttendance/LessonsList/", StringComparison.Ordinal))
                    .Distinct().ToArray();
                var lessonId = form.QuerySelector<IHtmlInputElement>("input[name=lessonId]")?.Value;
                if (error.Location is null || listLinks.Length != 1
                    || string.IsNullOrWhiteSpace(lessonId) || !lessonId.All(char.IsAsciiDigit)
                    || !origin.AbsolutePath.EndsWith("/LessonAttendance/LessonDetail/" + lessonId, StringComparison.Ordinal))
                    throw new IOException("Registry deletion confirmation identity is unavailable.");
                var destination = new Uri(origin, error.Location);
                if (destination != listLinks[0])
                    throw new IOException("Registry deletion confirmation destination is unexpected.");
                var list = await GetHtml(destination);
                if (list.StatusCode != HttpStatusCode.OK || IsLoginAddress(list.Url)
                    || list.QuerySelector("input[type=password], .validation-summary-errors") is not null)
                    throw new IOException("Registry deletion confirmation read failed.");
                var table = list.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new IOException("Registry deletion confirmation table is missing.");
                if (list.QuerySelectorAll<IHtmlAnchorElement>("a[href]").All(anchor =>
                        !anchor.Href.Contains("/LessonAttendance/CreateLesson/", StringComparison.Ordinal))
                    || table.Rows.Length == 0)
                    throw new IOException("Registry deletion confirmation page is unrecognized.");
                if (table.QuerySelectorAll<IHtmlAnchorElement>("a[href]").Any(anchor =>
                        new Uri(anchor.Href).AbsolutePath is var path
                        && (path.EndsWith("/LessonDetail/" + lessonId, StringComparison.Ordinal)
                            || path.EndsWith("/Edit/" + lessonId, StringComparison.Ordinal))))
                    throw new RegistryDeletionRefusedException();
            }
        }
    }

    private async Task SubmitMutation(IHtmlFormElement form, string operation, Action? onSubmissionStarted)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var response = await SubmitOnce(form, CancellationToken, onSubmissionStarted);
        RejectResponse(response, operation);
    }

    private async Task<IDocument> SubmitOnce(IHtmlFormElement form, CancellationToken token, Action? onSubmissionStarted)
    {
        if (!form.Method.Equals("post", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Registry mutation form must use POST.");
        try
        {
            return await Context.ScrapingContext.SubmitFormOnce(form, token, onSubmissionStarted)
                ?? throw new IOException("Registry returned no submission response.");
        }
        catch (FormSubmissionRedirectException error) when (error.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther
            && error.Location is { } location && IsLoginAddress(location.ToString()))
        {
            throw new RegistrySubmissionRejectedException("Registry submission redirected to login.");
        }
    }

    private static bool IsLoginAddress(string address)
    {
        var path = address.Split('?', '#')[0].TrimEnd('/');
        return path.EndsWith("/login", StringComparison.OrdinalIgnoreCase);
    }

    private static void RejectResponse(IDocument response, string operation)
    {
        if ((int) response.StatusCode is >= 400 and < 500
            || response.QuerySelector("input[type=password]") is not null
            || IsLoginAddress(response.Url)
            || response.QuerySelectorAll(".validation-summary-errors").Any(errors => !string.IsNullOrWhiteSpace(errors.TextContent)))
            throw new RegistrySubmissionRejectedException($"Registry rejected {operation}.");
        if (response.StatusCode != HttpStatusCode.OK)
            throw new IOException($"Registry {operation} response could not be confirmed.");
    }

    public async Task SubmitGrades(IDocument document, CancellationToken token, Action? onSubmissionStarted = null)
    {
        var form = RegistryGradeForm.Validate(document);
        token.ThrowIfCancellationRequested();
        var expected = form.QuerySelectorAll<IHtmlInputElement>("input[type=text]")
            .Select(input => (Name: input.Name, Value: input.Value)).ToArray();
        try
        {
            var response = await SubmitOnce(form, token, onSubmissionStarted);
            RejectResponse(response, "grade submission");
            // An ordinary form response or navigation away from the edit page must still be
            // confirmed by reading the saved values. A blank/unrecognized page proves nothing.
            if (response.QuerySelector<IHtmlFormElement>("form") is null && response.Url == document.Url)
                throw new IOException("Registry grade submission response could not be confirmed.");
        }
        catch (FormSubmissionRedirectException error) when (error.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.SeeOther)
        {
            // Confirm at the known edit URL below. A failed confirmation read cannot
            // establish rejection of the already-attempted POST.
        }
        var saved = await Context.Browser.OpenAsync(document.Url, token);
        var savedInputs = saved.QuerySelectorAll<IHtmlInputElement>("input[type=text]");
        if (saved.StatusCode != HttpStatusCode.OK
            || saved.QuerySelector("input[type=password], .validation-summary-errors") is not null
            || expected.Any(input => savedInputs.Count(actual => actual.Name == input.Name) != 1
                || !savedInputs.Any(actual => actual.Name == input.Name && actual.Value == input.Value)))
            throw new IOException("Registry saved grades could not be confirmed.");
    }

    public CoursesNavigator Courses()
    {
        return ActivatorUtilities.CreateInstance<CoursesNavigator>(ServiceProvider, this);
    }

    public GroupsNavigator Groups()
    {
        return ActivatorUtilities.CreateInstance<GroupsNavigator>(ServiceProvider, this);
    }

    public async Task<IDocument> GetHtml(Uri uri)
    {
        var document = await Context.Browser.OpenAsync(
            address: uri.ToString(),
            CancellationToken);
        return document;
    }
}

public readonly record struct RegistryScrapingContext(
    ScrapingContext ScrapingContext) : IDisposable
{
    public IBrowsingContext Browser => ScrapingContext.Browser;
    public HttpClient HttpClient => ScrapingContext.HttpClient;
    public IServiceProvider Services => ScrapingContext.BuilderServices!;

    public bool IsNull => this == default;

    public void Dispose()
    {
        ScrapingContext.Dispose();
    }

    public static async Task<RegistryScrapingContext> Create(
        IServiceProvider sp,
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder, ResolveHttpCallDelay(sp));
        builder.AddLogging(sp.GetRequiredService<ILoggerFactory>());
        builder.TokenAuth(x =>
        {
            x.PasswordLoginCall(credentials);
            x.Cache();
        });
        var ret = await builder.Build(cancellationToken);
        return new(ret);
    }

    private static TimeSpan ResolveHttpCallDelay(IServiceProvider sp)
    {
        var provider = sp.GetService(typeof(DataProvider<BuiltRegistryConfig>)) as DataProvider<BuiltRegistryConfig>;
        if (provider is null)
            return BuiltRegistryConfig.DefaultHttpCallDelay;
        // Null means registry not enabled at this path; mapping errors (e.g. negative delay) propagate.
        return provider.Get()?.HttpCallDelay ?? BuiltRegistryConfig.DefaultHttpCallDelay;
    }
}

public static partial class RegistryScraping
{
    internal static readonly TimeSpan RegistryRequestSpacing = BuiltRegistryConfig.DefaultHttpCallDelay;

    /// <summary>Dedicated read-only history transport; authentication uses the application's existing login builder.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The returned context owns the transport; construction failures dispose all allocated objects.")]
    public static async Task<ScrapingContext> CreateHistoryContext(Credentials credentials, CancellationToken token, TimeSpan? delay = null)
    {
        var spacing = delay ?? RegistryRequestSpacing;
        if (spacing < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        HttpMessageHandler? handler = null;
        HttpClient? client = null;
        HttpClientContext? transport = null;
        try
        {
            var cookieProvider = new AngleSharp.Io.MemoryCookieProvider();
            handler = new HttpClientHandler { CookieContainer = cookieProvider.Container, AllowAutoRedirect = false };
            var guard = new RegistryHistoryRequestHandler(handler, new(BaseUrl + "LessonAttendance"),
                DefaultTokenNames.LoginUrl, spacing);
            handler = guard;
            client = new HttpClient(guard);
            transport = new HttpClientContext(cookieProvider, client, guard);
            var builder = new ScrapingContextBuilder();
            // The history transport applies spacing to every request, including login.
            AddDefaultConfigWithoutHandlers(builder, TimeSpan.Zero);
            builder.AddLogging(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            builder.TokenAuth(x => x.PasswordLoginCall(credentials));
            var context = await builder.Build(token, transport);
            guard.CompleteAuthentication();
            return context;
        }
        catch
        {
            transport?.Dispose();
            client?.Dispose();
            handler?.Dispose();
            throw;
        }
    }

    public const string CredentialsConfigKey = "Curmanschii Anton:Registry";

    public static OnlineRegistryNavigator Navigator(
        this RegistryScrapingContext context,
        IServiceProvider sp,
        CancellationToken cancellationToken)
    {
        return new OnlineRegistryNavigator(
            sp.GetRequiredService<IRegistryErrorHandler>(),
            context,
            sp,
            cancellationToken);
    }

    internal static void AddDefaultConfigWithoutHandlers(ScrapingContextBuilder b)
    {
        AddDefaultConfigWithoutHandlers(b, RegistryRequestSpacing);
    }

    internal static void AddDefaultConfigWithoutHandlers(ScrapingContextBuilder b, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(delay));
        b.Delay(delay);
        b.AddConfig(DefaultTokensStorageConfig);
        b.AddConfig(DefaultPasswordLoginFieldNames);
        b.AddConfig(DefaultTokenNames);
    }

    public const string BaseUrl = "http://crd.usm.md/studregistry/";
    private static readonly TokenNamesConfig DefaultTokenNames = new()
    {
        BaseUrl = new Uri(BaseUrl),
        LoginUrl = new Uri("http://crd.usm.md/studregistry/Account/Login"),
        TokenCookieName = "ForDecanat",
    };
    private static readonly TokensStorageConfig DefaultTokensStorageConfig = new()
    {
        TokensFile = "tokens.json",
    };
    private static readonly PasswordLoginFieldNames DefaultPasswordLoginFieldNames = new()
    {
        Login = "UserLogin",
        Password = "UserPassword",
    };
}


public record struct StudentsLookupKey
{
    public LessonGroups Groups;
    public GroupPartitionKey GroupPartition;
    public CourseId CourseId;
    public LessonType LessonType;

    public StudentsLookupKey(
        in LessonGroups groups,
        GroupPartitionKey groupPartition,
        CourseId courseId,
        LessonType lessonType)
    {
        Groups = groups;
        GroupPartition = groupPartition;
        CourseId = courseId;
        LessonType = lessonType;
    }

    public StudentsLookupKey WithGroups(in LessonGroups g)
    {
        return this with
        {
            Groups = g,
        };
    }
}

public readonly record struct AttendanceLookupKey
{
    public readonly FoundGroups Groups;
    public readonly GroupPartitionKey GroupPartition;
    public readonly CourseId CourseId;
    public LessonType LessonType { get; init; }

    // The program may use any of this info to get the right data.
    public int DayIndex { get; init; }
    public LessonGroups? ActiveGroups { get; init; }
    public readonly DateTime DateTime;

    public AttendanceLookupKey(
        in FoundGroups groups,
        GroupPartitionKey groupPartition,
        CourseId courseId,
        LessonType lessonType,
        int dayIndex,
        DateTime dateTime)
    {
        Groups = groups;
        GroupPartition = groupPartition;
        CourseId = courseId;
        LessonType = lessonType;
        DayIndex = dayIndex;
        DateTime = dateTime;
    }
}

public record struct FoundGroups
{
    /// <summary>Confirmed registry-only combination of existing faculty cohorts.</summary>
    public bool IsCombined;
    public required bool IsWildcard;
    public required LessonGroups Value;
}
