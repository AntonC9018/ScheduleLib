using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

public sealed partial class AddLessonsToOnlineRegistryTaskHandler
{
    /// <summary>Deletes obsolete intake lab columns only when every scheduled lab has a more specific destination.</summary>
    public async Task PlanIntakeLabCleanup(
        IRegistrySyncNavigator navigator,
        Semester semester,
        ILessonFilter lessonFilter,
        RegistrySyncDiagnostics diagnostics,
        Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned,
        CancellationToken cancellationToken = default)
    {
        var config = _configProvider.Get() ?? throw new InvalidOperationException("Online registry not configured.");
        foreach (var assignments in (await navigator.GetCourses(semester))
                 .GroupBy(course => course.CourseId))
        {
            var course = assignments.First();
            var destinations = new List<RegistryLessonDestination>();
            var visited = new HashSet<Uri>();
            foreach (var assignment in assignments)
            {
                foreach (var group in await navigator.GetGroups(assignment))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!visited.Add(group.Uri))
                    {
                        continue;
                    }

                    var filter = new LessonSearchFilter(
                        course.CourseId,
                        group.Groups,
                        group.GroupPartition);
                    LessonMatchParams parameters = new(
                        filter,
                        _lookup.LessonsByCourse,
                        _schedule);
                    var selection = RegistryLessonRouting.Candidates(config, parameters);
                    var decision = lessonFilter.Filter(new(
                        filter,
                        _schedule,
                        selection.Lessons));
                    if (decision.Decision is LessonValidityDecision.Skip or LessonValidityDecision.Error)
                    {
                        continue;
                    }

                    destinations.Add(new(Group: group, Selection: selection));
                }
            }

