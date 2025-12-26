using AngleSharp;
using AngleSharp.Dom;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
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

public sealed class CoursesNavigator
{
    private readonly OnlineRegistryNavigator _navigator;
    private readonly CourseNameUnifierModule _unifier;
    private readonly LookupModule _lookup;

    public CoursesNavigator(
        OnlineRegistryNavigator navigator,
        CourseNameUnifierModule unifier,
        LookupModule lookup)
    {
        _unifier = unifier;
        _lookup = lookup;
        _navigator = navigator;
    }

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
                var maybeCourseId = _unifier.Find(new()
                {
                    CourseName = courseName,
                    Lookup = _lookup,
                    ParseOptions = new()
                    {
                        IgnorePunctuation = true,
                    },
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
    private readonly SubGroupsByGroup _subGroupsMap;

    public GroupsNavigator(
        OnlineRegistryNavigator navigator,
        Schedule schedule,
        GroupParseContext groupParseContext)
    {
        _navigator = navigator;
        _schedule = schedule;
        _groupParseContext = groupParseContext;
        _subGroupsMap = _schedule.SubGroupsByGroup();
    }

    public async Task<IEnumerable<GroupLink>> Get(CourseLink courseLink)
    {
        var doc = await _navigator.GetHtml(courseLink.Url);
        var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = _groupParseContext,
            SearchGroupId = (in GroupForSearch group) =>
            {
                var ids = FindGroupMatch(_schedule, _subGroupsMap, group);
                // ReSharper disable once PossibleMultipleEnumeration
                if (ids.Count == 0)
                {
                    // TODO: Do this better
                    _navigator.ErrorHandler.GroupNotFound(group.UnparsedName.Trim());
                }
                return ids;
            },
        });
        return ret;
    }

    internal static LessonGroups FindGroupMatch(
        Schedule schedule,
        SubGroupsByGroup subGroupsMap,
        in GroupForSearch g)
    {
        var ret = new LessonGroups();
        // TODO: reuse
        // var subGroup = HtmlSearch.SubGroupFromString(g);
        foreach (var g1 in schedule.EnumerateGroups())
        {
            // if (subGroup != SubGroup.All)
            // {
            //     if (!subGroupsMap[g1.Id].Contains(subGroup))
            //     {
            //         continue;
            //     }
            // }
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
public sealed partial class OnlineRegistryNavigator
{
    public readonly IRegistryErrorHandler ErrorHandler;
    public readonly RegistryScrapingContext Context;
    public readonly IServiceProvider ServiceProvider;
    public readonly CancellationToken CancellationToken;

    public CoursesNavigator Courses()
    {
        var sp = Context.Services;
        return ActivatorUtilities.CreateInstance<CoursesNavigator>(sp, this);
    }

    public GroupsNavigator Groups()
    {
        var sp = Context.Services;
        return ActivatorUtilities.CreateInstance<GroupsNavigator>(sp, this);
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

    public void Dispose()
    {
        ScrapingContext.Dispose();
    }

    public static async Task<RegistryScrapingContext> Create(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder);
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
    public const string CredentialsConfigKey = "Registry";

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
        b.Delay(TimeSpan.FromSeconds(0.5));
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


public readonly record struct StudentsLookupKey
{
    public readonly LessonGroups Groups;
    public readonly SubGroup SubGroup;
    public readonly CourseId CourseId;
    public readonly LessonType LessonType;

    public StudentsLookupKey(
        in LessonGroups groups,
        SubGroup subGroup,
        CourseId courseId,
        LessonType lessonType)
    {
        Groups = groups;
        SubGroup = subGroup;
        CourseId = courseId;
        LessonType = lessonType;
    }

    public StudentsLookupKey WithGroups(in LessonGroups g)
    {
        return new StudentsLookupKey(
            g,
            SubGroup,
            CourseId,
            LessonType);
    }
}

public readonly record struct AttendanceLookupKey
{
    public readonly FoundGroups Groups;
    public readonly SubGroup SubGroup;
    public readonly CourseId CourseId;
    public readonly LessonType LessonType;

    // The program may use any of this info to get the right data.
    public readonly int DayIndex;
    public readonly DateTime DateTime;

    public AttendanceLookupKey(
        in FoundGroups groups,
        SubGroup subGroup,
        CourseId courseId,
        LessonType lessonType,
        int dayIndex,
        DateTime dateTime)
    {
        Groups = groups;
        SubGroup = subGroup;
        CourseId = courseId;
        LessonType = lessonType;
        DayIndex = dayIndex;
        DateTime = dateTime;
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

    public readonly CommandProcessingConfig WithLog(LessonEquationCommandTypes types)
    {
        var newBits = Bits | ((int) types << LogOffset);
        return new()
        {
            Bits = newBits,
        };
    }

    private const int ProcessOffset = 0;
    private const int ProcessMask = (1 << (int) LessonEquationCommandType.Count) - 1;
    private const int DryRunOffset = 8;
    private const int DryRunMask = ProcessMask << DryRunOffset;
    private const int LogOffset = 16;
    private const int LogMask = ProcessMask << LogOffset;
    private const int ValueMask = ProcessMask;


    public static CommandProcessingConfig None => new();
    public static CommandProcessingConfig Process => None.WithProcess(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig DryRun => None.WithDryRun(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig Log => None.WithLog(LessonEquationCommandTypes.All);

    /// <summary>
    /// Masks out the "process" that are also on "dry run".
    /// </summary>
    /// <value></value>
    public readonly CommandProcessingConfig Normalized
    {
        get
        {
            int dryRunBits = DryRunMask & Bits;
            int doNotProcessMask = ((dryRunBits >> DryRunOffset) & ValueMask) << ProcessOffset;
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

    public readonly bool HasLog(LessonEquationCommandType type)
    {
        var mask = 1 << ((int) type + LogOffset);
        return (Bits & mask) != 0;
    }

    public readonly bool HasAnyLog(LessonEquationCommandTypes types)
    {
        var mask = (int) types << LogOffset;
        return (Bits & mask) != 0;
    }
}

public record struct FoundGroups
{
    public required bool IsWildcard;
    public required LessonGroups Value;
}
