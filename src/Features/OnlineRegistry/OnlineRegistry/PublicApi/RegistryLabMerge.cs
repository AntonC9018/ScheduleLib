namespace ScheduleLib.OnlineRegistry;

/// <summary>Explicit registry-only projection; historical schedule lessons remain unchanged.</summary>
public sealed class RegistryLabMergeRule
{
    public required string Course { get; set; }
    public required string Group { get; set; }
    public string SourceSubGroup { get; set; } = "I";
}

internal readonly record struct RegistryLessonSelection(
    List<AnyLessonId> Lessons,
    GroupPartitionKey AttendancePartition,
    bool IsLabMerge = false);

internal static class RegistryLabMerges
{
    internal static RegistryLabMergeRule? Find(
        IReadOnlyList<RegistryLabMergeRule> rules,
        Schedule schedule,
        CourseId course,
        in LessonGroups groups,
        GroupPartitionKey partition)
    {
        static bool ShouldSkipMerge(
            in LessonGroups candidateGroups,
            CourseId candidateCourse,
            GroupPartitionKey candidatePartition)
        {
            if (!candidateGroups.IsSingleGroup)
            {
                return true;
            }
            if (candidateCourse.IsInvalid)
            {
                return true;
            }

            if (candidatePartition != GroupPartitionKey.All)
            {
                return true;
            }

            return false;
        }

        if (ShouldSkipMerge(
                in groups,
                course,
                partition))
        {
            return null;
        }

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

                    if (merge.Alternative == partition.Alternative)
                    {
                        return true;
                    }

                    return false;
                }

                return CheckCondition();
            }))
        {
            return null;
        }

        RegistryLabMergeRule? found = null;
        foreach (var rule in rules)
        {
            if (!string.Equals(
                    rule.Course,
                    schedule.Get(course).FullName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var groupName = schedule.Get(groups[0]).Name;
            if (!string.Equals(
                    rule.Group,
                    groupName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (found is not null)
            {
                throw new InvalidOperationException("Duplicate registry lab merge rules.");
            }

            if (string.IsNullOrWhiteSpace(rule.SourceSubGroup))
            {
                throw new InvalidOperationException("Registry lab merge requires a surviving subgroup.");
            }

            found = rule;
        }
        return found;
    }

    internal static RegistryLessonSelection Select(
        IReadOnlyList<RegistryLabMergeRule> rules,
        LessonMatchParams parameters)
    {
        var filter = parameters.Filter;

        bool CalculateConditionResult()
        {
            if (filter.Groups.IsWildcard)
            {
                return true;
            }
            return filter.Groups.IsCombined;
        }

        bool conditionResult = CalculateConditionResult();


        var rule = conditionResult
            ? null
            : Find(
                rules,
                parameters.Schedule,
                filter.CourseId,
                filter.Groups.Value,
                filter.GroupPartition);
        if (rule is null)
        {
            var ordinary = MatchLessonHelper.MatchLessonsInSchedule(parameters)
                .ToList();
            return new(
                ordinary,
                ordinary.Count == 0
                    ? filter.GroupPartition
                    : parameters.Schedule.Get(ordinary[0]).Lesson.GroupPartitionKey);
        }
        var subGroup = new SubGroup(rule.SourceSubGroup);
        var specialization = Specialization.All;
        var survivingPartition = new GroupPartitionKey(subGroup, specialization);
        // Exact surviving lab partition; no elective fallback or cross-parent lesson.
        var lessons = parameters.Lookup[filter.CourseId].Where(id =>
            {
                if (!parameters.Schedule.IsLessonActive(id))
                {
                    return false;
                }

                var lesson = parameters.Schedule.Get(id).Lesson;

                bool CalculateConditionResult()
                {
                    if (lesson.Type != LessonType.Lab)
                    {
                        return false;
                    }

                    if (!(lesson.GroupPartitionKey == survivingPartition))
                    {
                        return false;
                    }

                    if (!lesson.Groups.IsSingleGroup)
                    {
                        return false;
                    }

                    if (lesson.Groups[0] == filter.Groups.Value[0])
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                return conditionResult;
            })
            .ToList();
        if (lessons.Count == 0)
        {
            throw new InvalidOperationException("Surviving registry lab merge lessons not found.");
        }
        // A registry course can contain lectures as well as the merged labs.
        lessons.AddRange(MatchLessonHelper.MatchLessonsInSchedule(parameters)
            .Where(id => parameters.Schedule.Get(id).Lesson.Type != LessonType.Lab));
        return new(
            lessons,
            GroupPartitionKey.All,
            IsLabMerge: true);
    }
}
