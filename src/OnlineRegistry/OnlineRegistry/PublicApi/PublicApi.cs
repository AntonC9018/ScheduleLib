using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;


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

public static partial class RegistryScraping
{
    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Names ??= NamesConfig.Default;

        var notFoundStudents = new List<Name>();

        using var context = await CreateContext();
        var lists = new MatchingLists();

        var courseLinks = await QueryCourseLinks();
        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(groupsUrl);
            foreach (var group in groups)
            {
                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);
                var lessons = MissingLessonDetection.MatchLessonsInSchedule(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

                // Figure out the exact dates the lessons will occur on.
                var lessonsWithTimes = MissingLessonDetection.GetDateTimesOfScheduledLessons(new()
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

                var completeLessons = lessonsWithTimes.WithIndex().Select(x =>
                {
                    var lesson = p.Schedule.Get(x.Item.LessonId);
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
                        DateTime = x.Item.DateTime,
                    };
                    var attendance = p.Attendance.Get(key);

                    ref var remapHelper = ref remapHelpers[(int) lessonType];
                    if (attendanceIndex == 0)
                    {
                        remapHelper = StudentNameRemapHelper.Create(
                            namesInHtml: scanResult.Students,
                            namesInDb: p.Attendance.StudentNames(new()
                            {
                                CourseId = courseLink.CourseId,
                                GroupId = group.GroupId,
                                SubGroup = group.SubGroup,
                                LessonType = lessonType,
                            }),
                            outNotFoundIndices: notFoundStudents);
                        if (notFoundStudents.Count != 0)
                        {
                            p.ErrorHandler.StudentsNotInDbButInRegistry(new()
                            {
                                Students = notFoundStudents,
                                Schedule = p.Schedule,
                                GroupId = group.GroupId,
                                LessonId = x.Item.LessonId,
                            });
                            notFoundStudents.Clear();
                        }
                    }
                    attendanceIndex++;

                    var attendanceForHtml = remapHelper.RemapToHtml(attendance);
                    var topic = p.LessonTopics.Get(key);
                    return new LessonInstance
                    {
                        DateTime = x.Item.DateTime,
                        LessonId = x.Item.LessonId,
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
                        DryRun(command, courseLink.CourseId, group.GroupId);
                        continue;
                    }

                    if (p.ProcessingFlags.HasProcess(command.Type))
                    {
                        await HandleCommand(
                            command,
                            addLessonUri,
                            expectedStudents: scanResult.Students);
                        continue;
                    }
                }
            }
        }
        return;

        void DryRun(
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
            var doc = await GetHtml(uri);
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
            var doc = await GetHtml(detailsUri);
            var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
            await form.SubmitAsync();
        }

        async Task<(ScanLessonResult ScanResult, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(
            Uri groupUri)
        {
            var doc = await GetHtml(groupUri);
            var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
            var lessons = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = p.ErrorHandler,
                GetAddLessonDocument = () =>
                {
                    var t = GetHtml(addLessonLink);
                    return t;
                },
            });
            return (lessons, addLessonLink);
        }

        async Task<IEnumerable<GroupLink>> QueryGroupLinksOfCourse(Uri courseUrl)
        {
            var doc = await GetHtml(courseUrl);
            var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
            {
                Document = doc,
                GroupParseContext = p.GroupParseContext,
                SearchGroupId = (in GroupForSearch group) =>
                {
                    var id = FindGroupMatch(p.Schedule, group);
                    if (id == GroupId.Invalid)
                    {
                        // TODO: Do this better
                        p.ErrorHandler.GroupNotFound($"{group.FacultyName}{group.GroupNumber}{group.SubGroupName}");
                    }
                    return id;
                },
            });
            return ret;
        }

        async Task<IEnumerable<CourseLink>> QueryCourseLinks()
        {
            var doc = await GetHtml(p.Names.LessonsUrl);
            var ret = HtmlSearch.ScanCoursesDocumentForLinks(new()
            {
                Document = doc,
                Semester = p.Semester,
                FindCourse = courseName =>
                {
                    if (courseName.Length == 0)
                    {
                        p.ErrorHandler.LessonWithoutName();
                        return null;
                    }
                    var maybeCourseId = p.CourseNameUnifier.Find(new()
                    {
                        CourseName = courseName,
                        Lookup = p.LookupModule,
                        ParseOptions = new()
                        {
                            IgnorePunctuation = true,
                        },
                    });
                    if (maybeCourseId is not { } courseId)
                    {
                        p.ErrorHandler.CourseNotFound(courseName);
                        return null;
                    }
                    return courseId;
                },
            });
            return ret;
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<IDocument> GetHtml(Uri uri)
        {
            var document = await context.Browser.OpenAsync(
                address: uri.ToString(),
                p.CancellationToken);
            return document;
        }

        async Task<RegistryScrapingContext> CreateContext()
        {
            var http = HttpClientContext.Create();
            http.Client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/96.0.4664.45 Safari/537.36");
            http.Client.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
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

    private static GroupId FindGroupMatch(Schedule schedule, in GroupForSearch g)
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

[InlineArray((int) LessonType.Count)]
file struct ValueForEachLessonType<T>
{
    private T _items;
}
