using System.Net;
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

    public async Task<IEnumerable<CourseLink>> Get(Semester semester)
    {
        var lessonAttendanceUrl = new Uri($"{RegistryScraping.BaseUrl}LessonAttendance");
        var doc = await _navigator.GetHtml(lessonAttendanceUrl);
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
                var maybeCourseId = _lookup.Course(courseName.AsMemory(), new()
                {
                    IgnorePunctuation = true,
                });
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

    public GroupsNavigator(
        OnlineRegistryNavigator navigator,
        Schedule schedule,
        GroupParseContext groupParseContext,
        SubGroupNameRemapper remapper,
        SpecializationRegistry specializationRegistry)
    {
        _navigator = navigator;
        _schedule = schedule;
        _groupParseContext = groupParseContext;
        _remapper = remapper;
        _specializationRegistry = specializationRegistry;
    }

    public async Task<IEnumerable<GroupLink>> Get(CourseLink courseLink)
    {
        var doc = await _navigator.GetHtml(courseLink.Url);
        var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = _groupParseContext,
            GroupPartitionResolver = new GroupPartitionResolver(_specializationRegistry),
            SearchGroupId = (ref GroupForSearch group) =>
            {
                group.SubGroupName = _remapper.RemapName(group.SubGroupName);
                var ids = FindGroupMatch(_schedule, group);
                // ReSharper disable once PossibleMultipleEnumeration
                if (ids.Count == 0)
                {
                    // TODO: Do this better
                    _navigator.ErrorHandler.GroupNotFound(group.UnparsedName.Trim());
                }
                return ids;
            },
            ParseErrorHandler = c => _navigator.ErrorHandler.GroupParsingError(c),
        });
        return ret;
    }

    internal static LessonGroups FindGroupMatch(
        Schedule schedule,
        in GroupForSearch g)
    {
        var ret = new LessonGroups();
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

    private static bool IsMatch(Group a, in GroupForSearch b)
    {
        bool facultyMatches = a.Faculty.Name.AsSpan().Equals(
            b.FacultyName.Span,
            StringComparison.OrdinalIgnoreCase);
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
    public Task SubmitLesson(IDocument document) => HtmlSearch.SendForm(document, CancellationToken);
    public async Task SubmitDelete(IDocument document)
    {
        var form = document.QuerySelector<AngleSharp.Html.Dom.IHtmlFormElement>("""form[name="deleteLessonForm"]""")
            ?? throw new InvalidOperationException("Registry delete form is missing.");
        CancellationToken.ThrowIfCancellationRequested();
        var response = await form.SubmitAsync().WaitAsync(CancellationToken) ?? throw new IOException("Registry returned no delete response.");
        if (response.QuerySelector(".validation-summary-errors") is { } errors && !string.IsNullOrWhiteSpace(errors.TextContent))
            throw new RegistrySubmissionRejectedException("Registry rejected deletion: " + errors.TextContent);
    }

    public async Task SubmitGrades(IDocument document, CancellationToken token, Action? onSubmissionStarted = null)
    {
        var form = RegistryGradeForm.Validate(document);
        token.ThrowIfCancellationRequested();
        var expected = form.QuerySelectorAll<IHtmlInputElement>("input[type=text]")
            .Select(input => (Name: input.Name, Value: input.Value)).ToArray();
        var response = await Context.ScrapingContext.SubmitFormOnce(form, token, onSubmissionStarted)
            ?? throw new IOException("Registry returned no grade response.");
        RejectResponse(response);
        // An ordinary form response or navigation away from the edit page must still be
        // confirmed by reading the saved values. A blank/unrecognized page proves nothing.
        if (response.QuerySelector<IHtmlFormElement>("form") is null && response.Url == document.Url)
            throw new IOException("Registry grade submission response could not be confirmed.");
        var saved = await Context.Browser.OpenAsync(document.Url, token);
        var savedInputs = saved.QuerySelectorAll<IHtmlInputElement>("input[type=text]");
        if (saved.StatusCode != HttpStatusCode.OK
            || saved.QuerySelector("input[type=password], .validation-summary-errors") is not null
            || expected.Any(input => savedInputs.Count(actual => actual.Name == input.Name) != 1
                || !savedInputs.Any(actual => actual.Name == input.Name && actual.Value == input.Value)))
            throw new IOException("Registry saved grades could not be confirmed.");

        static void RejectResponse(IDocument response)
        {
            if ((int) response.StatusCode is >= 400 and < 500
                || response.QuerySelector("input[type=password]") is not null
                || response.QuerySelector(".validation-summary-errors") is { } errors && !string.IsNullOrWhiteSpace(errors.TextContent))
                throw new RegistrySubmissionRejectedException("Registry rejected grade submission.");
            if (response.StatusCode != HttpStatusCode.OK)
                throw new IOException("Registry grade submission response could not be confirmed.");
        }
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
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder);
        builder.AddLogging(sp.GetRequiredService<ILoggerFactory>());
        builder.TokenAuth(x =>
        {
            x.PasswordLoginCall(credentials);
            x.Cache();
        });
        var ret = await builder.Build(cancellationToken);
        return new(ret);
    }
}

public static partial class RegistryScraping
{
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
        b.Delay(TimeSpan.FromSeconds(1.0));
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
    public required bool IsWildcard;
    public required LessonGroups Value;
}
