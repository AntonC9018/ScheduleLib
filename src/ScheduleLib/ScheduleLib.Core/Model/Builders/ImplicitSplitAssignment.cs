using System.Collections.Immutable;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Assigns the implicitly configured specializations and alternatives to lessons of
    /// matching courses. Runs after <see cref="ScheduleBuilderHelper.ClassifySubGroups"/>,
    /// so explicitly labeled lessons are already classified and contradictions with the
    /// configuration get detected here.
    /// <para>
    /// A lesson covering several groups can resolve differently per group: the groups
    /// covered by a configuration entry receive the value, the rest stay without one.
    /// A single lesson stores a single value, so such a lesson is split into one lesson
    /// per distinct resolution, each carrying only the groups it represents. The shared
    /// time, room and teachers are copied to every part.
    /// </para>
    /// </summary>
    private static void AssignImplicitSplits(this ScheduleBuilder s)
    {
        if (s.ImplicitSplitConfig is not { } config
            || config.Scopes.Count == 0)
        {
            return;
        }
        StudyYear? studyYear = s.GroupParseContext?.CurrentStudyYear;

        // Splits append new lessons, so the loops cover only the original ones.
        int weeklyCount = s.WeeklyLessons.Count;
        for (int i = 0; i < weeklyCount; i++)
        {
            Split(s, s.WeeklyLessons.Ref(i), config, studyYear);
        }
        int oneTimeCount = s.OneTimeLessons.Count;
        for (int i = 0; i < oneTimeCount; i++)
        {
            Split(s, s.OneTimeLessons.Ref(i), config, studyYear);
        }
        return;

        void Split(
            ScheduleBuilder s,
            ILessonBuilderModel lesson,
            ImplicitSplitConfig config,
            StudyYear? studyYear)
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

            var names = s.Courses.Ref(courseId.Value.Id).Names;
            var courseName = names.Length > 0 ? names[0] : courseId.Value.Id.ToString();

            var parts = new List<Part>();
            List<GroupId>? remainder = null;
            Specialization? seenSpec = null;
            Alternative? seenAlt = null;
            foreach (var groupId in groups)
            {
                var group = s.Groups.Ref(groupId.Value);
                Specialization? spec = null;
                Alternative? alt = null;
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
                    ResolveSpecialization(scope, names, courseName, ref spec);
                    ResolveAlternative(scope, names, courseName, ref alt);
                }
                if (spec is null && alt is null)
                {
                    // The group is covered by no entry: its part of the lesson keeps
                    // going out without a specialization.
                    (remainder ??= []).Add(groupId);
                    continue;
                }
                if (spec is { } resolvedSpec)
                {
                    if (seenSpec is { } prevSpec && prevSpec != resolvedSpec)
                    {
                        throw new InvalidOperationException(
                            $"The implicit split configuration assigns conflicting specializations "
                            + $"'{prevSpec.Value}' and '{resolvedSpec.Value}' to course '{courseName}'.");
                    }
                    seenSpec ??= resolvedSpec;
                }
                if (alt is { } resolvedAlt)
                {
                    if (seenAlt is { } prevAlt && prevAlt != resolvedAlt)
                    {
                        throw new InvalidOperationException(
                            $"The implicit split configuration assigns conflicting alternatives "
                            + $"'{prevAlt.Value}' and '{resolvedAlt.Value}' to course '{courseName}'.");
                    }
                    seenAlt ??= resolvedAlt;
                }
                int partIndex = parts.FindIndex(p =>
                    Nullable.Equals(p.Spec, spec)
                    && Nullable.Equals(p.Alt, alt));
                if (partIndex == -1)
                {
                    var partGroups = new LessonGroups();
                    partGroups[0] = groupId;
                    parts.Add(new()
                    {
                        Groups = partGroups,
                        Spec = spec,
                        Alt = alt,
                    });
                }
                else
                {
                    var part = parts[partIndex];
                    part.Groups[part.Groups.Count] = groupId;
                }
            }
            if (parts.Count == 0)
            {
                return;
            }

            bool isSplit = parts.Count + (remainder?.Count ?? 0) > 1;
            // The original lesson keeps the first part; the remaining parts and the
            // uncovered groups become lessons of their own. Copies are made before the
            // original is stamped, so they inherit its unassigned values.
            if (isSplit)
            {
                switch (lesson)
                {
                    case WeeklyLessonBuilderModel w:
                        foreach (var part in PartsAfterFirst())
                        {
                            var slot = s.WeeklyLessons.New();
                            slot.Value = new()
                            {
                                Data = new()
                                {
                                    Base = CopiedBase(w.Data.Base, part, courseName),
                                    Date = w.Data.Date,
                                },
                            };
                        }
                        break;
                    case OneTimeLessonBuilderModel o:
                        foreach (var part in PartsAfterFirst())
                        {
                            var slot = s.OneTimeLessons.New();
                            slot.Value = new()
                            {
                                Data = new()
                                {
                                    Base = CopiedBase(o.Data.Base, part, courseName),
                                    Date = o.Data.Date,
                                },
                            };
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown lesson model kind: cannot split the lesson.");
                }
            }

            {
                var part = parts[0];
                ref var group = ref lesson.Base.Group;
                if (isSplit)
                {
                    group.Groups = part.Groups;
                }
                Stamp(ref group, part, courseName);
            }
            return;

            IEnumerable<Part> PartsAfterFirst()
            {
                for (int i = 1; i < parts.Count; i++)
                {
                    yield return parts[i];
                }
                if (remainder is { } rest)
                {
                    var partGroups = new LessonGroups();
                    for (int i = 0; i < rest.Count; i++)
                    {
                        partGroups[i] = rest[i];
                    }
                    yield return new()
                    {
                        Groups = partGroups,
                        Spec = null,
                        Alt = null,
                    };
                }
            }
        }

        static void ResolveSpecialization(
            ImplicitSplitScope scope,
            ImmutableArray<string> names,
            string courseName,
            ref Specialization? assigned)
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

        static void ResolveAlternative(
            ImplicitSplitScope scope,
            ImmutableArray<string> names,
            string courseName,
            ref Alternative? assigned)
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

        static void Stamp(
            ref LessonBuilderGroupData group,
            Part part,
            string courseName)
        {
            if (part.Spec is { } spec)
            {
                if (group.Specialization != Specialization.All
                    && group.Specialization != spec)
                {
                    throw new InvalidOperationException(
                        $"The lesson for course '{courseName}' has the explicit specialization "
                        + $"'{group.Specialization.Value}', but the implicit split configuration "
                        + $"assigns '{spec.Value}'.");
                }
                group.Specialization = spec;
            }
            if (part.Alt is { } alt)
            {
                if (group.Alternative != Alternative.All
                    && group.Alternative != alt)
                {
                    throw new InvalidOperationException(
                        $"The lesson for course '{courseName}' has the explicit alternative "
                        + $"'{group.Alternative.Value}', but the implicit split configuration "
                        + $"assigns '{alt.Value}'.");
                }
                group.Alternative = alt;
            }
        }

        static LessonBuilderModelDataBase CopiedBase(
            in LessonBuilderModelDataBase src,
            Part part,
            string courseName)
        {
            var copy = src;
            copy.General.Teachers = new(src.General.Teachers);
            copy.Group.Groups = part.Groups;
            Stamp(ref copy.Group, part, courseName);
            return copy;
        }
    }
}

file sealed class Part
{
    public LessonGroups Groups;
    public Specialization? Spec;
    public Alternative? Alt;
}
