using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Logging;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

[AutoConstructor]
public sealed partial class AddLessonsToOnlineRegistryTaskHandler
{
    // TODO:
    // Figure out what to do with this abstraction,
    // because it depends on config from the registry config.
    // Using a mapper could be ok to extract parts of the config.
    private readonly IRegistryErrorHandler _errorHandler;
    private readonly ILogger _logger;

    private readonly LookupModule _lookup;
    private readonly ScheduledDateTimeProvider _dateTimeProvider;

    private readonly Schedule _schedule;
    private readonly DataProvider<BuiltRegistryConfig> _configProvider;

    public readonly record struct RunParams()
    {
        public required OnlineRegistryNavigator Navigator { get; init; }
        public required StudentAttendanceList Attendance { get; init; }
        public required ILessonTopics LessonTopics { get; init; }
        public required Semester Semester { get; init; }
        public ILessonFilter LessonFilter { get; init; } = ShouldAlwaysProcessLessonFilter.Instance;
    }

    public async Task Run(RunParams p)
    {
        var notFoundStudents = new List<Name>();

        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("Online registry not configured.");
        }

        var coursesNav = p.Navigator.Courses();
        var groupsNav = p.Navigator.Groups();

        var courseLinks = await coursesNav.Get(p.Semester);
        foreach (var courseLink in courseLinks)
        {
            var groups = await groupsNav.Get(courseLink);
            foreach (var group in groups)
            {
                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);

                var filter = new LessonSearchFilter(
                    courseLink.CourseId,
                    group.Groups,
                    group.SubGroup);
                var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
                        lookup: _lookup.LessonsByCourse,
                        schedule: _schedule,
                        filter: filter))
                    .ToArray();

                var decision = p.LessonFilter.Filter(new(
                    filter: filter,
                    schedule: _schedule,
                    lessons: lessons));

                switch (decision.Decision)
                {
                    case LessonValidityDecision.Process:
                    case LessonValidityDecision.None:
                    {
                        break;
                    }
                    case LessonValidityDecision.Error:
                    {
                        _errorHandler.LessonsDecidedErroneous(
                            new(decision.ErrorContext));
                        continue;
                    }
                    case LessonValidityDecision.Skip:
                    {
                        continue;
                    }
                }

                // Figure out the exact dates the lessons will occur on.
                using var lessonsWithTimes = _dateTimeProvider
                    .GetSorted(new()
                    {
                        Lessons = lessons,
                        Semester = p.Semester,
                    })
                    .ToRentedBuffer();

                var ltHelper = OneForEach.Enum<LessonType>();

                // TODO: Decouple from the implementation, by making a lookup helper at least.
                using var remapHelpers = ltHelper.RentArray<StudentNameRemapHelper>();
                using var indexesByLessonType = ltHelper.RentArray<int>();

                remapHelpers.Clear();
                indexesByLessonType.Clear();

                using var outputBuffer = new RentedBuffer<LessonInstance>(lessonsWithTimes.Len);
                var outputBuilder = outputBuffer.Builder();

                using var firstLessonIds = ltHelper.RentArray<AnyLessonId>();

                EnumBitArray<LessonType> existingTypes = new();
                foreach (ref var x in lessonsWithTimes.Span)
                {
                    var l = _schedule.Get(x.LessonId);
                    var lessonType = l.Lesson.Type;
                    if (!existingTypes.IsSet(lessonType))
                    {
                        firstLessonIds[lessonType] = x.LessonId;
                        existingTypes.Set(lessonType);
                    }
                }

                foreach (var dbLessonType in existingTypes.SetValues())
                {
                    var studentNames = p.Attendance.StudentNames(new(
                        courseId: courseLink.CourseId,
                        groups: group.Groups.Value,
                        subGroup: group.SubGroup,
                        lessonType: dbLessonType));
                    remapHelpers[dbLessonType] = StudentNameRemapHelper.Create(
                        namesInHtml: scanResult.Students,
                        namesInDb: studentNames,
                        outNotFoundIndices: notFoundStudents);
                    if (notFoundStudents.Count != 0)
                    {
                        _errorHandler.StudentsNotInDbButInRegistry(new(
                            students: notFoundStudents,
                            schedule: _schedule,
                            groups: group.Groups,
                            lessonId: firstLessonIds[dbLessonType]));
                        notFoundStudents.Clear();
                    }
                }

                ProcessAll(ref outputBuilder);

                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                void ProcessAll(ref SpanBuilder<LessonInstance> outputBuilder)
                {
                    var sp = lessonsWithTimes.Span;
                    for (int i = 0; i < sp.Length; i++)
                    {
                        var l = _schedule.Get(sp[i].LessonId);
                        var dbLessonType = l.Lesson.Type;
                        ref var attendanceIndex = ref indexesByLessonType[dbLessonType];

                        var registryLessonType = dbLessonType;
                        if (dbLessonType == LessonType.Prelegere)
                        {
                            registryLessonType = LessonType.Curs;
                        }

                        int topicsIndex = attendanceIndex;
                        // if (existingTypes.IsSet(LessonType.Prelegere)
                        //     && !existingTypes.IsSet(LessonType.Curs)
                        //     && dbLessonType is LessonType.Prelegere or LessonType.Curs)
                        // {
                        //     var a = indexesByLessonType[LessonType.Curs];
                        //     var b = indexesByLessonType[LessonType.Prelegere];
                        //     topicsIndex = a + b;
                        // }


                        var lessonInstance = MapLesson(
                            x: sp[i],
                            dbLessonType: dbLessonType,
                            registryLessonType: registryLessonType,
                            attendanceIndex: attendanceIndex,
                            remapHelper: remapHelpers[dbLessonType],
                            topicDayIndex: topicsIndex,
                            topicLessonType: registryLessonType);
                        outputBuilder.Add(lessonInstance);

                        attendanceIndex++;
                    }
                }

                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                LessonInstance MapLesson(
                    in LessonWithDate x,
                    LessonType dbLessonType,
                    LessonType registryLessonType,
                    int attendanceIndex,
                    StudentNameRemapHelper remapHelper,
                    int topicDayIndex,
                    LessonType topicLessonType)
                {
                    var lesson = _schedule.Get(x.LessonId);
                    var courseId = lesson.Lesson.Course;

                    var key = new AttendanceLookupKey(
                        groups: group.Groups,
                        subGroup: group.SubGroup,
                        courseId: courseId,
                        lessonType: dbLessonType,
                        dayIndex: attendanceIndex,
                        dateTime: x.DateTime);
                    var attendance = p.Attendance.Get(key);

                    var attendanceForHtml = remapHelper.RemapToHtml(attendance.AsArray());
                    UpdateAttendanceForRegistry(attendanceForHtml, scanResult.Students);

                    // Note: the index used here is per lesson type as well.
                    var topic = p.LessonTopics.Get(key with
                    {
                        LessonType = topicLessonType,
                        DayIndex = topicDayIndex,
                    });

                    return new LessonInstance
                    {
                        DateTime = x.DateTime,
                        LessonId = x.LessonId,
                        Attendance = attendanceForHtml,
                        Topic = topic,
                        RegistryLessonType = registryLessonType,
                    };
                }

                // Update
                var equationCommands = config.EquationCommandsDerivation.DeriveCommands(new(
                    schedule: _schedule,
                    remoteLessons: scanResult.Lessons.OrderBy(x => x.DateTime),
                    // Need to copy to be able to enumerate
                    localLessons: outputBuilder.Complete().ToArray()));
                foreach (var command in equationCommands)
                {
                    if (config.CommandProcessingConfig.HasDryRun(command.Type))
                    {
                        Log1();
                        continue;
                    }
                    else if (config.CommandProcessingConfig.HasLog(command.Type))
                    {
                        Log1();
                    }

                    if (config.CommandProcessingConfig.HasProcess(command.Type))
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
                ? _schedule.Get(command.Local.LessonId).Lesson.Type
                : command.Remote.LessonType;
            var dateString = date.ToString("dd.MM.yy");
            var course = _schedule.Get(courseId);
            var lessonName = course.FullName;
            var groupName = groups.Value.ToString(_schedule);
            LogCommandBeforeExecution(
                CommandName: commandName,
                DateString: dateString,
                LessonName: lessonName,
                GroupName: groupName,
                LessonType: lessonType);
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
            var action = _errorHandler.ExtraLessonInstanceFound(x.DateTime);
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
            var doc = await p.Navigator.GetHtml(uri);
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
            return CreateOrUpdate(uri, lesson, _schedule, p.Navigator.Context.HttpClient, expectedStudents);
        }

        async Task Delete(Uri detailsUri)
        {
            var doc = await p.Navigator.GetHtml(detailsUri);
            var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
            await form.SubmitAsync();
        }

        async Task<(ScanLessonResult ScanResult, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(
            Uri groupUri)
        {
            var doc = await p.Navigator.GetHtml(groupUri);
            var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
            var lessons = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = _errorHandler,
                GetAddLessonDocument = () =>
                {
                    var t = p.Navigator.GetHtml(addLessonLink);
                    return t;
                },
            });
            return (lessons, addLessonLink);
        }
    }

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

    [LoggerMessage(LogLevel.Information, "{CommandName}: {DateString} - {LessonName} ({GroupName} {LessonType})")]
    partial void LogCommandBeforeExecution(
        string CommandName,
        string DateString,
        string LessonName,
        string GroupName,
        LessonType LessonType);
}
