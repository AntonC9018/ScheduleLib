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
        public RegistrySyncDiagnostics? Diagnostics { get; init; }
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
                _logger.LogError("Registry {Kind} form preparation failed for {Date}", action.Kind, action.Date);
            }
        }
    }

    public async Task<IReadOnlyList<RegistrySyncAction>> Plan(RunParams p, CancellationToken cancellationToken = default, bool explicitApply = false)
    {
        var actions = new List<RegistrySyncAction>();
        await PlanGroups(p, group =>
        {
            actions.AddRange(group);
            return Task.FromResult(true);
        }, cancellationToken, explicitApply);
        return actions;
    }

    /// <summary>Reconciles one course/group at a time; false stops before scanning another group.</summary>
    public async Task PlanGroups(RunParams p, Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
        CancellationToken cancellationToken = default, bool explicitApply = false)
    {
        var notFoundStudents = new List<Name>();

        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("Online registry not configured.");
        }

        var courseLinks = (await p.Navigator.GetCourses(p.Semester)).ToArray();
        var coverage = p.Diagnostics is null ? null : new RegistryTargetCoverage(_schedule, ExpectedTargets(), p.Diagnostics);
        coverage?.ReportMissingCourses(courseLinks.Select(link => link.CourseId).ToHashSet());
        var remainingCourseLinks = courseLinks.GroupBy(link => link.CourseId).ToDictionary(group => group.Key, group => group.Count());
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
                var selection = RegistryLabMerges.Select(config.LabMerges, new(
                        lookup: _lookup.LessonsByCourse,
                        schedule: _schedule,
                        filter: filter));
                var lessons = selection.Lessons;
                if (lessons.Count == 0)
                {
                    p.Diagnostics?.Add("registry-partition-mismatch", _schedule.Get(courseLink.CourseId).FullName,
                        group.Groups.Value.ToString(_schedule),
                        $"No schedule lessons match {RegistrySyncDiagnostics.DescribePartition(group.GroupPartition)}. Check the registry assignment and schedule partition mapping.");
                    continue;
                }
                var matchedGroupPartition = selection.AttendancePartition;

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
                        _errorHandler.LessonsDecidedErroneous(new(decision.ErrorContext));
                        p.Diagnostics?.Add("registry-assignment-mismatch", _schedule.Get(courseLink.CourseId).FullName,
                            group.Groups.Value.ToString(_schedule),
                            "The registry destination matches schedule lessons assigned to another teacher. Check its course/group assignment before applying.");
                        continue;
                    }
                    case LessonValidityDecision.Skip:
                    {
                        continue;
                    }
                }

                if (scanResult.Students.Length == 0)
                    p.Diagnostics?.Add("empty-registry-roster", _schedule.Get(courseLink.CourseId).FullName,
                        group.Groups.Value.ToString(_schedule),
                        "The registry destination has no student rows. Ask the registry administrator to check its enrollment; absences cannot be copied to an empty roster.");
                foreach (var lessonId in lessons)
                {
                    var lesson = _schedule.Get(lessonId).Lesson;
                    foreach (var parent in lesson.Groups.Where(parent => group.Groups.Value.Contains(parent)))
                        coverage?.Cover(new(courseLink.CourseId, parent, ProjectPartition(lesson, parent)));
                }

                // Figure out the exact dates the lessons will occur on.
                using var lessonsWithTimes = _dateTimeProvider
                    .GetSorted(new()
                    {
                        Lessons = lessons,
                        Semester = p.Semester,
                    })
                    .ToRentedBuffer();

                var remapHelpers = new Dictionary<(LessonType Type, GroupPartitionKey Partition), StudentNameRemapHelper>();
                var attendanceIndexes = new Dictionary<(LessonType Type, GroupPartitionKey Partition), int>();
                using var outputBuffer = new RentedBuffer<LessonInstance>(lessonsWithTimes.Len);
                var outputBuilder = outputBuffer.Builder();
                foreach (ref var x in lessonsWithTimes.Span)
                {
                    var lesson = _schedule.Get(x.LessonId).Lesson;
                    var key = (Type: lesson.Type, Partition: AttendancePartition(lesson));
                    if (remapHelpers.ContainsKey(key)) continue;
                    var studentNames = p.Attendance.StudentNames(new(
                        courseId: courseLink.CourseId, groups: group.Groups.Value,
                        groupPartition: key.Partition, lessonType: key.Type));
                    if (studentNames.Count == 0)
                        p.Diagnostics?.Add("missing-attendance-sheet", _schedule.Get(courseLink.CourseId).FullName,
                            group.Groups.Value.ToString(_schedule),
                            $"No attendance rows were loaded for {key.Type} ({RegistrySyncDiagnostics.DescribePartition(key.Partition)}). Configure or correct the attendance worksheet for this course/group partition.");
                    remapHelpers[key] = StudentNameRemapHelper.Create(scanResult.Students, studentNames, notFoundStudents);
                    if (notFoundStudents.Count != 0)
                    {
                        _errorHandler.StudentsNotInDbButInRegistry(new(notFoundStudents, _schedule, group.Groups, x.LessonId));
                        if (studentNames.Count != 0) p.Diagnostics?.Add("unmatched-attendance-students", _schedule.Get(courseLink.CourseId).FullName,
                            group.Groups.Value.ToString(_schedule),
                            $"{notFoundStudents.Count} registry students have no matching attendance row for {key.Type} ({RegistrySyncDiagnostics.DescribePartition(key.Partition)}). Check the attendance sheet and name mappings.");
                        notFoundStudents.Clear();
                    }
                }

                GroupPartitionKey AttendancePartition(LessonData lesson) => selection.IsLabMerge && lesson.Type == LessonType.Lab
                    ? GroupPartitionKey.All : lesson.GroupPartitionKey;

                ProcessAll(ref outputBuilder);

                [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
                void ProcessAll(ref SpanBuilder<LessonInstance> outputBuilder)
                {
                    var sp = lessonsWithTimes.Span;
                    for (int i = 0; i < sp.Length; i++)
                    {
                        var l = _schedule.Get(sp[i].LessonId);
                        var dbLessonType = l.Lesson.Type;
                        var attendanceKey = (Type: dbLessonType, Partition: AttendancePartition(l.Lesson));
                        var attendanceIndex = attendanceIndexes.GetValueOrDefault(attendanceKey);

                        var registryLessonType = dbLessonType;
                        if (dbLessonType == LessonType.Prelegere)
                        {
                            registryLessonType = LessonType.Curs;
                        }

                        var lessonInstance = MapLesson(
                            x: sp[i],
                            dbLessonType: dbLessonType,
                            registryLessonType: registryLessonType,
                            attendanceIndex: attendanceIndex,
                            remapHelper: remapHelpers[attendanceKey]);
                        outputBuilder.Add(lessonInstance);

                        attendanceIndexes[attendanceKey] = attendanceIndex + 1;
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
                        groupPartition: AttendancePartition(lesson.Lesson),
                        courseId: courseId,
                        lessonType: dbLessonType,
                        dayIndex: attendanceIndex,
                        dateTime: x.DateTime);
                    var attendance = p.Attendance.Get(key);

                    var attendanceForHtml = remapHelper.RemapToHtml(attendance.AsArray());
                    UpdateAttendanceForRegistry(attendanceForHtml, scanResult.Students);


                    return new LessonInstance
                    {
                        DateTime = x.DateTime,
                        LessonId = x.LessonId,
                        Attendance = attendanceForHtml,
                        Topic = null,
                        RegistryLessonType = registryLessonType,
                    };
                }

                var assessmentRules = RegistryAssessments.Find(config.Assessments, _schedule,
                    courseLink.CourseId, group.Groups.Value, matchedGroupPartition);
                var arranged = RegistryAssessments.Arrange(outputBuilder.Complete().ToArray(),
                    scanResult.Lessons.OrderBy(x => x.DateTime).ToArray(), assessmentRules,
                    (lesson, topicIndex) => p.LessonTopics.Get(new AttendanceLookupKey(
                        groups: group.Groups,
                        groupPartition: AttendancePartition(_schedule.Get(lesson.LessonId).Lesson),
                        courseId: courseLink.CourseId,
                        lessonType: _schedule.Get(lesson.LessonId).Lesson.Type,
                        dayIndex: topicIndex,
                        dateTime: lesson.DateTime)));
                var missingTopics = arranged.Local.Count(lesson => string.IsNullOrWhiteSpace(lesson.Topic));
                if (missingTopics != 0)
                    p.Diagnostics?.Add("missing-lesson-topics", _schedule.Get(courseLink.CourseId).FullName,
                        group.Groups.Value.ToString(_schedule),
                        $"{missingTopics} lessons have no configured topic for {RegistrySyncDiagnostics.DescribePartition(matchedGroupPartition)}. Add or correct the topic source for this course/group partition; text is copied without translation.");
                var equationCommands = config.EquationCommandsDerivation.DeriveCommands(new(
                    schedule: _schedule,
                    remoteLessons: arranged.Remote,
                    localLessons: arranged.Local));
                var actions = new List<RegistrySyncAction>();
                foreach (var command in equationCommands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (config.CommandProcessingConfig.HasLog(command.Type)
                        || config.CommandProcessingConfig.HasDryRun(command.Type))
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
                if (!await onGroupPlanned(actions)) return;
            }
            if (--remainingCourseLinks[courseLink.CourseId] == 0) coverage?.ReportMissingGroups(courseLink.CourseId);
        }

        IEnumerable<RegistryExpectedTarget> ExpectedTargets()
        {
            foreach (var lesson in _schedule.EnumerateAllLessons())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ids = new List<AnyLessonId> { lesson.Id };
                var filter = new LessonSearchFilter(lesson.Lesson.Course,
                    new FoundGroups { Value = lesson.Lesson.Groups, IsWildcard = lesson.Lesson.Groups.Count > 1 },
                    lesson.Lesson.GroupPartitionKey);
                var validity = p.LessonFilter.Filter(new(filter, _schedule, ids));
                if (validity.Decision is LessonValidityDecision.Skip or LessonValidityDecision.Error || ids.Count == 0) continue;
                bool hasDates;
                try { hasDates = _dateTimeProvider.GetSorted(new() { Lessons = ids, Semester = p.Semester }).Any(); }
                catch (MissingSemesterDateRangeException)
                {
                    p.Diagnostics?.Add("missing-schedule-dates", _schedule.Get(lesson.Lesson.Course).FullName,
                        lesson.Lesson.Groups.ToString(_schedule),
                        "Configure the semester date range for this group's grade, attendance mode, and qualification before synchronizing it.");
                    continue;
                }
                if (!hasDates) continue;
                foreach (var parent in lesson.Lesson.Groups)
                    yield return new(lesson.Lesson.Course, parent, ProjectPartition(lesson.Lesson, parent));
            }
        }

        GroupPartitionKey ProjectPartition(LessonData lesson, GroupId parent)
        {
            if (lesson.Type != LessonType.Lab || !lesson.Groups.IsSingleGroup) return lesson.GroupPartitionKey;
            var merge = RegistryLabMerges.Find(config.LabMerges, _schedule, lesson.Course, new LessonGroups { parent }, GroupPartitionKey.All);
            return merge is not null ? GroupPartitionKey.All : lesson.GroupPartitionKey;
        }

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
