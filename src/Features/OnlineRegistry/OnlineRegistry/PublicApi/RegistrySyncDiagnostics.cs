namespace ScheduleLib.OnlineRegistry;

public sealed record RegistrySyncDiagnostic(string Kind, string? Course, string? Group, string Detail);

/// <summary>Actionable reconciliation gaps, without credentials or student records.</summary>
public sealed class RegistrySyncDiagnostics
{
    private readonly List<RegistrySyncDiagnostic> _items = [];
    private readonly HashSet<RegistrySyncDiagnostic> _seen = [];
    public IReadOnlyList<RegistrySyncDiagnostic> Items => _items;
    public event Action<RegistrySyncDiagnostic>? Reported;

    public static string DescribePartition(GroupPartitionKey partition)
    {
        if (partition == GroupPartitionKey.All) return "whole group";
        var parts = new List<string>();
        if (partition.SubGroup != SubGroup.All) parts.Add($"subgroup {partition.SubGroup.Value}");
        if (partition.Specialization != Specialization.All) parts.Add($"specialization {partition.Specialization.Value}");
        if (partition.Alternative != Alternative.All) parts.Add($"alternative {partition.Alternative.Value}");
        return string.Join(", ", parts);
    }

    public void Add(string kind, string? course, string? group, string detail)
    {
        var item = new RegistrySyncDiagnostic(Kind: kind, Course: course, Group: group, Detail: detail);
        if (_seen.Add(item))
        {
            _items.Add(item);
            Reported?.Invoke(item);
        }
    }
}

internal readonly record struct RegistryExpectedTarget(CourseId Course, GroupId Group, GroupPartitionKey Partition);

internal sealed class RegistryTargetCoverage(Schedule schedule, IEnumerable<RegistryExpectedTarget> targets,
    RegistrySyncDiagnostics diagnostics)
{
    private readonly HashSet<RegistryExpectedTarget> _remaining = targets.ToHashSet();

    public void ReportMissingCourses(IReadOnlySet<CourseId> courses)
    {
        foreach (var target in _remaining.Where(target => !courses.Contains(target.Course)).ToArray())
        {
            diagnostics.Add(kind: "missing-registry-course", course: schedule.Get(target.Course).FullName, @group: schedule.Get(target.Group).Name,
                detail: "Ask the registry administrator to assign this scheduled course to the teacher.");
            _remaining.Remove(target);
        }
    }

    public void Cover(RegistryExpectedTarget target) => _remaining.Remove(target);

    public void CoverLesson(AnyLessonId lessonId, GroupId parent, GroupPartitionKey partition,
        IReadOnlyList<RegistryGroupMatchRule> rules)
    {
        var source = schedule.Get(lessonId);
        Cover(new(source.Lesson.Course, parent, partition));
        if (source.Lesson.Type is not (LessonType.Curs or LessonType.Prelegere)
            || partition != GroupPartitionKey.All || !source.Lesson.Groups.IsSingleGroup)
            return;

        var host = schedule.Get(parent);
        if (host.Language is not (Language.Ro or Language.Ru)) return;
        var intake = schedule.EnumerateGroups().Where(group => SameIntake(first: host, second: group.Item)).ToArray();
        if (intake.Any(group => group.Item.Language == Language.None)
            || intake.GroupBy(group => group.Item.Language).Any(groups => groups.Count() > 1)) return;

        foreach (var target in _remaining.Where(target => target.Course == source.Lesson.Course).ToArray())
        {
            var audience = schedule.Get(target.Group);
            if (!SameIntake(first: host, second: audience)
                || target.Partition != new GroupPartitionKey(SpecialSubGroups.FromLanguage(host.Language), Specialization.All)
                || !rules.Any(rule => rule.MatchByLanguage && rule.AllowParentGroup
                    && rule.Faculty.Equals(audience.Faculty.Name, StringComparison.OrdinalIgnoreCase)
                    && rule.Language == audience.Language && rule.Language != host.Language
                    && rule.SubGroup == SpecialSubGroups.FromLanguage(audience.Language).Value)) continue;

            // Language partitions in a separate parent can attend the exact same lecture.
            // Its existing parent-language destination already synchronizes that event.
            var shared = schedule.EnumerateAllLessons().Any(candidate =>
                candidate.Lesson.Groups.IsSingleGroup && candidate.Lesson.Groups[0] == target.Group
                && candidate.Lesson.GroupPartitionKey == target.Partition
                && (candidate.Lesson with { Groups = source.Lesson.Groups, SubGroup = source.Lesson.SubGroup }) == source.Lesson
                && SameDate(first: source, second: candidate));
            if (shared) Cover(target);
        }
    }

    private static bool SameIntake(Group first, Group second)
    {
        return first.Faculty.Name.Equals(second.Faculty.Name, StringComparison.OrdinalIgnoreCase)
        && first.Grade == second.Grade && first.AttendanceMode == second.AttendanceMode
        && first.QualificationType == second.QualificationType;
    }

    private static bool SameDate(AnyLessonAccessor first, AnyLessonAccessor second)
    {
        if (first.Weekly is { } weekly && second.Weekly is { } otherWeekly)
            return weekly.Date == otherWeekly.Date;
        if (first.OneTime is { } oneTime && second.OneTime is { } otherOneTime)
            return oneTime.Date == otherOneTime.Date;
        return false;
    }

    public void ReportMissingGroups(CourseId course)
    {
        foreach (var target in _remaining.Where(target => target.Course == course).ToArray())
        {
            var partition = RegistrySyncDiagnostics.DescribePartition(target.Partition);
            diagnostics.Add(kind: "missing-registry-group", course: schedule.Get(course).FullName, @group: schedule.Get(target.Group).Name,
                detail: $"No registry destination matched the scheduled partition {partition}. Ask the registry administrator to add or correct its group assignment.");
            _remaining.Remove(target);
        }
    }
}
