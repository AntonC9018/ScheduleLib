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
    private static Attendance[]? MergeLectureAttendance(LessonInstance[] lessons)
    {
        var marks = lessons.First().Attendance;
        if (marks is null)
        {
            return null;
        }

        var combined = (Attendance[]) marks.Clone();
        foreach (var lesson in lessons.Skip(1))
        {
            if (lesson.Attendance is not { } other)
            {
                continue;
            }

            if (other.Length != combined.Length)
            {
                throw new InvalidOperationException("Shared lecture attendance roster lengths differ.");
            }

            for (int index = 0; index < combined.Length; index++)
            {
                if (combined[index] == Attendance.None)
                {
                    combined[index] = other[index];
                }
                else if (other[index] != Attendance.None && other[index] != combined[index])
                {
                    throw new InvalidOperationException(
                        "Shared lecture attendance sources disagree for the same occurrence.");
                }
            }
        }
        return combined;
    }

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
        public bool HasAttendanceSources { get; init; } = true;
        public required ILessonTopics LessonTopics { get; init; }
        public required Semester Semester { get; init; }
        public RegistrySyncDiagnostics? Diagnostics { get; init; }
        public ILessonFilter LessonFilter { get; init; } = ShouldAlwaysProcessLessonFilter.Instance;
    }

    public async Task Run(RunParams p)
    {
        static bool IsRefusedDeletion(string actionKind, RegistryActionExecutionException error)
        {
            if (actionKind != "delete")
            {
                return false;
            }

            if (error.InnerException is RegistryDeletionRefusedException)
            {
                return true;
            }

            return false;
        }

        static bool IsFormPreparationFailureBeforeSubmission(RegistryActionExecutionException error)
        {
            if (error.SubmissionStarted)
            {
                return false;
            }

            if (error.InnerException is RegistryFormPreparationException)
            {
                return true;
            }

            return false;
        }

        var plan = await Plan(p);
        foreach (var action in plan)
        {
            try
            {
                await action.Execute(CancellationToken.None);
            }
            catch (RegistryActionExecutionException error) when (IsRefusedDeletion(action.Kind, error))
            {
                _logger.LogWarning(
                    "Registry refused deletion of {Course} ({Groups}) on {Date}; skipped without retrying.",
                    action.Course,
                    action.Groups,
                    action.Date);
                p.Diagnostics?.Add(kind: "registry-deletion-refused",
                    course: action.Course,
                    @group: action.Groups,
                    detail:
                    $"Skipped deletion on {action.Date:yyyy-MM-dd HH:mm}: the lesson remains unchanged. No retry was performed.");
            }
            catch (RegistryActionExecutionException error) when (IsFormPreparationFailureBeforeSubmission(error))
            {
                _logger.LogError("Registry {Kind} form preparation failed for {Date}",
                    action.Kind,
                    action.Date);
            }
        }
    }

    public async Task<IReadOnlyList<RegistrySyncAction>> Plan(
        RunParams p,
        CancellationToken cancellationToken = default,
        bool explicitApply = false)
    {
        var actions = new List<RegistrySyncAction>();
        await PlanGroups(p,
            group =>
            {
                actions.AddRange(group);
                return Task.FromResult(true);
            },
            cancellationToken,
            explicitApply);
        return actions;
    }

    /// <summary>Reconciles one course/group at a time; false stops before scanning another group.</summary>
    public async Task PlanGroups(
        RunParams p,
        Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
        CancellationToken cancellationToken = default,
        bool explicitApply = false)
    {
        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("Online registry not configured.");
        }

        var courseLinks = (await p.Navigator.GetCourses(p.Semester))
            .ToArray();
        var coverage = p.Diagnostics is null
            ? null
            : new RegistryTargetCoverage(_schedule,
                ExpectedTargets(),
                p.Diagnostics);
        coverage?.ReportMissingCourses(courseLinks.Select(link => link.CourseId)
            .ToHashSet());
        foreach (var courseAssignments in courseLinks.GroupBy(link => link.CourseId))
        {
            // Discover every assignment for this canonical course before routing or submitting.
            var courseLink = courseAssignments.First();
            var destinations = new List<RegistryLessonDestination>();
            var visited = new HashSet<Uri>();
            foreach (var assignment in courseAssignments)
            {
                foreach (var group in await p.Navigator.GetGroups(assignment))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(group.Uri))
                    {
                        continue;
                    }

                    var filter = new LessonSearchFilter(courseLink.CourseId,
                        group.Groups,
                        group.GroupPartition);
                    var config2 = config;
                    LessonMatchParams parameters = new(filter,
                        _lookup.LessonsByCourse,
                        _schedule);
                    var candidates = RegistryLessonRouting.Candidates(config2, parameters);
                    var validity = p.LessonFilter.Filter(new(filter,
                        _schedule,
                        candidates.Lessons));
                    if (validity.Decision == LessonValidityDecision.Error)
                    {
                        var iRegistryErrorHandler = _errorHandler;
                        ErroneousLesson lessons2 = new(validity.ErrorContext);
                        iRegistryErrorHandler.LessonsDecidedErroneous(lessons2);
                        p.Diagnostics?.Add(kind: "registry-assignment-mismatch",
                            course: _schedule.Get(courseLink.CourseId).FullName,
                            @group: group.Groups.Value.ToString(_schedule),
                            detail:
                            "The registry destination has no matching lessons assigned to this teacher. Check its assignment.");
                        continue;
                    }
                    if (validity.Decision == LessonValidityDecision.Skip)
                    {
                        continue;
                    }

                    destinations.Add(new(Group: group, Selection: candidates));
                }
            }

            var routed = RegistryLessonRouting.Prioritize(_schedule,
                destinations,
                p.Diagnostics,
                out var hasUnresolvedAudience);
            var studentCoverage = new RegistryStudentCoverage(p.Attendance, config.StudentNames);
            var studentCoverageComplete = !hasUnresolvedAudience;
            foreach (var destination in destinations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var group = destination.Group;
                var selection = destination.Selection;
                var filter = new LessonSearchFilter(courseLink.CourseId,
                    group.Groups,
                    group.GroupPartition);
                var audiences = routed[group.Uri];
                var lessons = selection.Lessons.Where(audiences.ContainsKey)
                    .ToList();
                if (lessons.Count == 0)
                {
                    if (selection.Lessons.Count == 0)
                    {
                        string CalculateDetail()
                        {
                            string describePartition = RegistrySyncDiagnostics.DescribePartition(group.GroupPartition);
                            string detail2 =
                                $"No schedule lessons match {describePartition}. Check the registry assignment and schedule partition mapping.";
                            return detail2;
                        }

                        p.Diagnostics?.Add(kind: "registry-partition-mismatch",
                            course: _schedule.Get(courseLink.CourseId).FullName,
                            @group: group.Groups.Value.ToString(_schedule),
                            detail: CalculateDetail());
                    }

                    continue;
                }
                var matchedGroupPartition = selection.AttendancePartition;

                var (scanResult, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);
                if (scanResult.Students.Length == 0)
                {
                    p.Diagnostics?.Add(kind: "empty-registry-roster",
                        course: _schedule.Get(courseLink.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail:
                        "The registry destination has no student rows. Ask the registry administrator to check its enrollment; absences cannot be copied to an empty roster.");
                }

                foreach (var lessonId in lessons)
                {
                    var lesson = _schedule.Get(lessonId).Lesson;
                    foreach (var parent in audiences[lessonId])
                    {
                        GroupPartitionKey CalculatePartition()
                        {
                            var partition = ProjectPartition(lesson, parent);
                            return partition;
                        }

                        coverage?.CoverLesson(lessonId,
                            parent,
                            CalculatePartition(),
                            config.GroupMatchRules);
                    }
                }

                // Figure out the exact dates the lessons will occur on.
                LessonWithDate[] datedLessons;
                try
                {
                    datedLessons = _dateTimeProvider.GetSorted(new()
                    {
                        Lessons = lessons,
                        Semester = p.Semester,
                    })
                        .ToArray();
                }
                catch (MissingSemesterDateRangeException)
                {
                    studentCoverageComplete = false;
                    p.Diagnostics?.Add(kind: "missing-schedule-dates",
                        course: _schedule.Get(courseLink.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail:
                        "Configure the semester date range for this group's grade, attendance mode, and qualification before synchronizing it.");
                    continue;
                }
                using var lessonsWithTimes = datedLessons.ToRentedBuffer();

                var remapHelpers =
                    new Dictionary<(LessonType Type, GroupPartitionKey Partition, LessonGroups Audience),
                        StudentNameRemapHelper>();
                var attendanceIndexes =
                    new Dictionary<(LessonType Type, GroupPartitionKey Partition, LessonGroups Audience), int>();
                using var outputBuffer = new RentedBuffer<LessonInstance>(lessonsWithTimes.Len);
                var outputBuilder = outputBuffer.Builder();
                foreach (ref var x in lessonsWithTimes.Span)
                {
                    var lesson = _schedule.Get(x.LessonId).Lesson;
                    var key = (lesson.Type, Partition: AttendancePartition(lesson),
                        Audience: audiences[x.LessonId].Ordered());
                    if (remapHelpers.ContainsKey(key))
                    {
                        continue;
                    }

                    StudentsLookupKey CalculateKey()
                    {
                        StudentsLookupKey key2 = new(
                            courseId: courseLink.CourseId,
                            groups: key.Audience,
                            groupPartition: key.Partition,
                            lessonType: key.Type);
                        return key2;
                    }

                    var studentNames = p.Attendance.StudentNames(CalculateKey());

                    bool CalculateConditionResult3()
                    {
                        if (!p.HasAttendanceSources)
                        {
                            return false;
                        }

                        if (studentNames.Count == 0)
                        {
                            return true;
                        }

                        return false;
                    }

                    bool conditionResult = CalculateConditionResult3();


                    if (conditionResult)
                    {
                        p.Diagnostics?.Add(kind: "missing-attendance-sheet",
                            course: _schedule.Get(courseLink.CourseId).FullName,
                            @group: group.Groups.Value.ToString(_schedule),
                            detail:
                            $"No attendance rows were loaded for {key.Type} ({RegistrySyncDiagnostics.DescribePartition(key.Partition)}). Configure or correct the attendance worksheet for this course/group partition.");
                    }

                    remapHelpers[key] = StudentNameRemapHelper.Create(scanResult.Students,
                        studentNames,
                        [],
                        config.StudentNames);
                }

                if (p.HasAttendanceSources)
                {
                    if (remapHelpers.Count != 0)
                    {
                        var membershipKeys = selection.Lessons.Select(id =>
                        {
                            StudentsLookupKey ProjectValue()
                            {
                                var lesson = _schedule.Get(id).Lesson;
                                var audience = new LessonGroups();
                                foreach (var parent in lesson.Groups.Where(group.Groups.Value.Contains))
                                {
                                    audience.Add(parent);
                                }

                                GroupPartitionKey CalculateGroupPartition()
                                {
                                    var groupPartition = AttendancePartition(lesson);
                                    return groupPartition;
                                }

                                return new StudentsLookupKey(audience.Ordered(),
                                    CalculateGroupPartition(),
                                    courseLink.CourseId,
                                    lesson.Type);
                            }

                            return ProjectValue();
                        });
                        studentCoverage.Observe(scanResult.Students,
                            routedKeys: remapHelpers.Keys.Select(key =>
                                new StudentsLookupKey(key.Audience,
                                    key.Partition,
                                    courseLink.CourseId,
                                    key.Type)),
                            membershipKeys: membershipKeys);
                    }
                }

                GroupPartitionKey AttendancePartition(LessonData lesson)
                {
                    return RegistryLessonRouting.ProjectPartition(config,
                        _schedule,
                        lesson);
                }

                ProcessAll(ref outputBuilder);

                [SuppressMessage(category: "ReSharper", checkId: "AccessToDisposedClosure")]
                void ProcessAll(ref SpanBuilder<LessonInstance> outputBuilder)
                {
                    var sp = lessonsWithTimes.Span;
                    for (int i = 0; i < sp.Length; i++)
                    {
                        var l = _schedule.Get(sp[i].LessonId);
                        var dbLessonType = l.Lesson.Type;
                        var attendanceKey = (Type: dbLessonType, Partition: AttendancePartition(l.Lesson),
                            Audience: audiences[sp[i].LessonId].Ordered());
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

                [SuppressMessage(category: "ReSharper", checkId: "AccessToDisposedClosure")]
                LessonInstance MapLesson(
                    in LessonWithDate x,
                    LessonType dbLessonType,
                    LessonType registryLessonType,
                    int attendanceIndex,
                    StudentNameRemapHelper remapHelper)
                {
                    var lesson = _schedule.Get(x.LessonId);
                    var courseId = lesson.Lesson.Course;

                    GroupPartitionKey CalculateGroupPartition()
                    {
                        var groupPartition2 = AttendancePartition(lesson.Lesson);
                        return groupPartition2;
                    }

                    var key = new AttendanceLookupKey(
                        groups: new FoundGroups
                        {
                            Value = audiences[x.LessonId].Ordered(),
                            IsWildcard = false,
                        },
                        groupPartition: CalculateGroupPartition(),
                        courseId: courseId,
                        lessonType: dbLessonType,
                        dayIndex: attendanceIndex,
                        dateTime: x.DateTime);
                    var attendance = p.Attendance.Get(key with
                    {
                        ActiveGroups = key.Groups.Value,
                    });

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

                LessonGroups TopicGroups(LessonInstance lesson) => audiences[lesson.LessonId].Ordered();
                var knownScopes = destinations.SelectMany(candidate => candidate.Selection.Lessons.Select(id =>
                        RegistryTopicScope.Format(_schedule,
                            _schedule.Get(id).Lesson.Groups,
                            RegistryLessonRouting.ProjectPartition(config,
                                _schedule,
                                _schedule.Get(id).Lesson))))
                    .Concat(destinations.SelectMany(candidate => routed[candidate.Group.Uri].Select(entry =>
                        RegistryTopicScope.Format(_schedule,
                            entry.Value,
                            RegistryLessonRouting.ProjectPartition(config,
                                _schedule,
                                _schedule.Get(entry.Key).Lesson)))))
                    .Concat(destinations.SelectMany(candidate => candidate.Selection.Lessons.Select(id =>
                        RegistryTopicScope.Format(_schedule,
                            _schedule.Get(id).Lesson.Groups,
                            _schedule.Get(id).Lesson.GroupPartitionKey))))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                bool CheckCondition()
                {
                    if (group.Groups.IsWildcard)
                    {
                        return true;
                    }

                    if (group.Groups.IsCombined)
                    {
                        return true;
                    }

                    if (!group.Groups.Value.IsSingleGroup)
                    {
                        return true;
                    }

                    if (lessons.Any(id => _schedule.Get(id).Lesson.Type == LessonType.Lab
                            && AttendancePartition(_schedule.Get(id).Lesson) != group.GroupPartition))
                    {
                        return true;
                    }

                    return false;
                }

                var labelScopes = CheckCondition();
                var localInstances = outputBuilder.Complete()
                    .ToArray();

                string Scope(LessonInstance local)
                {
                    LessonGroups CalculateGroups()
                    {
                        var groups = TopicGroups(local);
                        return groups;
                    }

                    if (local.RegistryLessonType == LessonType.Curs)
                    {
                        return RegistryLectureAudience.Format(_schedule, local.LessonId);
                    }

                    return RegistryTopicScope.Format(_schedule,
                        CalculateGroups(),
                        AttendancePartition(_schedule.Get(local.LessonId).Lesson));
                }

                knownScopes = knownScopes.Concat(localInstances.Select(Scope))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                // A lecture represented once per attending parent is still one physical session in a shared list.
                localInstances = localInstances.GroupBy(local =>
                        (Date: local.DateTime, Type: local.RegistryLessonType, Scope: Scope(local)))
                    .SelectMany(session => session.Key.Type == LessonType.Curs
                        ? new[]
                        {
                            session.First() with
                            {
                                Attendance = MergeLectureAttendance(session.ToArray()),
                            },
                        }
                        : session.ToArray())
                    .OrderBy(local => local.DateTime)
                    .ToArray();

                string? ResolveScope(RemoteLessonInstance remote, string oldScope)
                {
                    var matchingType = localInstances.Where(local => remote.LessonType == LessonType.Unspecified
                            || local.RegistryLessonType == remote.LessonType)
                        .ToArray();
                    var direct = matchingType.Where(local =>
                    {
                        string a = Scope(local);
                        string b = oldScope;
                        var comparisonType = StringComparison.OrdinalIgnoreCase;
                        bool equals = string.Equals(a,
                            b,
                            comparisonType);
                        return equals;
                    });
                    if (direct.Any())
                    {
                        return Scope(direct.First());
                    }

                    var migrated = matchingType.Where(local => string.Equals(RegistryTopicScope.Format(_schedule,
                                    _schedule.Get(local.LessonId).Lesson.Groups,
                                    AttendancePartition(_schedule.Get(local.LessonId).Lesson)),
                                oldScope,
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(RegistryTopicScope.Format(_schedule,
                                    _schedule.Get(local.LessonId).Lesson.Groups,
                                    _schedule.Get(local.LessonId).Lesson.GroupPartitionKey),
                                oldScope,
                                StringComparison.OrdinalIgnoreCase))
                        .ToArray();
                    var atTime = migrated.Where(local => local.DateTime == remote.DateTime)
                        .ToArray();
                    LessonInstance[] value;
                    if (atTime.Length == 0)
                    {
                        value = migrated;
                    }
                    else
                    {
                        value = atTime;
                    }

                    var value2 = value;
                    Func<LessonInstance, string> selector = Scope;
                    var select = value2.Select(selector);
                    var distinct = select.Distinct();
                    var scopes = distinct.ToArray();
                    if (scopes.Length != 1)
                    {
                        return null;
                    }

                    return scopes[0];
                }

                var movedLabs = selection.Lessons.Except(lessons)
                    .Where(id => _schedule.Get(id).Lesson.Type == LessonType.Lab)
                    .ToList();
                var movedTimes = movedLabs.Count == 0
                    ? new HashSet<DateTime>()
                    : _dateTimeProvider.GetSorted(new()
                    {
                        Lessons = movedLabs,
                        Semester = p.Semester,
                    })
                        .Select(lesson => lesson.DateTime)
                        .ToHashSet();
                // Protect only labs demonstrably routed elsewhere, including their existing attendance.
                var remoteInstances = scanResult.Lessons.Where(remote =>
                    {
                        if (remote.LessonType != LessonType.Lab)
                        {
                            return true;
                        }

                        bool conditionValue;
                        if (RegistryTopicScope.TrySplit(topic: remote.Topic,
                                knownScopes,
                                baseTopic: out _,
                                scope: out var scope))
                        {
                            conditionValue = ResolveScope(remote, scope!) is not null;
                        }
                        else
                        {
                            bool CalculateConditionResult()
                            {
                                if (!movedTimes.Contains(remote.DateTime))
                                {
                                    return true;
                                }

                                if (localInstances.Any(local =>
                                        local.RegistryLessonType == LessonType.Lab
                                        && local.DateTime == remote.DateTime))
                                {
                                    return true;
                                }

                                return false;
                            }

                            bool conditionResult = CalculateConditionResult();


                            conditionValue = conditionResult;
                        }

                        return conditionValue;
                    })
                    .OrderBy(remote => remote.DateTime)
                    .ToArray();
                var unresolvedAssessments = new List<RemoteLessonInstance>();
                var inferredTypes = new Dictionary<Uri, LessonType>();
                var owners = RegistryScopeReconciliation.Assign(_schedule,
                    localInstances,
                    remoteInstances,
                    knownScopes,
                    Scope,
                    config.EquationCommandsDerivation,
                    unresolvedAssessments.Add,
                    ResolveScope,
                    inferredTypes);
                foreach (var assessment in unresolvedAssessments)
                {
                    string CalculateDetail2()
                    {
                        string detail3 =
                            $"Assessment on {assessment.DateTime:yyyy-MM-dd HH:mm} has no scope identifying its scheduled audience. Its entry and candidate sessions were preserved.";
                        return detail3;
                    }

                    p.Diagnostics?.Add(kind: "ambiguous-registry-assessment",
                        course: _schedule.Get(courseLink.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail: CalculateDetail2());
                }

                var arrangedLocal = new List<LessonInstance>();
                var equationCommands = new List<LessonEquationCommand>();
                var consumedRemote = new HashSet<Uri>();
                foreach (var audience in localInstances.GroupBy(local => (
                             Groups: TopicGroups(local),
                             Partition: AttendancePartition(_schedule.Get(local.LessonId).Lesson))))
                {
                    var bucketScopes = audience.Select(Scope)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var remote = remoteInstances.Where(existing =>
                        {
                            if (!owners.TryGetValue(existing.ViewUri, out var owner))
                            {
                                return false;
                            }
                            if (!bucketScopes.Contains(owner))
                            {
                                return false;
                            }

                            if (!consumedRemote.Contains(existing.ViewUri))
                            {
                                return true;
                            }

                            return false;
                        })
                        .ToArray();
                    foreach (var existing in remote)
                    {
                        consumedRemote.Add(existing.ViewUri);
                    }

                    if (unresolvedAssessments.Any(assessment => audience.Any(lesson =>
                        {
                            bool CalculateConditionResult()
                            {
                                if (!(assessment.DateTime.Date == lesson.DateTime.Date))
                                {
                                    return false;
                                }

                                if (assessment.LessonType == LessonType.Unspecified)
                                {
                                    return true;
                                }

                                if (assessment.LessonType == lesson.RegistryLessonType)
                                {
                                    return true;
                                }

                                return false;
                            }

                            bool conditionResult = CalculateConditionResult();


                            return conditionResult;
                        })))
                    {
                        continue;
                    }

                    var local = audience.ToArray();
                    var assessmentRules = RegistryAssessments.Find(config.Assessments,
                            _schedule,
                            courseLink.CourseId,
                            audience.Key.Groups,
                            audience.Key.Partition)
                        .Where(rule => local.Any(lesson => lesson.RegistryLessonType == rule.LessonType))
                        .ToArray();
                    var arrangedAudience = RegistryAssessments.Arrange(local,
                        remote,
                        assessmentRules,
                        (lesson, topicIndex) => p.LessonTopics.Get(new AttendanceLookupKey(
                            groups: new FoundGroups
                            {
                                Value = TopicGroups(lesson),
                                IsWildcard = false,
                            },
                            groupPartition: audience.Key.Partition,
                            courseId: courseLink.CourseId,
                            lessonType: _schedule.Get(lesson.LessonId).Lesson.Type,
                            dayIndex: topicIndex,
                            dateTime: lesson.DateTime)),
                        TopicGroups);
                    var labelledLocal = arrangedAudience.Local.Select(lesson =>
                        {
                            string CalculateScope()
                            {
                                string scope2 = Scope(lesson);
                                return scope2;
                            }

                            if (labelScopes)
                            {
                                return lesson with
                                {
                                    Topic = RegistryTopicScope.Append(topic: lesson.Topic,
                                        scope: CalculateScope(),
                                        knownScopes),
                                };
                            }

                            return lesson;
                        })
                        .ToArray();
                    arrangedLocal.AddRange(labelledLocal);
                    var
                        list = equationCommands;
                    var collection =
                        RegistryScopeReconciliation.Derive(_schedule,
                            labelledLocal,
                            arrangedAudience.Remote,
                            owners,
                            Scope,
                            config.EquationCommandsDerivation,
                            inferredTypes);
                    list.AddRange(collection);
                }
                // Unowned ordinary extras retain the configured policy; assessments are protected.
                equationCommands.AddRange(remoteInstances.Where(remote => !consumedRemote.Contains(remote.ViewUri)
                        && !RegistryAssessmentTopics.IsAssessment(remote.Topic))
                    .Select(LessonEquationCommand.Delete));
                var missingTopics = arrangedLocal.Count(lesson => string.IsNullOrWhiteSpace(lesson.Topic));
                if (missingTopics != 0)
                {
                    p.Diagnostics?.Add(kind: "missing-lesson-topics",
                        course: _schedule.Get(courseLink.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail:
                        $"{missingTopics} lessons have no configured topic for {RegistrySyncDiagnostics.DescribePartition(matchedGroupPartition)}. Add or correct the topic source for this course/group partition; text is copied without translation.");
                }

                var actions = new List<RegistrySyncAction>();
                foreach (var command in equationCommands)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (config.CommandProcessingConfig.HasLog(command.Type)
                        || config.CommandProcessingConfig.HasDryRun(command.Type))
                    {
                        DateTime CalculateDateTime()
                        {
                            DateTime dateTime2;
                            if (command.HasAll)
                            {
                                dateTime2 = command.Local.DateTime;
                            }
                            else
                            {
                                dateTime2 = command.Remote.DateTime;
                            }

                            return dateTime2;
                        }

                        LogCommandBeforeExecution(CommandName: command.Type.ToString(),
                            DateString: CalculateDateTime()
                                .ToString("dd.MM.yy"),
                            LessonName: _schedule.Get(courseLink.CourseId).FullName,
                            GroupName: group.Groups.Value.ToString(_schedule),
                            command.HasAll
                                ? _schedule.Get(command.Local.LessonId).Lesson.Type
                                : command.Remote.LessonType);
                    }
                    var date = command.HasAll ? command.Local.DateTime : command.Remote.DateTime;
                    bool conditionResult3;
                    if (explicitApply)
                    {
                        conditionResult3 = true;
                    }
                    else
                    {
                        if (config.CommandProcessingConfig.HasProcess(command.Type))
                        {
                            conditionResult3 = config.CommandProcessingConfig.HasDryRun(command.Type);
                            conditionResult3 = !conditionResult3;
                        }
                        else
                        {
                            conditionResult3 = false;
                        }
                    }

                    var enabled = conditionResult3;
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

                    string? CalculateTopic()
                    {
                        string? topic2;
                        if (command.HasAll)
                        {
                            topic2 = command.Local.Topic;
                        }
                        else
                        {
                            topic2 = command.Remote.Topic;
                        }

                        return topic2;
                    }

                    int? CalculateAttendanceCount()
                    {
                        int? attendanceCount;
                        if (command.HasAll)
                        {
                            attendanceCount = command.Local.Attendance?.Length;
                        }
                        else
                        {
                            attendanceCount = command.Remote.Attendance.Length;
                        }

                        return attendanceCount;
                    }

                    actions.Add(new(kind: command.Type.ToString()
                            .ToLowerInvariant(),
                        destination: group.Uri.ToString(),
                        course: _schedule.Get(courseLink.CourseId).FullName,
                        groups: group.Groups.Value.ToString(_schedule),
                        date,
                        topic: CalculateTopic(),
                        CalculateAttendanceCount(),
                        enabled,
                        omission: omission,
                        async token =>
                        {
                            token.ThrowIfCancellationRequested();
                            var submissionStarted = false;
                            try
                            {
                                await HandleCommand(captured,
                                    addLessonUri,
                                    scanResult.Students,
                                    () =>
                                    {
                                        submissionStarted = true;
                                    });
                            }
                            catch (OperationCanceledException error)
                            {
                                throw new RegistryActionCancelledException(submissionStarted, error);
                            }
                            catch (Exception error)
                            {
                                throw new RegistryActionExecutionException(submissionStarted, error);
                            }
                        }));
                }
                if (!(await onGroupPlanned(actions)))
                {
                    return;
                }
            }

            bool CalculateConditionResult2()
            {
                if (!p.HasAttendanceSources)
                {
                    return false;
                }

                return studentCoverageComplete;
            }

            bool conditionResult2 = CalculateConditionResult2();


            if (conditionResult2)
            {
                studentCoverage.Report(_schedule,
                    courseLink.CourseId,
                    p.Diagnostics);
            }

            coverage?.ReportMissingGroups(courseLink.CourseId);
        }

        IEnumerable<RegistryExpectedTarget> ExpectedTargets()
        {
            foreach (var lesson in _schedule.EnumerateAllLessons())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ids = new List<AnyLessonId> { lesson.Id };
                var filter = new LessonSearchFilter(lesson.Lesson.Course,
                    new FoundGroups
                    {
                        Value = lesson.Lesson.Groups,
                        IsWildcard = lesson.Lesson.Groups.Count > 1,
                    },
                    lesson.Lesson.GroupPartitionKey);
                var validity = p.LessonFilter.Filter(new(filter,
                    _schedule,
                    ids));

                bool CalculateConditionResult()
                {
                    if (validity.Decision is LessonValidityDecision.Skip or LessonValidityDecision.Error)
                    {
                        return true;
                    }

                    if (ids.Count == 0)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
                {
                    continue;
                }

                bool hasDates;
                try
                {
                    hasDates = _dateTimeProvider.GetSorted(new()
                    {
                        Lessons = ids,
                        Semester = p.Semester,
                    })
                        .Any();
                }
                catch (MissingSemesterDateRangeException)
                {
                    p.Diagnostics?.Add(kind: "missing-schedule-dates",
                        course: _schedule.Get(lesson.Lesson.Course).FullName,
                        @group: lesson.Lesson.Groups.ToString(_schedule),
                        detail:
                        "Configure the semester date range for this group's grade, attendance mode, and qualification before synchronizing it.");
                    continue;
                }
                if (!hasDates)
                {
                    continue;
                }

                foreach (var parent in lesson.Lesson.Groups)
                {
                    GroupPartitionKey CalculatePartition()
                    {
                        var partition = ProjectPartition(lesson.Lesson, parent);
                        return partition;
                    }

                    yield return new(lesson.Lesson.Course,
                        parent,
                        CalculatePartition());
                }
            }
        }

        GroupPartitionKey ProjectPartition(LessonData lesson, GroupId parent)
        {
            bool conditionResult;
            if (lesson.Type != LessonType.Lab)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = lesson.Groups.IsSingleGroup;
                conditionResult = !conditionResult;
            }

            if (conditionResult)
            {
                return lesson.GroupPartitionKey;
            }

            var merge = RegistryLabMerges.Find(config.LabMerges,
                _schedule,
                lesson.Course,
                new LessonGroups { parent },
                GroupPartitionKey.All);
            if (merge is not null)
            {
                return GroupPartitionKey.All;
            }

            return lesson.GroupPartitionKey;
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
                    expectedStudents,
                    markSubmission);
            }

            async Task Create(LessonInstance lessonInstance)
            {
                await CreateOrUpdate1(
                    addLessonUri,
                    lessonInstance,
                    expectedStudents,
                    markSubmission);
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

        Task CreateOrUpdate1(
            Uri uri,
            LessonInstance lesson,
            HtmlStudent[] expectedStudents,
            Action markSubmission)
        {
            // ReSharper disable once AccessToDisposedClosure
            return CreateOrUpdate(uri,
                lesson,
                _schedule,
                expectedStudents,
                markSubmission);
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
