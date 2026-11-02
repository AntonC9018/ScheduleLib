using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.OnlineRegistry;

/// <summary>The category of one actionable reconciliation gap.</summary>
[JsonConverter(typeof(RegistrySyncDiagnosticKindJsonConverter))]
public enum RegistrySyncDiagnosticKind
{
    /// <summary>Registry-student coverage: missing-registry-course.</summary>
    MissingRegistryCourse,
    /// <summary>Registry-student coverage: missing-registry-group.</summary>
    MissingRegistryGroup,
    /// <summary>Registry-student coverage: unmatched-registry-course.</summary>
    UnmatchedRegistryCourse,
    /// <summary>Registry-student coverage: unmatched-registry-group.</summary>
    UnmatchedRegistryGroup,
    /// <summary>Registry-student coverage: unrecognized-registry-group.</summary>
    UnrecognizedRegistryGroup,
    /// <summary>Registry-student coverage: ambiguous-registry-destination.</summary>
    AmbiguousRegistryDestination,
    /// <summary>Attendance-student coverage: attendance-students-not-in-registry.</summary>
    AttendanceStudentsNotInRegistry,
    /// <summary>Attendance-student coverage: unmatched-attendance-students.</summary>
    UnmatchedAttendanceStudents,
    /// <summary>Lesson import: registry-assignment-mismatch.</summary>
    RegistryAssignmentMismatch,
    /// <summary>Lesson import: registry-partition-mismatch.</summary>
    RegistryPartitionMismatch,
    /// <summary>Lesson import: registry-deletion-refused.</summary>
    RegistryDeletionRefused,
    /// <summary>Lesson import: empty-registry-roster.</summary>
    EmptyRegistryRoster,
    /// <summary>Lesson import: missing-schedule-dates.</summary>
    MissingScheduleDates,
    /// <summary>Lesson import: missing-attendance-sheet.</summary>
    MissingAttendanceSheet,
    /// <summary>Lesson import: ambiguous-registry-assessment.</summary>
    AmbiguousRegistryAssessment,
    /// <summary>Lesson import: missing-lesson-topics.</summary>
    MissingLessonTopics,
    /// <summary>Intake lab cleanup: registry-lab-routing-unresolved.</summary>
    RegistryLabRoutingUnresolved,
    /// <summary>Intake lab cleanup: registry-lab-fallback-preserved.</summary>
    RegistryLabFallbackPreserved,
    /// <summary>Intake lab cleanup: unsafe-registry-lab-cleanup.</summary>
    UnsafeRegistryLabCleanup,
}

public static class RegistrySyncDiagnosticKindExtensions
{
    public static string ToWire(this RegistrySyncDiagnosticKind kind)
    {
        return kind switch
        {
            RegistrySyncDiagnosticKind.MissingRegistryCourse => "missing-registry-course",
            RegistrySyncDiagnosticKind.MissingRegistryGroup => "missing-registry-group",
            RegistrySyncDiagnosticKind.UnmatchedRegistryCourse => "unmatched-registry-course",
            RegistrySyncDiagnosticKind.UnmatchedRegistryGroup => "unmatched-registry-group",
            RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup => "unrecognized-registry-group",
            RegistrySyncDiagnosticKind.AmbiguousRegistryDestination => "ambiguous-registry-destination",
            RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry =>
                "attendance-students-not-in-registry",
            RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents => "unmatched-attendance-students",
            RegistrySyncDiagnosticKind.RegistryAssignmentMismatch => "registry-assignment-mismatch",
            RegistrySyncDiagnosticKind.RegistryPartitionMismatch => "registry-partition-mismatch",
            RegistrySyncDiagnosticKind.RegistryDeletionRefused => "registry-deletion-refused",
            RegistrySyncDiagnosticKind.EmptyRegistryRoster => "empty-registry-roster",
            RegistrySyncDiagnosticKind.MissingScheduleDates => "missing-schedule-dates",
            RegistrySyncDiagnosticKind.MissingAttendanceSheet => "missing-attendance-sheet",
            RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment => "ambiguous-registry-assessment",
            RegistrySyncDiagnosticKind.MissingLessonTopics => "missing-lesson-topics",
            RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved => "registry-lab-routing-unresolved",
            RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved => "registry-lab-fallback-preserved",
            RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup => "unsafe-registry-lab-cleanup",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }
}

public sealed class RegistrySyncDiagnosticKindJsonConverter : WireNameJsonConverter<RegistrySyncDiagnosticKind>
{
    protected override string ToWire(RegistrySyncDiagnosticKind value) => value.ToWire();
}

public sealed record RegistrySyncDiagnostic(
    RegistrySyncDiagnosticKind Kind,
    string? Course,
    string? Group,
    string Detail);

/// <summary>Actionable reconciliation gaps, without credentials or student records.</summary>
public sealed class RegistrySyncDiagnostics
{
    private readonly List<RegistrySyncDiagnostic> _items = [];
    private readonly HashSet<RegistrySyncDiagnostic> _seen = [];
    public IReadOnlyList<RegistrySyncDiagnostic> Items => _items;
    public event Action<RegistrySyncDiagnostic>? Reported;

