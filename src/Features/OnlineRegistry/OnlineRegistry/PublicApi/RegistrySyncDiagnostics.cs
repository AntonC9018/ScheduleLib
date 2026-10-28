namespace ScheduleLib.OnlineRegistry;

public sealed record RegistrySyncDiagnostic(
    string Kind,
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

    public void Add(
        string kind,
        string? course,
        string? group,
        string detail)
    {
        var item = new RegistrySyncDiagnostic(Kind: kind,
            Course: course,
            Group: group,
            Detail: detail);
        if (_seen.Add(item))
        {
            _items.Add(item);
            Reported?.Invoke(item);
        }
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
            string CalculateGroup()
            {
                var get = schedule.Get(target.Group);
                string group2 = get.Name;
                return group2;
            }

            diagnostics.Add(kind: "missing-registry-course",
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
        RegistryExpectedTarget target2 = new(source.Lesson.Course,
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

            diagnostics.Add(kind: "missing-registry-group",
                course: schedule.Get(course).FullName,
                @group: CalculateGroup(),
                detail:
                $"No registry destination matched the scheduled partition {partition}. Ask the registry administrator to add or correct its group assignment.");
            _remaining.Remove(target);
        }
    }
}
