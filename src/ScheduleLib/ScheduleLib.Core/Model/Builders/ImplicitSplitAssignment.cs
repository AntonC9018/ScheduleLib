using System.Collections.Immutable;
using System.Diagnostics;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Assigns implicit specializations and alternatives, splitting lessons whose
    /// groups resolve differently (one lesson stores one value).
    /// Assumes <c>LessonBuilderHelper.ValidateLessons</c> has been called.
    /// </summary>
    private static void AssignImplicitSplits(this ScheduleBuilder s)
    {
        if (s.ImplicitSplitConfig is not { } config
            || config.Scopes.Count == 0)
        {
            return;
        }
        StudyYear? studyYear = s.GroupParseContext?.CurrentStudyYear;
        if (studyYear is null && config.Scopes.Any(static scope => scope.StudyYear is not null))
        {
            throw MissingImplicitSplitStudyYearException.ForNoGroupParseContext();
        }
        if (studyYear is { } currentYear
            && !s.GroupParseContextIsExplicit
            && config.Scopes.Any(static scope => scope.StudyYear is not null)
            && !config.Scopes.Any(scope => scope.StudyYear == currentYear))
        {
            throw MissingImplicitSplitStudyYearException.ForNoMatchingWallClockYear(currentYear);
        }
        // A null study year matches only year-less scopes (see ImplicitSplitScope.Matches).

        // Splits append new lessons, so the loops cover only the original ones:
        // the counts are captured before any split runs.
        int weeklyCount = s.WeeklyLessons.Count;
        for (int i = 0; i < weeklyCount; i++)
        {
            Split(s.WeeklyLessons.Ref(i));
        }
        int oneTimeCount = s.OneTimeLessons.Count;
        for (int i = 0; i < oneTimeCount; i++)
        {
            Split(s.OneTimeLessons.Ref(i));
        }
        return;

        // Resolves one lesson against the captured config and study year, splitting
        // it when its groups resolve to distinct values.
        void Split(ILessonBuilderModel lesson)
        {
            if (lesson.Base.General.Course is not { } courseId
                || courseId.IsInvalid)
            {
                // Unreachable: ValidateLessons guarantees a known course on every
                // lesson (consultations included). Throws so release builds fail
                // loudly instead of silently skipping the lesson.
                Debug.Assert(false, "ValidateLessons guarantees a course on every lesson.");
                throw new InvalidOperationException("The lesson course must be initialized.");
            }
            Debug.Assert(courseId.Id >= 0 && courseId.Id < s.Courses.Count, "ValidateLessons guarantees a known course.");
            var groups = lesson.Base.Group.Groups;
            if (groups.IsEmpty)
            {
                // Consultations and other group-less lessons: nothing to resolve per group.
                return;
            }

            var names = s.Courses.Ref(courseId.Id).Names;
            // Invariant (see ValidateLessons): every referenced course has at least
            // one name; the first one identifies the course in diagnostics.
            Debug.Assert(names.Length > 0, "ValidateLessons guarantees at least one course name.");
            var courseName = names[0];

            var accumulator = new SplitAccumulator();
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
                    accumulator.AddUncovered(groupId);
                    continue;
                }
                if (spec is { } resolvedSpec)
                {
                    accumulator.NoteSpec(resolvedSpec, courseName);
                }
                if (alt is { } resolvedAlt)
                {
                    accumulator.NoteAlt(resolvedAlt, courseName);
                }
                accumulator.AddCovered(groupId, spec, alt);
            }
            if (accumulator.Parts.Count == 0)
            {
                return;
            }

            bool isSplit = accumulator.Parts.Count + (accumulator.Remainder?.Count ?? 0) > 1;
            if (isSplit)
            {
                AppendCopies();
            }
            StampOriginal();
            return;

            // The original lesson keeps the first part; the remaining parts and the
            // uncovered groups become lessons of their own. Copies are made before the
            // original is stamped, so they inherit its unassigned values.
            void AppendCopies()
            {
                foreach (var part in PartsAfterFirst())
                {
                    AppendCopy(part);
                }
            }

            void AppendCopy(Part part)
            {
                switch (lesson)
                {
                    case WeeklyLessonBuilderModel w:
                        var weeklySlot = s.WeeklyLessons.New();
                        weeklySlot.Value = new()
                        {
                            Data = new()
                            {
                                Base = CopiedBase(w.Data.Base, part, courseName),
                                Date = w.Data.Date,
                            },
                        };
                        break;
                    case OneTimeLessonBuilderModel o:
                        var oneTimeSlot = s.OneTimeLessons.New();
                        oneTimeSlot.Value = new()
                        {
                            Data = new()
                            {
                                Base = CopiedBase(o.Data.Base, part, courseName),
                                Date = o.Data.Date,
                            },
                        };
                        break;
                    default:
                        Debug.Assert(false, "All lesson model kinds are covered above.");
                        throw new InvalidOperationException(
                            "Unknown lesson model kind: cannot split the lesson.");
                }
            }

            void StampOriginal()
            {
                var part = accumulator.Parts[0];
                ref var group = ref lesson.Base.Group;
                if (isSplit)
                {
                    group.Groups = part.Groups;
                }
                Stamp(ref group, part, courseName);
            }

            IEnumerable<Part> PartsAfterFirst()
            {
                for (int i = 1; i < accumulator.Parts.Count; i++)
                {
                    yield return accumulator.Parts[i];
                }
                if (accumulator.Remainder is { } rest)
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
                    throw ConflictingImplicitAssignmentException.ForConflictingAssignment("specialization", prev.Value, value.Value, courseName);
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
                    throw ConflictingImplicitAssignmentException.ForConflictingAssignment("alternative", prev.Value, value.Value, courseName);
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
                    throw ConflictingImplicitAssignmentException.ForExplicitSpecializationConflict(courseName, group.Specialization.Value, spec.Value);
                }
                group.Specialization = spec;
            }
            if (part.Alt is { } alt)
            {
                if (group.Alternative != Alternative.All
                    && group.Alternative != alt)
                {
                    throw ConflictingImplicitAssignmentException.ForExplicitAlternativeConflict(courseName, group.Alternative.Value, alt.Value);
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

/// <summary>
/// Tracks the per-lesson split state while its groups resolve: one <see cref="Part"/>
/// per distinct (specialization, alternative) resolution, the groups covered by no
/// entry, and the single values seen so far (used to reject conflicting assignments
/// to the same course).
/// </summary>
file sealed class SplitAccumulator
{
    public readonly List<Part> Parts = new();
    public List<GroupId>? Remainder;
    public Specialization? SeenSpec;
    public Alternative? SeenAlt;

    public void AddUncovered(GroupId groupId)
    {
        (Remainder ??= []).Add(groupId);
    }

    public void NoteSpec(Specialization spec, string courseName)
    {
        if (SeenSpec is { } prev && prev != spec)
        {
            throw ConflictingImplicitAssignmentException.ForConflictingAssignment("specialization", prev.Value, spec.Value, courseName);
        }
        SeenSpec ??= spec;
    }

    public void NoteAlt(Alternative alt, string courseName)
    {
        if (SeenAlt is { } prev && prev != alt)
        {
            throw ConflictingImplicitAssignmentException.ForConflictingAssignment("alternative", prev.Value, alt.Value, courseName);
        }
        SeenAlt ??= alt;
    }

    public void AddCovered(GroupId groupId, Specialization? spec, Alternative? alt)
    {
        // Nullable.Equals compares the wrapped values with null equal to null, so two
        // groups resolving to "no value" on a dimension share one part. An explicit
        // call states that null-handling intent; lifted == would resolve the same way
        // but hides it behind operator lifting.
        int partIndex = Parts.FindIndex(p =>
            Nullable.Equals(p.Spec, spec)
            && Nullable.Equals(p.Alt, alt));
        if (partIndex == -1)
        {
            var partGroups = new LessonGroups();
            partGroups[0] = groupId;
            Parts.Add(new()
            {
                Groups = partGroups,
                Spec = spec,
                Alt = alt,
            });
        }
        else
        {
            var part = Parts[partIndex];
            part.Groups[part.Groups.Count] = groupId;
        }
    }
}

file sealed class Part
{
    public LessonGroups Groups;
    public Specialization? Spec;
    public Alternative? Alt;
}