    public static string DescribePartition(GroupPartitionKey partition)
    {
        if (partition == GroupPartitionKey.All)
        {
            return "whole group";
        }

        var parts = new List<string>();
        if (partition.SubGroup != SubGroup.All)
        {
            var list = parts;
            string item = $"subgroup {partition.SubGroup.Value}";
            list.Add(item);
        }

        if (partition.Specialization != Specialization.All)
        {
            var list2 = parts;
            string item2 = $"specialization {partition.Specialization.Value}";
            list2.Add(item2);
        }

        if (partition.Alternative != Alternative.All)
        {
            var list3 = parts;
            string item3 = $"alternative {partition.Alternative.Value}";
            list3.Add(item3);
        }

        return string.Join(", ", parts);
    }

    private void Add(
        RegistrySyncDiagnosticKind kind,
        string? course,
        string? group,
        string detail)
    {
        var item = new RegistrySyncDiagnostic(
            Kind: kind,
            Course: course,
            Group: group,
            Detail: detail);
        if (_seen.Add(item))
        {
            _items.Add(item);
            Reported?.Invoke(item);
        }
    }

    /// <summary>A scheduled course is absent from the teacher's registry scope.</summary>
    public void ReportMissingRegistryCourse(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.MissingRegistryCourse, course, group, detail);

    /// <summary>No registry list matched the scheduled partition of a course.</summary>
    public void ReportMissingRegistryGroup(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.MissingRegistryGroup, course, group, detail);

    /// <summary>The registry has no course entry matching a scheduled course name.</summary>
    public void ReportUnmatchedRegistryCourse(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.UnmatchedRegistryCourse, course, group, detail);

    /// <summary>A registry group name could not be matched to the schedule.</summary>
    public void ReportUnmatchedRegistryGroup(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.UnmatchedRegistryGroup, course, group, detail);

    /// <summary>A registry group label could not be parsed into a schedule group.</summary>
    public void ReportUnrecognizedRegistryGroup(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup, course, group, detail);

    /// <summary>Equally specific registry lists compete for one lab audience.</summary>
    public void ReportAmbiguousRegistryDestination(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.AmbiguousRegistryDestination, course, group, detail);

    /// <summary>Attendance students were not found in any relevant registry roster.</summary>
    public void ReportAttendanceStudentsNotInRegistry(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry, course, group, detail);

    /// <summary>Registry students remained after all relevant rosters were considered.</summary>
    public void ReportUnmatchedAttendanceStudents(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents, course, group, detail);

    /// <summary>The registry destination has no matching schedule lessons for this teacher.</summary>
    public void ReportRegistryAssignmentMismatch(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.RegistryAssignmentMismatch, course, group, detail);

    /// <summary>No scheduled lesson matches the partition warmed by the registry destination.</summary>
    public void ReportRegistryPartitionMismatch(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.RegistryPartitionMismatch, course, group, detail);

    /// <summary>The registry refused a lesson deletion without retrying another POST.</summary>
    public void ReportRegistryDeletionRefused(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.RegistryDeletionRefused, course, group, detail);

    /// <summary>The registry destination has no student rows.</summary>
    public void ReportEmptyRegistryRoster(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.EmptyRegistryRoster, course, group, detail);

    /// <summary>The semester date range for a group's cohort is not configured.</summary>
    public void ReportMissingScheduleDates(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.MissingScheduleDates, course, group, detail);

    /// <summary>No attendance rows were loaded for a course/group partition.</summary>
    public void ReportMissingAttendanceSheet(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.MissingAttendanceSheet, course, group, detail);

    /// <summary>An assessment entry has no scope identifying its audience.</summary>
    public void ReportAmbiguousRegistryAssessment(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment, course, group, detail);

    /// <summary>Arranged lessons have no configured topic for a course/group partition.</summary>
    public void ReportMissingLessonTopics(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.MissingLessonTopics, course, group, detail);

    /// <summary>Unresolved lab routing prevents intake column cleanup.</summary>
    public void ReportRegistryLabRoutingUnresolved(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved, course, group, detail);

    /// <summary>A list needed for scoped fallback keeps its lab columns and attendance.</summary>
    public void ReportRegistryLabFallbackPreserved(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved, course, group, detail);

