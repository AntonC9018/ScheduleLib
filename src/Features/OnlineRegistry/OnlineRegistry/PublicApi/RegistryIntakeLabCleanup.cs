using ScheduleLib.Dates;

namespace ScheduleLib.OnlineRegistry;

public sealed partial class AddLessonsToOnlineRegistryTaskHandler
{
    /// <summary>Deletes only lab columns from the unsafe destinations that ordinary synchronization skips.</summary>
    public async Task PlanIntakeLabCleanup(IRegistrySyncNavigator navigator, Semester semester,
        ILessonFilter lessonFilter, RegistrySyncDiagnostics diagnostics,
        Func<IReadOnlyList<RegistrySyncAction>, Task<bool>> onGroupPlanned, CancellationToken cancellationToken = default)
    {
        var visited = new HashSet<Uri>();
        foreach (var course in await navigator.GetCourses(semester))
        foreach (var group in await navigator.GetGroups(course))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!visited.Add(group.Uri)) continue;
            var filter = new LessonSearchFilter(course.CourseId, group.Groups, group.GroupPartition);
            var labs = MatchLessonHelper.FindUnsafeIntakeLabs(new(filter, _lookup.LessonsByCourse, _schedule));
            if (labs.Count == 0) continue;
            var decision = lessonFilter.Filter(new(filter, _schedule, labs));
            if (decision.Decision is LessonValidityDecision.Skip or LessonValidityDecision.Error || labs.Count == 0) continue;

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
