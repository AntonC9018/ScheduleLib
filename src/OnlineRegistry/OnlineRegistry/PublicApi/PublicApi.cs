using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
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

        using var context = await CreateContext(
            p.Credentials,
            p.Names,
            p.JsonOptions,
            p.CancellationToken);

        var htmlContext = new HtmlContext
        {
            Brower = context.Browser,
            CancellationToken = p.CancellationToken,
            ErrorHandler = p.ErrorHandler,
        };

        var lists = new MatchingLists();

        var courseLinks = await QueryCourseLinks(
            htmlContext,
            p.Names,
            p.Semester,
            new(p.CourseNameUnifier, p.LookupModule));

        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(
                htmlContext,
                groupsUrl,
                p.GroupParseContext,
                p.Schedule);

            foreach (var group in groups)
            {
                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(
                    htmlContext,
                    group.Uri);

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

                var remapHelpers = new ValueForEachLessonType<StudentNameRemapHelper>();
                var indexesByLessonType = new ValueForEachLessonType<int>();

                var completeLessons = lessonsWithTimes.WithIndex().Select(x =>
                {
                    var lesson = p.Schedule.Get(x.Item.LessonId);
                    var courseId = lesson.Lesson.Course;
                    var lessonType = lesson.Lesson.Type;
                    // TODO: Decouple from the implementation.
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
                            p.ErrorHandler.StudentsNotInDbButInRegistry(notFoundStudents);
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
                        DryRun(command, courseLink.CourseId);
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

        void DryRun(LessonEquationCommand command, CourseId courseId)
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
            var course = p.Schedule.Get(courseId);
            var lessonName = course.FullName;
            Console.WriteLine($"{commandName}: {dateString} - {lessonName}");
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
            var doc = await GetHtml(htmlContext, uri);
            _ = client;
            await SendUpdatedForm(new()
            {
                // HttpClient = client,
                // Target = uri,
                Document = doc,
                Lesson = lesson,
                Schedule = schedule,
                ExpectedStudents = expectedStudents,
            });
        }

        Task CreateOrUpdate1(Uri uri, LessonInstance lesson, HtmlStudent[] expectedStudents)
        {
            // ReSharper disable once AccessToDisposedClosure
            return CreateOrUpdate(uri, lesson, p.Schedule, context.HttpClient, expectedStudents);
        }

        async Task Delete(Uri detailsUri)
        {
            var doc = await GetHtml(htmlContext, detailsUri);
            var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
            await form.SubmitAsync();
        }

        static async Task SendUpdatedForm(SendUpdatedFormParams p)
        {
            IHtmlFormElement form;
            {
                var lessonDateBox = (IHtmlInputElement) p.Document.GetElementById("LessonDate")!;
                lessonDateBox.Value = p.Lesson.DateTime.ToString("yyyy-MM-ddTHH:mm");
                Debug.Assert(lessonDateBox.Value is not null and not "");
                form = lessonDateBox.Form!;
            }

            {
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
            }
            if (p.Lesson.Topic is { } topic)
            {
                var topicInput = (IHtmlTextAreaElement) p.Document.GetElementById("LessonTopic")!;
                topicInput.Value = topic;
            }
            if (p.Lesson.Attendance is { } attendance)
            {
                var table = (IHtmlTableElement) p.Document.QuerySelectorAll("table").Last();
                int firstIndex = 1;
                if (attendance.Length != table.Rows.Length - firstIndex)
                {
                    throw new InvalidOperationException("Attendance length does not match the number of students in the HTML");
                }
                var actualStudents = HtmlSearch.FindStudents(table);

                // Find column with name frecvența/nota
                var headerRow = table.Rows[0];
                int attendanceColumnIndex = FindIndexOfAttendance();
                for (int i = 0; i < attendance.Length; i++)
                {
                    var a = attendance[i];
                    if (a == Attendance.None)
                    {
                        continue;
                    }
                    if (a == Attendance.Grade)
                    {
                        throw new NotImplementedException();
                    }

                    var expectedStudent = p.ExpectedStudents[i];
                    var actualStudent = actualStudents[i];
                    if (!actualStudent.Equals(expectedStudent))
                    {
                        throw new InvalidOperationException($"Student mismatch at index {i}: expected {expectedStudent}, got {actualStudent}");
                    }
                    if (actualStudent.IsExpelled)
                    {
                        continue;
                    }

                    var row = table.Rows[i + firstIndex];
                    var cell = row.Cells[attendanceColumnIndex];
                    var input = (IHtmlInputElement) cell.Children[0];
                    input.TextContent = a.ToStringValue();
                }

                int FindIndexOfAttendance()
                {
                    for (int i = 0; i < headerRow.Cells.Length; i++)
                    {
                        var cell = headerRow.Cells[i];
                        if (cell.TextContent == "frecvența/nota")
                        {
                            return i;
                        }
                    }
                    throw new InvalidOperationException("Could not find attendance/grade column");
                }
            }

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

    internal readonly struct HtmlContext
    {
        public required IRegistryErrorHandler ErrorHandler { get; init; }
        public required CancellationToken CancellationToken { get; init; }
        public required IBrowsingContext Brower { get; init; }
    }

    internal static async Task<(ScanLessonResult ScanResult, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(
        HtmlContext context,
        Uri groupUri)
    {
        var doc = await GetHtml(context, groupUri);
        var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
        var lessons = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
        {
            Document = doc,
            ErrorHandler = context.ErrorHandler,
            GetAddLessonDocument = () =>
            {
                var t = GetHtml(context, addLessonLink);
                return t;
            },
        });
        return (lessons, addLessonLink);
    }

    internal static async Task<IEnumerable<GroupLink>> QueryGroupLinksOfCourse(
        HtmlContext context,
        Uri courseUrl,
        GroupParseContext groupParseContext,
        Schedule schedule)
    {
        var doc = await GetHtml(context, courseUrl);
        var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = groupParseContext,
            Schedule = schedule,
            ErrorHandler = context.ErrorHandler,
        });
        return ret;
    }

    internal static async Task<IEnumerable<CourseLink>> QueryCourseLinks(
        HtmlContext context,
        NamesConfig names,
        Semester semester,
        CourseNameUnifierModuleWithDeps courseNames)
    {
        var doc = await GetHtml(context, names.LessonsUrl);
        var ret = HtmlSearch.ScanCoursesDocumentForLinks(new()
        {
            Document = doc,
            Semester = semester,
            ErrorHandler = context.ErrorHandler,
            CourseNames = courseNames,
        });
        return ret;
    }

    [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
    static async Task<IDocument> GetHtml(
        this HtmlContext context,
        Uri uri)
    {
        var document = await context.Brower.OpenAsync(
            address: uri.ToString(),
            context.CancellationToken);
        return document;
    }

    internal static async Task<RegistryScrapingContext> CreateContext(
        Credentials credentials,
        NamesConfig names,
        JsonSerializerOptions? jsonOptions,
        CancellationToken cancellationToken)
    {
        var http = HttpClientContext.Create();
        try
        {
            var tokenContext = new TokenRetrievalContext(new()
            {
                Credentials = credentials,
                Names = names,
                CookieContainer = http.CookieProvider.Container,
                HttpClient = http.Client,
                JsonOptions = jsonOptions,
            });
            await tokenContext.InitializeToken(cancellationToken);

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

file struct SendUpdatedFormParams
{
    public required IDocument Document { get; init; }
    public required LessonInstance Lesson { get; init; }
    // public required HttpClient HttpClient { get; init; }
    // public required Uri Target { get; init; }
    public required Schedule Schedule { get; init; }
    public required HtmlStudent[] ExpectedStudents { get; init; }
}

[InlineArray((int) LessonType.Count)]
file struct ValueForEachLessonType<T>
{
    private T _items;
}
