using System.Diagnostics;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Scraping.Common;

namespace ScheduleLib.OnlineRegistry;


public struct AddLessonsToOnlineRegistryParams()
{
    public required CancellationToken CancellationToken;
    public required Semester Semester;
    public required Schedule Schedule;

    public required IRegistryErrorHandler ErrorHandler;
    public required CourseNameUnifierModule CourseNameUnifier;
    public required GroupParseContext GroupParseContext;
    public required LookupModule LookupModule;
    public required IAllScheduledDateProvider DateProvider;
    public required LessonTimeConfig TimeConfig;
    public required SemesterIntervalProvider SemesterIntervalProvider;
    public required IEquationCommandsDerivation EquationCommandsDerivation;
    public CommandProcessingConfig ProcessingFlags = CommandProcessingConfig.DryRun;

    public required StudentAttendanceList Attendance;
    public required ILessonTopics LessonTopics;
}

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
                var ids = RegistryScraping.FindGroupMatch(_schedule, _subGroupsMap, group);
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
}

public sealed class OnlineRegistryNavigator
{
    public readonly IRegistryErrorHandler ErrorHandler;
    public readonly RegistryScrapingContext Context;
    public readonly CancellationToken CancellationToken;

    public OnlineRegistryNavigator(
        IRegistryErrorHandler errorHandler,
        RegistryScrapingContext context,
        CancellationToken cancellationToken)
    {
        ErrorHandler = errorHandler;
        Context = context;
        CancellationToken = cancellationToken;
    }

    public CoursesNavigator Courses(
        CourseNameUnifierModule unifier,
        LookupModule lookup)
    {
        return new CoursesNavigator(this, unifier, lookup);
    }

