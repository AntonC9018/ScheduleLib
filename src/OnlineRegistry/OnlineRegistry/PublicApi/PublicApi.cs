using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
    public CommandProcessingConfig ProcessingFlags = CommandProcessingConfig.DryRun;

    public required StudentAttendanceList Attendance;
    public required LessonTopics LessonTopics;
}

public readonly struct LessonTopics
{
    private readonly Dictionary<AttendanceLookupKey, string> _topics;

    public LessonTopics(Dictionary<AttendanceLookupKey, string> topics)
    {
        _topics = topics;
    }

    public string? Get(AttendanceLookupKey key)
    {
        return _topics.GetValueOrDefault(key);
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

    public GroupsNavigator(
        OnlineRegistryNavigator navigator,
        Schedule schedule,
        GroupParseContext groupParseContext)
    {
        _navigator = navigator;
        _schedule = schedule;
        _groupParseContext = groupParseContext;
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
                var id = RegistryScraping.FindGroupMatch(_schedule, group);
                if (id == GroupId.Invalid)
                {
                    // TODO: Do this better
                    _navigator.ErrorHandler.GroupNotFound($"{group.FacultyName}{group.GroupNumber}{group.SubGroupName}");
                }
                return id;
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

        var lists = new MatchingLists();
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
                var lessons = MissingLessonDetection.MatchLessonsInSchedule(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

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
                    var key = new AttendanceLookupKey
                    {
                        GroupId = group.GroupId,
                        SubGroup = group.SubGroup,
                        CourseId = courseId,
                        LessonType = lessonType,
                        DayIndex = attendanceIndex,
                        DateTime = x.DateTime,
                    };
                    var attendance = p.Attendance.Get(key);

                    ref var remapHelper = ref remapHelpers[(int) lessonType];
                    if (attendanceIndex == 0)
                    {
                        var studentNames = p.Attendance.StudentNames(new()
                        {
                            CourseId = courseLink.CourseId,
                            GroupId = group.GroupId,
                            SubGroup = group.SubGroup,
                            LessonType = lessonType,
                        });
                        remapHelper = StudentNameRemapHelper.Create(
                            namesInHtml: scanResult.Students,
                            namesInDb: studentNames,
                            outNotFoundIndices: notFoundStudents);
                        if (notFoundStudents.Count != 0)
                        {
                            p.ErrorHandler.StudentsNotInDbButInRegistry(new()
                            {
                                Students = notFoundStudents,
                                Schedule = p.Schedule,
                                GroupId = group.GroupId,
                                LessonId = x.LessonId,
                            });
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
                var equationCommands = MissingLessonDetection.GetLessonEquationCommands(new()
                {
                    Lists = lists,
                    Schedule = p.Schedule,
                    AllLessons = completeLessons,
                    ExistingLessons = scanResult.Lessons,
                });
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
                        Log(command, courseLink.CourseId, group.GroupId);
                    }
                }
            }
        }
        return;


        void Log(
            LessonEquationCommand command,
            CourseId courseId,
            GroupId groupId)
        {
            var commandName = command.Type switch
            {
                LessonEquationCommandType.Create => "Create",
                LessonEquationCommandType.Update => "Update",
                LessonEquationCommandType.Delete => "Delete",
                _ => throw Unreachable(),
            };
            var date = command.HasAll ? command.All.DateTime : command.Existing.DateTime;
            var lessonType = command.HasAll
                ? p.Schedule.Get(command.All.LessonId).Lesson.Type
                : command.Existing.LessonType;
            var groupName = p.Schedule.Get(groupId).Name;
            var dateString = date.ToString("dd.MM.yy");
            var course = p.Schedule.Get(courseId);
            var lessonName = course.FullName;
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
                    await Create(command.All);
                    break;
                }
                case LessonEquationCommandType.Update:
                {
                    await Update(
                        command.Existing.EditUri,
                        command.All);
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

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    internal static async Task<IDocument> GetHtml(
        this RegistryScrapingContext context,
        Uri uri,
        CancellationToken cancellationToken)
    {
        var document = await context.Browser.OpenAsync(
            address: uri.ToString(),
            cancellationToken);
        return document;
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

    internal static GroupId FindGroupMatch(Schedule schedule, in GroupForSearch g)
    {
        var groups = schedule.Groups;
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (IsMatch(group, g))
            {
                return new(i);
            }
        }
        return GroupId.Invalid;
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

        if (a.GroupNumber != b.GroupNumber)
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

        return true;
    }
}


public readonly record struct StudentsLookupKey
{
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required CourseId CourseId { get; init; }
    public required LessonType LessonType { get; init; }
}

public readonly record struct AttendanceLookupKey
{
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required CourseId CourseId { get; init; }
    public required LessonType LessonType { get; init; }

    // The program may use any of this info to get the right data.
    public required int DayIndex { get; init; }
    public required DateTime DateTime { get; init; }
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

[InlineArray((int) LessonType.Count)]
file struct ValueForEachLessonType<T>
{
    private T _items;
}
