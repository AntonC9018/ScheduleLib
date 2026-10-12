using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

public sealed partial class AddLessonsToOnlineRegistryTaskHandler
{
    /// <summary>Deletes obsolete intake lab columns only when every scheduled lab has a more specific destination.</summary>
    public async Task PlanIntakeLabCleanup(IRegistrySyncNavigator navigator, Semester semester,
        ILessonFilter lessonFilter, RegistrySyncDiagnostics diagnostics,
        Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned, CancellationToken cancellationToken = default)
    {
        var config = _configProvider.Get() ?? throw new InvalidOperationException("Online registry not configured.");
        foreach (var assignments in (await navigator.GetCourses(semester)).GroupBy(course => course.CourseId))
        {
            var course = assignments.First();
            var destinations = new List<(GroupLink Group, RegistryLessonSelection Selection)>();
            var visited = new HashSet<Uri>();
            foreach (var assignment in assignments)
            foreach (var group in await navigator.GetGroups(assignment))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!visited.Add(group.Uri)) continue;
                var filter = new LessonSearchFilter(course.CourseId, group.Groups, group.GroupPartition);
                var selection = RegistryLessonRouting.Candidates(config, new(filter, _lookup.LessonsByCourse, _schedule));
                var decision = lessonFilter.Filter(new(filter, _schedule, selection.Lessons));
                if (decision.Decision is LessonValidityDecision.Skip or LessonValidityDecision.Error) continue;
                destinations.Add((Group: group, Selection: selection));
            }
            var routed = RegistryLessonRouting.Prioritize(_schedule, destinations, diagnostics);
            foreach (var destination in destinations)
            {
                var group = destination.Group;
                if (!group.Groups.IsWildcard && !group.Groups.IsCombined && group.Groups.Value.IsSingleGroup) continue;
                if (!destination.Selection.Lessons.Any(id => _schedule.Get(id).Lesson.Type == LessonType.Lab)) continue;
                if (destination.Selection.Lessons.Any(id => _schedule.Get(id).Lesson.Type == LessonType.Lab
                    && _schedule.Get(id).Lesson.Groups.Where(group.Groups.Value.Contains).Any(parent =>
                        !routed.Values.Any(audiences => audiences.TryGetValue(id, out var covered) && covered.Contains(parent)))))
                {
                    diagnostics.Add("registry-lab-routing-unresolved", _schedule.Get(course.CourseId).FullName,
                        group.Groups.Value.ToString(_schedule), "Unresolved lab routing prevents intake cleanup.");
                    continue;
                }
                if (routed[group.Uri].Keys.Any(id => _schedule.Get(id).Lesson.Type == LessonType.Lab))
                {
                    diagnostics.Add("registry-lab-fallback-preserved", _schedule.Get(course.CourseId).FullName,
                        group.Groups.Value.ToString(_schedule),
                        "This list is needed for scoped fallback labs. Intake cleanup leaves its lesson columns and attendance unchanged.");
                    continue;
                }
                var courseName = _schedule.Get(course.CourseId).FullName;
                var groupNames = group.Groups.Value.ToString(_schedule);
                var remote = await ReadLessons(group.Uri);
                var unwanted = remote.Where(lesson => lesson.LessonType == LessonType.Lab).ToArray();
                var marks = unwanted.Sum(lesson => lesson.Attendance.Count(IsRecordedMark));
                diagnostics.Add("unsafe-registry-lab-cleanup", courseName, groupNames,
                    $"Selected {unwanted.Length} laboratory columns and {marks} recorded attendance marks for deletion from {group.Uri}. Other lesson types are preserved.");
                var actions = unwanted.Select(lesson => new RegistrySyncAction("delete", group.Uri.ToString(),
                    courseName, groupNames, lesson.DateTime, lesson.Topic, lesson.Attendance.Count(IsRecordedMark),
                    enabled: true, omission: null, async token =>
                    {
                        var submissionStarted = false;
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            // A changed date, type, topic or attendance requires a fresh preview.
                            var current = (await ReadLessons(group.Uri)).Where(candidate => candidate.ViewUri == lesson.ViewUri).ToArray();
                            if (current.Length != 1 || !Unchanged(lesson, current[0]))
                                throw new InvalidOperationException("Registry lab changed after cleanup planning; preview again before deleting it.");
                            var document = await navigator.GetHtml(lesson.ViewUri);
                            token.ThrowIfCancellationRequested();
                            Exception? submissionError = null;
                            try { await navigator.SubmitDelete(document, () => submissionStarted = true); }
                            catch (Exception error) when (error is IOException or RegistrySubmissionRejectedException or RegistryDeletionRefusedException)
                            {
                                // A refused or unconfirmed response must be resolved through reads, never another POST.
                                submissionError = error;
                            }
                            var after = (await ReadLessons(group.Uri)).Where(candidate => candidate.ViewUri == lesson.ViewUri).ToArray();
                            if (after.Length == 1 && Unchanged(lesson, after[0]))
                                throw new RegistryDeletionRefusedException();
                            if (after.Length != 0)
                                throw new IOException("Registry lab changed during deletion; inspect the destination before retrying.", submissionError);
                        }
                        catch (OperationCanceledException error) { throw new RegistryActionCancelledException(submissionStarted, error); }
                        catch (Exception error) { throw new RegistryActionExecutionException(submissionStarted, error); }
                    })).ToArray();
                if (!await onGroupPlanned(actions)) return;
            }
        }

        async Task<RemoteLessonInstance[]> ReadLessons(Uri uri)
        {
            var document = await navigator.GetHtml(uri);
            var scan = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = document, ErrorHandler = _errorHandler,
                GetAddLessonDocument = () => navigator.GetHtml(HtmlSearch.ScanForLessonAddLink(document)),
            });
            return scan.Lessons.ToArray();
        }

        static bool IsRecordedMark(Attendance attendance)
            => attendance is Attendance.NotPresent or Attendance.MotivatedAbsent or Attendance.NotApplicable or Attendance.Grade;

        static bool Unchanged(RemoteLessonInstance expected, RemoteLessonInstance actual)
            => actual.LessonType == expected.LessonType && actual.DateTime == expected.DateTime
                && actual.Topic == expected.Topic && actual.Attendance.SequenceEqual(expected.Attendance);
    }
}