            var routed = RegistryLessonRouting.Prioritize(
                _schedule,
                destinations,
                diagnostics);
            foreach (var destination in destinations)
            {
                var group = destination.Group;

                bool ShouldSkipSingleGroupDestination()
                {
                    if (group.Groups.IsWildcard)
                    {
                        return false;
                    }

                    if (group.Groups.IsCombined)
                    {
                        return false;
                    }

                    if (group.Groups.Value.IsSingleGroup)
                    {
                        return true;
                    }

                    return false;
                }

                if (ShouldSkipSingleGroupDestination())
                {
                    continue;
                }

                bool IsLabLesson(AnyLessonId lessonId)
                {
                    var lesson = _schedule.Get(lessonId).Lesson;
                    return lesson.Type == LessonType.Lab;
                }

                if (!destination.Selection.Lessons.Any(IsLabLesson))
                {
                    continue;
                }

                bool IsUnresolvedLabLesson(AnyLessonId lessonId)
                {
                    if (!IsLabLesson(lessonId))
                    {
                        return false;
                    }

                    var lessonGroups = _schedule.Get(lessonId).Lesson.Groups;
                    foreach (var parent in lessonGroups.Where(group.Groups.Value.Contains))
                    {
                        if (!IsParentCoveredByMatchingDestination(lessonId, parent))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                bool IsParentCoveredByMatchingDestination(AnyLessonId lessonId, GroupId parent)
                {
                    foreach (var candidate in destinations)
                    {
                        if (candidate.Group.IsAdditionalCourses != group.IsAdditionalCourses)
                        {
                            continue;
                        }

                        if (!routed[candidate.Group.Uri].TryGetValue(lessonId, out var covered))
                        {
                            continue;
                        }

                        if (covered.Contains(parent))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                var hasUnresolvedLabRouting = destination.Selection.Lessons.Any(IsUnresolvedLabLesson);
                if (hasUnresolvedLabRouting)
                {
                    diagnostics.Add(
                        kind: "registry-lab-routing-unresolved",
                        course: _schedule.Get(course.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail: "Unresolved lab routing prevents intake cleanup.");
                    continue;
                }
                if (routed[group.Uri].Keys.Any(id => _schedule.Get(id).Lesson.Type == LessonType.Lab))
                {
                    diagnostics.Add(
                        kind: "registry-lab-fallback-preserved",
                        course: _schedule.Get(course.CourseId).FullName,
                        @group: group.Groups.Value.ToString(_schedule),
                        detail:
                        "This list is needed for scoped fallback labs. Intake cleanup leaves its lesson columns and attendance unchanged.");
                    continue;
                }
                var courseName = _schedule.Get(course.CourseId).FullName;
                var groupNames = group.Groups.Value.ToString(_schedule);
                var remote = await ReadLessons(group.Uri);
                var unwanted = remote.Where(lesson => lesson.LessonType == LessonType.Lab)
                    .ToArray();
                var marks = unwanted.Sum(lesson => lesson.Attendance.Count(IsRecordedMark));
                string detail =
                    $"Selected {unwanted.Length} laboratory columns and {marks} recorded attendance marks for deletion from {group.Uri}. Other lesson types are preserved.";
                diagnostics.Add(
                    kind: "unsafe-registry-lab-cleanup",
                    course: courseName,
                    @group: groupNames,
                    detail: detail);
                var actions = unwanted.Select(lesson => new RegistrySyncAction(
                        kind: RegistrySyncActionKind.Delete,
                        destination: group.Uri.ToString(),
                        course: courseName,
                        groups: groupNames,
                        lesson.DateTime,
                        topic: lesson.Topic,
                        lesson.Attendance.Count(IsRecordedMark),
                        enabled: true,
                        omission: null,
                        async token =>
                        {
                            await RegistryActionGuard.Run(async onSubmissionStarted =>
                            {
                                token.ThrowIfCancellationRequested();
                                // A changed date, type, topic or attendance requires a fresh preview.
                                var current = (await ReadLessons(group.Uri))
                                    .Where(candidate => candidate.ViewUri == lesson.ViewUri)
                                    .ToArray();
                                if (current.Length != 1 || !Unchanged(expected: lesson, actual: current[0]))
                                {
                                    throw new InvalidOperationException(
                                        "Registry lab changed after cleanup planning; preview again before deleting it.");
                                }

                                var document = await navigator.GetHtml(lesson.ViewUri);
                                token.ThrowIfCancellationRequested();
                                Exception? submissionError = null;
                                try
                                {
                                    await navigator.SubmitDelete(
                                        document,
                                        onSubmissionStarted);
                                }
                                catch (Exception error) when (error is IOException
                                                                  or RegistrySubmissionRejectedException
                                                                  or RegistryDeletionRefusedException)
                                {
                                    // A refused or unconfirmed response must be resolved through reads, never another POST.
                                    submissionError = error;
                                }
                                var after = (await ReadLessons(group.Uri))
                                    .Where(candidate => candidate.ViewUri == lesson.ViewUri)
                                    .ToArray();
                                if (after.Length == 1 && Unchanged(expected: lesson, actual: after[0]))
                                {
                                    throw new RegistryDeletionRefusedException();
                                }

                                if (after.Length != 0)
                                {
                                    throw new IOException(
                                        "Registry lab changed during deletion; inspect the destination before retrying.",
                                        submissionError);
                                }
                            });
                        }))
                    .ToArray();
                if (!(await onGroupPlanned(actions)))
                {
                    return;
                }
            }
        }

        async Task<RemoteLessonInstance[]> ReadLessons(Uri uri)
        {
            var document = await navigator.GetHtml(uri);
            var scan = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = document,
                ErrorHandler = _errorHandler,
                GetAddLessonDocument = () =>
                {
                    var iRegistrySyncNavigator = navigator;
                    var uri = HtmlSearch.ScanForLessonAddLink(document);
                    var html = iRegistrySyncNavigator.GetHtml(uri);
                    return html;
                },
            });
            return scan.Lessons.ToArray();
        }

        static bool IsRecordedMark(Attendance attendance)
        {
            return attendance is Attendance.NotPresent
                or Attendance.MotivatedAbsent
                or Attendance.NotApplicable
                or Attendance.Grade;
        }

        static bool Unchanged(RemoteLessonInstance expected, RemoteLessonInstance actual)
        {
            if (actual.LessonType != expected.LessonType)
            {
                return false;
            }

            if (!(actual.DateTime == expected.DateTime))
            {
                return false;
            }

            if (actual.Topic != expected.Topic)
            {
                return false;
            }

            if (actual.Attendance.SequenceEqual(expected.Attendance))
            {
                return true;
            }

            return false;
        }
    }
}
