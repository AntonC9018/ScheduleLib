using System.Text.Json.Serialization;
using ScheduleLib.Helper.JsonConverters;
using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

[JsonConverter(typeof(RegistrySyncDiagnosticKindJsonConverter))]
public enum RegistrySyncDiagnosticKind
{
    MissingRegistryCourse,
    MissingRegistryGroup,
    UnmatchedRegistryCourse,
    UnmatchedRegistryGroup,
    UnrecognizedRegistryGroup,
    AmbiguousRegistryDestination,
    AttendanceStudentsNotInRegistry,
    UnmatchedAttendanceStudents,
    RegistryAssignmentMismatch,
    RegistryPartitionMismatch,
    RegistryDeletionRefused,
    EmptyRegistryRoster,
    MissingScheduleDates,
    MissingAttendanceSheet,
    AmbiguousRegistryAssessment,
    MissingLessonTopics,
    RegistryLabRoutingUnresolved,
    RegistryLabFallbackPreserved,
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
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind),
                kind,
                null),
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
            parts.Add($"subgroup {partition.SubGroup.Value}");
        }

        if (partition.Specialization != Specialization.All)
        {
            parts.Add($"specialization {partition.Specialization.Value}");
        }

        if (partition.Alternative != Alternative.All)
        {
            parts.Add($"alternative {partition.Alternative.Value}");
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

    public void ReportMissingRegistryCourse(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.MissingRegistryCourse,
            course: course,
            @group: group,
            detail: "Ask the registry administrator to assign this scheduled course to the teacher.");
    }

    public void ReportMissingRegistryGroup(
        string? course,
        string? group,
        GroupPartitionKey partition)
    {
        var partitionDescription = DescribePartition(partition);
        Add(
            RegistrySyncDiagnosticKind.MissingRegistryGroup,
            course: course,
            @group: group,
            detail:
            $"No registry destination matched the scheduled partition {partitionDescription}. Ask the registry administrator to add or correct its group assignment.");
    }

    public void ReportUnmatchedRegistryCourse(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.UnmatchedRegistryCourse,
            course: course,
            @group: group,
            detail: "Add or correct the course mapping in the schedule configuration.");
    }

    public void ReportUnmatchedRegistryGroup(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.UnmatchedRegistryGroup,
            course: course,
            @group: group,
            detail: "Check the parent group, language, and subgroup mapping in the schedule configuration.");
    }

    public void ReportUnrecognizedRegistryGroup(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.UnrecognizedRegistryGroup,
            course: course,
            @group: group,
            detail: "Correct the registry group label or add support for its format.");
    }

    public void ReportAmbiguousRegistryDestination(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.AmbiguousRegistryDestination,
            course: course,
            @group: group,
            detail:
            "Multiple equally specific registry lists cover this lab audience. Correct the assignments; this audience was not routed by list order.");
    }

    public void ReportAttendanceStudentsNotInRegistry(
        string? course,
        string? group,
        IReadOnlyCollection<Name> students,
        LessonType lessonType,
        GroupPartitionKey partition)
    {
        var studentList = DescribeStudents(students);
        Add(
            RegistrySyncDiagnosticKind.AttendanceStudentsNotInRegistry,
            course: course,
            @group: group,
            detail:
            $"{students.Count} attendance students have no matching student in any relevant registry roster for {lessonType} ({DescribePartition(partition)}): {studentList}. Check enrollment and name mappings.");
    }

    public void ReportUnmatchedAttendanceStudents(
        string? course,
        string? group,
        IReadOnlyCollection<Name> students,
        string lessonTypes,
        string partitions)
    {
        var studentList = DescribeStudents(students);
        Add(
            RegistrySyncDiagnosticKind.UnmatchedAttendanceStudents,
            course: course,
            @group: group,
            detail:
            $"{students.Count} registry students have no matching attendance row after all relevant rosters were considered for {lessonTypes} ({partitions}): {studentList}. Check attendance worksheets and name mappings.");
    }

    private static string DescribeStudents(IEnumerable<Name> students)
    {
        return string.Join("; ", students.Select(student => student.ToString()));
    }

    public void ReportRegistryAssignmentMismatch(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.RegistryAssignmentMismatch,
            course: course,
            @group: group,
            detail: "The registry destination has no matching lessons assigned to this teacher. Check its assignment.");
    }

    public void ReportRegistryPartitionMismatch(
        string? course,
        string? group,
        GroupPartitionKey partition)
    {
        var partitionDescription = DescribePartition(partition);
        Add(
            RegistrySyncDiagnosticKind.RegistryPartitionMismatch,
            course: course,
            @group: group,
            detail:
            $"No schedule lessons match {partitionDescription}. Check the registry assignment and schedule partition mapping.");
    }

    public void ReportRegistryDeletionRefused(
        string? course,
        string? group,
        DateTime date)
    {
        Add(
            RegistrySyncDiagnosticKind.RegistryDeletionRefused,
            course: course,
            @group: group,
            detail:
            $"Skipped deletion on {date:yyyy-MM-dd HH:mm}: the lesson remains unchanged. No retry was performed.");
    }

    public void ReportEmptyRegistryRoster(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.EmptyRegistryRoster,
            course: course,
            @group: group,
            detail:
            "The registry destination has no student rows. Ask the registry administrator to check its enrollment; absences cannot be copied to an empty roster.");
    }

    public void ReportMissingScheduleDates(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.MissingScheduleDates,
            course: course,
            @group: group,
            detail:
            "Configure the semester date range for this group's grade, attendance mode, and qualification before synchronizing it.");
    }

    public void ReportMissingAttendanceSheet(
        string? course,
        string? group,
        LessonType lessonType,
        GroupPartitionKey partition)
    {
        Add(
            RegistrySyncDiagnosticKind.MissingAttendanceSheet,
            course: course,
            @group: group,
            detail:
            $"No attendance rows were loaded for {lessonType} ({DescribePartition(partition)}). Configure or correct the attendance worksheet for this course/group partition.");
    }

    public void ReportAmbiguousRegistryAssessment(
        string? course,
        string? group,
        DateTime date)
    {
        Add(
            RegistrySyncDiagnosticKind.AmbiguousRegistryAssessment,
            course: course,
            @group: group,
            detail:
            $"Assessment on {date:yyyy-MM-dd HH:mm} has no scope identifying its scheduled audience. Its entry and candidate sessions were preserved.");
    }

    public void ReportMissingLessonTopics(
        string? course,
        string? group,
        int count,
        GroupPartitionKey partition)
    {
        Add(
            RegistrySyncDiagnosticKind.MissingLessonTopics,
            course: course,
            @group: group,
            detail:
            $"{count} lessons have no configured topic for {DescribePartition(partition)}. Add or correct the topic source for this course/group partition; text is copied without translation.");
    }

    public void ReportRegistryLabRoutingUnresolved(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.RegistryLabRoutingUnresolved,
            course: course,
            @group: group,
            detail: "Unresolved lab routing prevents intake cleanup.");
    }

    public void ReportRegistryLabFallbackPreserved(string? course, string? group)
    {
        Add(
            RegistrySyncDiagnosticKind.RegistryLabFallbackPreserved,
            course: course,
            @group: group,
            detail:
            "This list is needed for scoped fallback labs. Intake cleanup leaves its lesson columns and attendance unchanged.");
    }

    public void ReportUnsafeRegistryLabCleanup(
        string? course,
        string? group,
        int columnCount,
        int markCount,
        Uri destination)
    {
        Add(
            RegistrySyncDiagnosticKind.UnsafeRegistryLabCleanup,
            course: course,
            @group: group,
            detail:
            $"Selected {columnCount} laboratory columns and {markCount} recorded attendance marks for deletion from {destination}. Other lesson types are preserved.");
    }
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
            var courseName = schedule.Get(target.Course).FullName;
            var groupName = schedule.Get(target.Group).Name;
            diagnostics.ReportMissingRegistryCourse(
                course: courseName,
                @group: groupName);
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
        Cover(new RegistryExpectedTarget(
            source.Lesson.Course,
            parent,
            partition));

        bool IsNotSingleGroupLecture()
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

        if (IsNotSingleGroupLecture())
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

            bool IsNotParentLanguageProjection()
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

                bool IsMatchingLanguageRule(RegistryGroupMatchRule rule)
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
                }

                if (!rules.Any(IsMatchingLanguageRule))
                {
                    return true;
                }

                return false;
            }

            if (IsNotParentLanguageProjection())
            {
                continue;
            }

            // Language partitions in a separate parent can attend the exact same lecture.
            // Its existing parent-language destination already synchronizes that event.
            var shared = schedule.EnumerateAllLessons()
                .Any(candidate =>
                {
                    if (!candidate.Lesson.Groups.IsSingleGroup)
                    {
                        return false;
                    }
                    if (candidate.Lesson.Groups[0] != target.Group)
                    {
                        return false;
                    }
                    if (candidate.Lesson.GroupPartitionKey != target.Partition)
                    {
                        return false;
                    }

                    var withSourceAudience = candidate.Lesson.WithGroupsAndSubGroup(
                        source.Lesson.Groups,
                        source.Lesson.SubGroup);
                    if (withSourceAudience != source.Lesson)
                    {
                        return false;
                    }

                    if (SameDate(first: source, second: candidate))
                    {
                        return true;
                    }

                    return false;
                });
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

        if (first.Grade != second.Grade)
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
            var courseName = schedule.Get(course).FullName;
            var groupName = schedule.Get(target.Group).Name;
            diagnostics.ReportMissingRegistryGroup(
                course: courseName,
                @group: groupName,
                partition: target.Partition);
            _remaining.Remove(target);
        }
    }
}
