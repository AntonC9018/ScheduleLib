using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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
        public required IRegistrySyncNavigator Navigator { get; init; }
        public required StudentAttendanceList Attendance { get; init; }
        public required ILessonTopics LessonTopics { get; init; }
        public required Semester Semester { get; init; }
        public ILessonFilter LessonFilter { get; init; } = ShouldAlwaysProcessLessonFilter.Instance;
    }

    public async Task Run(RunParams p)
    {
        var plan = await Plan(p);
        foreach (var action in plan)
        {
            try { await action.Execute(CancellationToken.None); }
            catch (RegistryActionExecutionException error) when (!error.SubmissionStarted
                && error.InnerException is RegistryFormPreparationException)
            {
                _logger.LogError(error.InnerException, "Registry {Kind} form preparation failed for {Date}", action.Kind, action.Date);
            }
        }
    }

    public async Task<IReadOnlyList<RegistrySyncAction>> Plan(RunParams p, CancellationToken cancellationToken = default, bool explicitApply = false)
    {
        var actions = new List<RegistrySyncAction>();
        var notFoundStudents = new List<Name>();

        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("Online registry not configured.");
        }

        var courseLinks = await p.Navigator.GetCourses(p.Semester);
        foreach (var courseLink in courseLinks)
        {
            var groups = await p.Navigator.GetGroups(courseLink);
            foreach (var group in groups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);

                var filter = new LessonSearchFilter(
                    courseLink.CourseId,
                    group.Groups,
                    group.GroupPartition);
                var lessons = MatchLessonHelper.MatchLessonsInSchedule(new(
                        lookup: _lookup.LessonsByCourse,
                        schedule: _schedule,
                        filter: filter))
                    .ToList();
                if (lessons.Count == 0)
                {
                    continue;
                }
                var matchedGroupPartition =
                    _schedule.Get(lessons[0]).Lesson.GroupPartitionKey;

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
                        groupPartition: matchedGroupPartition,
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

                        // int topicsIndex = attendanceIndex;
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
                            remapHelper: remapHelpers[dbLessonType]);
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
                    StudentNameRemapHelper remapHelper)
                {
                    var lesson = _schedule.Get(x.LessonId);
                    var courseId = lesson.Lesson.Course;

                    var key = new AttendanceLookupKey(
                        groups: group.Groups,
                        groupPartition: matchedGroupPartition,
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
                        LessonType = dbLessonType,
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
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!explicitApply && (config.CommandProcessingConfig.HasLog(command.Type)
                        || config.CommandProcessingConfig.HasDryRun(command.Type)))
                    {
                        LogCommandBeforeExecution(command.Type.ToString(),
                            (command.HasAll ? command.Local.DateTime : command.Remote.DateTime).ToString("dd.MM.yy"),
                            _schedule.Get(courseLink.CourseId).FullName, group.Groups.Value.ToString(_schedule),
                            command.HasAll ? _schedule.Get(command.Local.LessonId).Lesson.Type : command.Remote.LessonType);
                    }
                    var date = command.HasAll ? command.Local.DateTime : command.Remote.DateTime;
                    var enabled = explicitApply || config.CommandProcessingConfig.HasProcess(command.Type)
                        && !config.CommandProcessingConfig.HasDryRun(command.Type);
                    string? omission = enabled ? null : "Disabled by configured processing flags.";
                    if (command.Type == LessonEquationCommandType.Delete)
                    {
                        var policy = _errorHandler.ExtraLessonInstanceFound(date);
                        if (policy != ExtraLessonInstanceAction.Delete)
                        {
                            enabled = false;
                            omission = policy == ExtraLessonInstanceAction.LeaveAlone
                                ? "Extra lesson left alone by configured policy."
                                : "DeleteWithoutDataLoss is unsupported; extra lesson omitted.";
                        }
                    }
                    var captured = command;
                    actions.Add(new(command.Type.ToString().ToLowerInvariant(), group.Uri.ToString(),
                        _schedule.Get(courseLink.CourseId).FullName, group.Groups.Value.ToString(_schedule), date,
                        command.HasAll ? command.Local.Topic : command.Remote.Topic,
                        command.HasAll ? command.Local.Attendance?.Length : command.Remote.Attendance.Length,
                        enabled, omission, async token =>
                        {
                            token.ThrowIfCancellationRequested();
                            var submissionStarted = false;
                            try { await HandleCommand(captured, addLessonUri, scanResult.Students, () => submissionStarted = true); }
                            catch (OperationCanceledException error) { throw new RegistryActionCancelledException(submissionStarted, error); }
                            catch (Exception error) { throw new RegistryActionExecutionException(submissionStarted, error); }
                        }));
                }
            }
        }
        return actions;

        async ValueTask HandleCommand(
            LessonEquationCommand command,
            Uri addLessonUri,
            HtmlStudent[] expectedStudents,
            Action markSubmission)
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
                    await Delete(command.Remote.ViewUri, markSubmission);
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
                    expectedStudents, markSubmission);
            }

            async Task Create(LessonInstance lessonInstance)
            {
                await CreateOrUpdate1(
                    addLessonUri,
                    lessonInstance,
                    expectedStudents, markSubmission);
            }
        }

        async Task CreateOrUpdate(
            Uri uri,
            LessonInstance lesson,
            Schedule schedule,
            HtmlStudent[] expectedStudents,
            Action markSubmission)
        {
            var doc = await p.Navigator.GetHtml(uri);
            try
            {
                HtmlSearch.UpdateForm(new()
                {
                    Document = doc,
                    Lesson = lesson,
                    Schedule = schedule,
                    ExpectedStudents = expectedStudents,
                });
            }
            catch (InvalidOperationException error)
            {
                throw new RegistryFormPreparationException(error);
            }
            await p.Navigator.SubmitLesson(doc, markSubmission);
        }

        Task CreateOrUpdate1(Uri uri, LessonInstance lesson, HtmlStudent[] expectedStudents, Action markSubmission)
        {
            // ReSharper disable once AccessToDisposedClosure
            return CreateOrUpdate(uri, lesson, _schedule, expectedStudents, markSubmission);
        }

        async Task Delete(Uri detailsUri, Action markSubmission)
        {
            var doc = await p.Navigator.GetHtml(detailsUri);
            await p.Navigator.SubmitDelete(doc, markSubmission);
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