    public GroupsNavigator Groups(
        Schedule schedule,
        GroupParseContext groupParseContext)
    {
        return new GroupsNavigator(this, schedule, groupParseContext);
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
    public ServiceProvider Services => ScrapingContext.Services!;

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

    public static async Task AddLessonsToOnlineRegistry(
        this RegistryScrapingContext context,
        AddLessonsToOnlineRegistryParams p)
    {
        var notFoundStudents = new List<Name>();
        var navigator = new OnlineRegistryNavigator(
            p.ErrorHandler,
            context,
            p.CancellationToken);

        var coursesNav = navigator.Courses(p.CourseNameUnifier, p.LookupModule);
        var groupsNav = navigator.Groups(p.Schedule, p.GroupParseContext);

        var courseLinks = await coursesNav.Get(p.Semester);
        foreach (var courseLink in courseLinks)
        {
            var groups = await groupsNav.Get(courseLink);
            foreach (var group in groups)
            {
                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);
                if (!scanResult.IsValid)
                {
                    continue;
                }
                var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
                    lookup: p.LookupModule.LessonsByCourse,
                    schedule: p.Schedule,
                    courseId: courseLink.CourseId,
                    groups: group.Groups,
                    subGroup: group.SubGroup));

                // Figure out the exact dates the lessons will occur on.
                var lessonsWithTimes = ScheduledLessonsHelper.GetSortedScheduledLessons(new()
                {
                    Lessons = lessons,
                    Schedule = p.Schedule,
                    DateProvider = p.DateProvider,
                    TimeConfig = p.TimeConfig,
                    SemesterIntervalProvider = p.SemesterIntervalProvider,
                    Semester = p.Semester,
                });

                // TODO: Decouple from the implementation, by making a lookup helper at least.
                var remapHelpers = new ValueForEachLessonType<StudentNameRemapHelper>();
                var indexesByLessonType = new ValueForEachLessonType<int>();

                var completeLessons = lessonsWithTimes.Select(x =>
                {
                    var lesson = p.Schedule.Get(x.LessonId);
                    var courseId = lesson.Lesson.Course;
                    var lessonType = lesson.Lesson.Type;
                    ref var attendanceIndex = ref indexesByLessonType[(int) lessonType];
                    // TODO: Should work for any subset of the groups, currently it does not.
                    var key = new AttendanceLookupKey(
                        groups: group.Groups,
                        subGroup: group.SubGroup,
                        courseId: courseId,
                        lessonType: lessonType,
                        dayIndex: attendanceIndex,
                        dateTime: x.DateTime);
                    var attendance = p.Attendance.Get(key);

                    ref var remapHelper = ref remapHelpers[(int) lessonType];
                    if (attendanceIndex == 0)
                    {
                        var studentNames = p.Attendance.StudentNames(new(
                            courseId: courseLink.CourseId,
                            groups: group.Groups.Value,
                            subGroup: group.SubGroup,
                            lessonType: lessonType));
                        remapHelper = StudentNameRemapHelper.Create(
                            namesInHtml: scanResult.Students,
                            namesInDb: studentNames,
                            outNotFoundIndices: notFoundStudents);
                        if (notFoundStudents.Count != 0)
                        {
                            p.ErrorHandler.StudentsNotInDbButInRegistry(new(
                                students: notFoundStudents,
                                schedule: p.Schedule,
                                groups: group.Groups,
                                lessonId: x.LessonId));
                            notFoundStudents.Clear();
                        }
                    }
                    attendanceIndex++;

                    var attendanceForHtml = remapHelper.RemapToHtml(attendance);
                    UpdateAttendanceForRegistry(attendanceForHtml, scanResult.Students);

                    var topic = p.LessonTopics.Get(key);
                    return new LessonInstance
                    {
                        DateTime = x.DateTime,
                        LessonId = x.LessonId,
                        Attendance = attendanceForHtml,
                        Topic = topic,
                    };
                });

                // Update
                var equationCommands = p.EquationCommandsDerivation.DeriveCommands(new(
                    schedule: p.Schedule,
                    remoteLessons: scanResult.Lessons.OrderBy(x => x.DateTime),
                    localLessons: completeLessons));
                foreach (var command in equationCommands)
                {
                    if (p.ProcessingFlags.HasDryRun(command.Type))
                    {
                        Log1();
                        continue;
                    }
                    else if (p.ProcessingFlags.HasLog(command.Type))
                    {
                        Log1();
                    }

                    if (p.ProcessingFlags.HasProcess(command.Type))
                    {
                        await HandleCommand(
                            command,
                            addLessonUri,
                            expectedStudents: scanResult.Students);
                        continue;
                    }

                    void Log1()
                    {
                        Log(command, courseLink.CourseId, group.Groups);
                    }
                }
            }
        }
        return;


        void Log(
            LessonEquationCommand command,
            CourseId courseId,
            in FoundGroups groups)
        {
            var commandName = command.Type switch
            {
                LessonEquationCommandType.Create => "Create",
                LessonEquationCommandType.Update => "Update",
                LessonEquationCommandType.Delete => "Delete",
                _ => throw Unreachable(),
            };
            var date = command.HasAll ? command.Local.DateTime : command.Remote.DateTime;
            var lessonType = command.HasAll
                ? p.Schedule.Get(command.Local.LessonId).Lesson.Type
                : command.Remote.LessonType;
            var dateString = date.ToString("dd.MM.yy");
            var course = p.Schedule.Get(courseId);
            var lessonName = course.FullName;
            var groupName = groups.Value.ToString(p.Schedule);
            Console.WriteLine($"{commandName}: {dateString} - {lessonName} ({groupName} {lessonType})");
        }

        async ValueTask HandleCommand(
            LessonEquationCommand command,
            Uri addLessonUri,
            HtmlStudent[] expectedStudents)
        {
            switch (command.Type)
            {
                case LessonEquationCommandType.Create:
                {
                    await Create(command.Local);
                    break;
                }
                case LessonEquationCommandType.Update:
                {
                    await Update(
                        command.Remote.EditUri,
                        command.Local);
                    break;
                }
                case LessonEquationCommandType.Delete:
                {
                    await HandleExtraLesson(command.Remote);
                    break;
                }
                default:
                {
                    Debug.Fail("Unreachable");
                    break;
                }
            }


            async Task Update(Uri editUri, LessonInstance lessonInstance)
            {
                await CreateOrUpdate1(
                    editUri,
                    lessonInstance,
                    expectedStudents);
            }

            async Task Create(LessonInstance lessonInstance)
            {
                await CreateOrUpdate1(
                    addLessonUri,
                    lessonInstance,
                    expectedStudents);
            }
        }

        async ValueTask HandleExtraLesson(RemoteLessonInstance x)
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

        async Task CreateOrUpdate(
            Uri uri,
            LessonInstance lesson,
            Schedule schedule,
            HttpClient client,
            HtmlStudent[] expectedStudents)
        {
            var doc = await navigator.GetHtml(uri);
            _ = client;
            HtmlSearch.UpdateForm(new()
            {
                Document = doc,
                Lesson = lesson,
                Schedule = schedule,
                ExpectedStudents = expectedStudents,
            });
            await HtmlSearch.SendForm(doc);
        }

        Task CreateOrUpdate1(Uri uri, LessonInstance lesson, HtmlStudent[] expectedStudents)
        {
            // ReSharper disable once AccessToDisposedClosure
            return CreateOrUpdate(uri, lesson, p.Schedule, context.HttpClient, expectedStudents);
        }

        async Task Delete(Uri detailsUri)
        {
            var doc = await navigator.GetHtml(detailsUri);
            var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
            await form.SubmitAsync();
        }

        async Task<(ScanLessonResult ScanResult, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(
            Uri groupUri)
        {
            var doc = await navigator.GetHtml(groupUri);
            var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
            var lessons = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = p.ErrorHandler,
                GetAddLessonDocument = () =>
                {
                    var t = navigator.GetHtml(addLessonLink);
                    return t;
                },
            });
            return (lessons, addLessonLink);
        }
    }

    public static OnlineRegistryNavigator Navigator(
        this RegistryScrapingContext context,
        IRegistryErrorHandler errorHandler,
        CancellationToken cancellationToken)
    {
        return new OnlineRegistryNavigator(
            errorHandler,
            context,
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

    internal static void UpdateAttendanceForRegistry(Attendance[] attendanceForHtml, HtmlStudent[] students)
    {
        for (int index = 0; index < attendanceForHtml.Length; index++)
        {
            ref var a = ref attendanceForHtml[index];
            if (students[index].IsExpelled)
            {
                a = Attendance.None;
                continue;
            }
            a = a switch
            {
                Attendance.Grade => throw new NotImplementedException(),
                Attendance.NotApplicable => Attendance.Present,
                _ => a,
            };
        }
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
        FoundGroups groups,
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
