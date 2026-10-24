namespace ScheduleLib.OnlineRegistry;

/// <summary>Explicit registry-only projection; historical schedule lessons remain unchanged.</summary>
public sealed class RegistryLabMergeRule
{
    public required string Course { get; set; }
    public required string Group { get; set; }
    public string SourceSubGroup { get; set; } = "I";
}

internal readonly record struct RegistryLessonSelection(
    List<AnyLessonId> Lessons, GroupPartitionKey AttendancePartition, bool IsLabMerge = false);

internal static class RegistryLabMerges
{
    internal static RegistryLabMergeRule? Find(IReadOnlyList<RegistryLabMergeRule> rules,
        Schedule schedule, CourseId course, in LessonGroups groups, GroupPartitionKey partition)
    {
        if (!groups.IsSingleGroup || course.IsInvalid || partition != GroupPartitionKey.All) return null;
        var group = groups[0];
        if (schedule.AutomaticLabSubGroupMerges.Any(merge =>
        {
            bool CheckCondition()
            {
                if (!(merge.Course == course))
                {
                    return false;
                }

                if (!(merge.Group == group))
                {
                    return false;
                }

                if (!(merge.Specialization == partition.Specialization))
                {
                    return false;
                }

                if (!(merge.Alternative == partition.Alternative))
                {
                    return false;
                }

                return true;
            }

            return CheckCondition();
        })) return null;
        RegistryLabMergeRule? found = null;
        foreach (var rule in rules)
        {
            bool conditionResult;
            if (!string.Equals(rule.Course, schedule.Get(course).FullName, StringComparison.OrdinalIgnoreCase))
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = string.Equals(rule.Group, schedule.Get(groups[0]).Name, StringComparison.OrdinalIgnoreCase);
                conditionResult = !(conditionResult);
            }

            if (conditionResult)
                continue;
            if (found is not null)
                throw new InvalidOperationException("Duplicate registry lab merge rules.");
            if (string.IsNullOrWhiteSpace(rule.SourceSubGroup))
                throw new InvalidOperationException("Registry lab merge requires a surviving subgroup.");
            found = rule;
        }
        return found;
    }

    internal static RegistryLessonSelection Select(IReadOnlyList<RegistryLabMergeRule> rules,
        LessonMatchParams parameters)
    {
        var filter = parameters.Filter;
        bool conditionResult;
        if (filter.Groups.IsWildcard)
        {
            conditionResult = true;
        }
        else
        {
            conditionResult = filter.Groups.IsCombined;
        }

        var rule = conditionResult ? null
            : Find(rules, parameters.Schedule, filter.CourseId, filter.Groups.Value, filter.GroupPartition);
        if (rule is null)
        {
            var ordinary = MatchLessonHelper.MatchLessonsInSchedule(parameters).ToList();
            return new(ordinary, ordinary.Count == 0 ? filter.GroupPartition
                : parameters.Schedule.Get(ordinary[0]).Lesson.GroupPartitionKey);
        }
        var survivingPartition = new GroupPartitionKey(new SubGroup(rule.SourceSubGroup), Specialization.All);
        // Exact surviving lab partition; no elective fallback or cross-parent lesson.
        var lessons = parameters.Lookup[filter.CourseId].Where(id =>
        {
            if (!parameters.Schedule.IsLessonActive(id))
                return false;
            var lesson = parameters.Schedule.Get(id).Lesson;
            if (lesson.Type == LessonType.Lab && lesson.GroupPartitionKey == survivingPartition
               && lesson.Groups.IsSingleGroup && lesson.Groups[0] == filter.Groups.Value[0])
            {
                return true;
            }

            return false;
        }).ToList();
        if (lessons.Count == 0)
            throw new InvalidOperationException("Surviving registry lab merge lessons not found.");
        // A registry course can contain lectures as well as the merged labs.
        lessons.AddRange(MatchLessonHelper.MatchLessonsInSchedule(parameters)
            .Where(id => parameters.Schedule.Get(id).Lesson.Type != LessonType.Lab));
        return new(lessons, GroupPartitionKey.All, IsLabMerge: true);
    }
}
