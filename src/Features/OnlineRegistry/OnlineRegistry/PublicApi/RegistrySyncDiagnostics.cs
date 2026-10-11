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
        var item = new RegistrySyncDiagnostic(kind, course, group, detail);
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
            diagnostics.Add("missing-registry-course", schedule.Get(target.Course).FullName, schedule.Get(target.Group).Name,
                "Ask the registry administrator to assign this scheduled course to the teacher.");
            _remaining.Remove(target);
        }
    }

    public void Cover(RegistryExpectedTarget target) => _remaining.Remove(target);

    public void ReportMissingGroups(CourseId course)
    {
        foreach (var target in _remaining.Where(target => target.Course == course).ToArray())
        {
            var partition = RegistrySyncDiagnostics.DescribePartition(target.Partition);
            diagnostics.Add("missing-registry-group", schedule.Get(course).FullName, schedule.Get(target.Group).Name,
                $"No registry destination matched the scheduled partition {partition}. Ask the registry administrator to add or correct its group assignment.");
            _remaining.Remove(target);
        }
    }
}
