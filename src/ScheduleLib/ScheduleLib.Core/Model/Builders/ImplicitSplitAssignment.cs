using System.Collections.Immutable;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Assigns the implicitly configured specializations and alternatives to lessons of
    /// matching courses. Runs after <see cref="ScheduleBuilderHelper.ClassifySubGroups"/>,
    /// so explicitly labeled lessons are already classified and contradictions with the
    /// configuration get detected here.
    /// </summary>
    public static void AssignImplicitSplits(this ScheduleBuilder s)
    {
        if (s.ImplicitSplitConfig is not { } config
            || config.Scopes.Count == 0)
        {
            return;
        }
        StudyYear? studyYear = s.GroupParseContext?.CurrentStudyYear;

        foreach (var lesson in s.WeeklyLessons.List)
        {
            Assign(lesson);
        }
        foreach (var lesson in s.OneTimeLessons.List)
        {
            Assign(lesson);
        }

        void Assign(ILessonBuilderModel lesson)
        {
            var courseId = lesson.Base.General.Course;
            if (courseId is null || courseId.Value.IsInvalid)
            {
                return;
            }
            var groups = lesson.Base.Group.Groups;
            if (groups.IsEmpty)
            {
                return;
            }

            // Every group of the lesson must fall into a scope. A lesson shared with
            // groups outside every scope stays unattributed: one stored value could not
            // represent both populations.
            List<ImplicitSplitScope>? matchingScopes = null;
            foreach (var groupId in groups)
            {
                var group = s.Groups.Ref(groupId.Value);
                bool matched = false;
                foreach (var scope in config.Scopes)
                {
                    if (!scope.Matches(
                            studyYear,
                            group.Grade,
                            group.Faculty,
                            group.AttendanceMode,
                            group.QualificationType))
                    {
                        continue;
                    }
                    matched = true;
                    (matchingScopes ??= []).Add(scope);
                }
                if (!matched)
                {
                    return;
                }
            }

            var names = s.Courses.Ref(courseId.Value.Id).Names;
            var courseName = names.Length > 0 ? names[0] : courseId.Value.Id.ToString();

            AssignSpecialization(lesson, names, courseName, matchingScopes!);
            AssignAlternative(lesson, names, courseName, matchingScopes!);
        }

        static void AssignSpecialization(
            ILessonBuilderModel lesson,
            ImmutableArray<string> names,
            string courseName,
            List<ImplicitSplitScope> matchingScopes)
        {
            Specialization? assigned = null;
            foreach (var scope in matchingScopes)
            {
                foreach (var name in names)
                {
                    if (!scope.SpecializationCourses.TryGetValue(name, out var value))
                    {
                        continue;
                    }
                    if (assigned is { } prev && prev != value)
                    {
                        throw new InvalidOperationException(
                            $"The implicit split configuration assigns conflicting specializations "
                            + $"'{prev.Value}' and '{value.Value}' to course '{courseName}'.");
                    }
                    assigned = value;
                }
            }
            if (assigned is not { } assignment)
            {
                return;
            }

            ref var group = ref lesson.Base.Group;
            if (group.Specialization != Specialization.All
                && group.Specialization != assignment)
            {
                throw new InvalidOperationException(
                    $"The lesson for course '{courseName}' has the explicit specialization "
                    + $"'{group.Specialization.Value}', but the implicit split configuration "
                    + $"assigns '{assignment.Value}'.");
            }
            group.Specialization = assignment;
        }

        static void AssignAlternative(
            ILessonBuilderModel lesson,
            ImmutableArray<string> names,
            string courseName,
            List<ImplicitSplitScope> matchingScopes)
        {
            Alternative? assigned = null;
            foreach (var scope in matchingScopes)
            {
                foreach (var name in names)
                {
                    if (!scope.AlternativeCourses.TryGetValue(name, out var value))
                    {
                        continue;
                    }
                    if (assigned is { } prev && prev != value)
                    {
                        throw new InvalidOperationException(
                            $"The implicit split configuration assigns conflicting alternatives "
                            + $"'{prev.Value}' and '{value.Value}' to course '{courseName}'.");
                    }
                    assigned = value;
                }
            }
            if (assigned is not { } assignment)
            {
                return;
            }

            ref var group = ref lesson.Base.Group;
            if (group.Alternative != Alternative.All
                && group.Alternative != assignment)
            {
                throw new InvalidOperationException(
                    $"The lesson for course '{courseName}' has the explicit alternative "
                    + $"'{group.Alternative.Value}', but the implicit split configuration "
                    + $"assigns '{assignment.Value}'.");
            }
            group.Alternative = assignment;
        }
    }
}