    /// <summary>Intake cleanup selected unsafe laboratory columns for deletion.</summary>
    public void ReportUnsafeRegistryLabCleanup(string? course, string? group, string detail) =>
        Add(RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup, course, group, detail);
}

internal readonly record struct RegistryExpectedTarget(CourseId Course, GroupId Group, GroupPartitionKey Partition);

internal sealed class RegistryTargetCoverage(
    Schedule schedule,
    IEnumerable<RegistryExpectedTarget> targets,
    RegistrySyncDiagnostics diagnostics)
{
    private readonly HashSet<RegistryExpectedTarget> _remaining = targets.ToHashSet();

    public void ReportMissingCourses(IReadOnlySet<CourseId> courses)
    {
        foreach (var target in _remaining.Where(target => !courses.Contains(target.Course))
                     .ToArray())
        {
            string CalculateGroup()
            {
                var get = schedule.Get(target.Group);
                string group2 = get.Name;
                return group2;
            }

            diagnostics.ReportMissingRegistryCourse(
                course: schedule.Get(target.Course).FullName,
                @group: CalculateGroup(),
                detail: "Ask the registry administrator to assign this scheduled course to the teacher.");
            _remaining.Remove(target);
        }
    }

    public void Cover(RegistryExpectedTarget target) => _remaining.Remove(target);

    public void CoverLesson(
        AnyLessonId lessonId,
        GroupId parent,
        GroupPartitionKey partition,
        IReadOnlyList<RegistryGroupMatchRule> rules)
    {
        var source = schedule.Get(lessonId);
        RegistryExpectedTarget target2 = new(
            source.Lesson.Course,
            parent,
            partition);
        Cover(target2);

        bool CheckCondition()
        {
            if (source.Lesson.Type is not (LessonType.Curs or LessonType.Prelegere))
            {
                return true;
            }

            if (partition != GroupPartitionKey.All)
            {
                return true;
            }

            if (!source.Lesson.Groups.IsSingleGroup)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            return;
        }

        var host = schedule.Get(parent);
        if (host.Language is not (Language.Ro or Language.Ru))
        {
            return;
        }

        var intake = schedule.EnumerateGroups()
            .Where(group => SameIntake(first: host, second: group.Item))
            .ToArray();
        if (intake.Any(group => group.Item.Language == Language.None)
            || intake.GroupBy(group => group.Item.Language)
                .Any(groups => groups.Count() > 1))
        {
            return;
        }

        foreach (var target in _remaining.Where(target => target.Course == source.Lesson.Course)
                     .ToArray())
        {
            var audience = schedule.Get(target.Group);

            bool CheckCondition2()
            {
                if (!SameIntake(first: host, second: audience))
                {
                    return true;
                }

                if (target.Partition
                    != new GroupPartitionKey(SpecialSubGroups.FromLanguage(host.Language), Specialization.All))
                {
                    return true;
                }

                if (!rules.Any(rule =>
                    {
                        if (!rule.MatchByLanguage)
                        {
                            return false;
                        }
                        if (!rule.AllowParentGroup)
                        {
                            return false;
                        }
                        if (!rule.Faculty.Equals(audience.Faculty.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                        if (rule.Language != audience.Language)
                        {
                            return false;
                        }
                        if (rule.Language == host.Language)
                        {
                            return false;
                        }
                        if (rule.SubGroup == SpecialSubGroups.FromLanguage(audience.Language).Value)
                        {
                            return true;
                        }

                        return false;
                    }))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition2())
            {
                continue;
            }

            // Language partitions in a separate parent can attend the exact same lecture.
            // Its existing parent-language destination already synchronizes that event.
            var shared = schedule.EnumerateAllLessons()
                .Any(candidate =>
                    candidate.Lesson.Groups.IsSingleGroup
                    && candidate.Lesson.Groups[0] == target.Group
                    && candidate.Lesson.GroupPartitionKey == target.Partition
                    && (candidate.Lesson with
                    {
                        Groups = source.Lesson.Groups,
                        SubGroup = source.Lesson.SubGroup,
                    })
                    == source.Lesson
                    && SameDate(first: source, second: candidate));
            if (shared)
            {
                Cover(target);
            }
        }
    }

    private static bool SameIntake(Group first, Group second)
    {
        if (!first.Faculty.Name.Equals(second.Faculty.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!(first.Grade == second.Grade))
        {
            return false;
        }

        if (first.AttendanceMode != second.AttendanceMode)
        {
            return false;
        }

        if (first.QualificationType == second.QualificationType)
        {
            return true;
        }

        return false;
    }

    private static bool SameDate(AnyLessonAccessor first, AnyLessonAccessor second)
    {
        if (first.Weekly is { } weekly && second.Weekly is { } otherWeekly)
        {
            if (weekly.Date == otherWeekly.Date)
            {
                return true;
            }

            return false;
        }

        if (!(first.OneTime is { } oneTime && second.OneTime is { } otherOneTime))
        {
            return false;
        }

        if (oneTime.Date == otherOneTime.Date)
        {
            return true;
        }

        return false;
    }

    public void ReportMissingGroups(CourseId course)
    {
        foreach (var target in _remaining.Where(target => target.Course == course)
                     .ToArray())
        {
            var partition = RegistrySyncDiagnostics.DescribePartition(target.Partition);

            string CalculateGroup()
            {
                var get = schedule.Get(target.Group);
                string group2 = get.Name;
                return group2;
            }

            diagnostics.ReportMissingRegistryGroup(
                course: schedule.Get(course).FullName,
                @group: CalculateGroup(),
                detail:
                $"No registry destination matched the scheduled partition {partition}. Ask the registry administrator to add or correct its group assignment.");
            _remaining.Remove(target);
        }
    }
}
