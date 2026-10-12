namespace ScheduleLib.OnlineRegistry;

/// <summary>Chooses lab destinations before reconciling any list for a course.</summary>
internal static class RegistryLessonRouting
{
    internal static RegistryLessonSelection Candidates(BuiltRegistryConfig config, LessonMatchParams parameters)
    {
        var merged = RegistryLabMerges.Select(config.LabMerges, parameters);
        if (merged.IsLabMerge) return merged;
        var filter = parameters.Filter;
        var lessons = MatchLessonHelper.MatchLessonCandidates(parameters)
            .Where(id => IsCurrentLab(config, parameters.Schedule, parameters.Schedule.Get(id).Lesson)
                && (parameters.Schedule.Get(id).Lesson.Type != LessonType.Lab
                    || ContainsPartition(filter.GroupPartition, ProjectPartition(config, parameters.Schedule, parameters.Schedule.Get(id).Lesson))))
            .ToHashSet();
        foreach (var id in parameters.Lookup[filter.CourseId])
        {
            if (!parameters.Schedule.IsLessonActive(id)) continue;
            var lesson = parameters.Schedule.Get(id).Lesson;
            if (lesson.Type != LessonType.Lab) continue;
            if (!IsCurrentLab(config, parameters.Schedule, lesson)) continue;
            if (lesson.Groups.IsEmpty || !lesson.Groups.Any(filter.Groups.Value.Contains)) continue;
            if (filter.Groups.IsCombined && !lesson.Groups.IsSubSetOf(filter.Groups.Value)) continue;
            if (filter.Groups.IsWildcard && !lesson.Groups.IsSubSetOf(filter.Groups.Value)
                && !filter.Groups.Value.IsSubSetOf(lesson.Groups)) continue;
            // A parent list can contain sessions of its subgroups; the topic identifies the audience.
            var partition = ProjectPartition(config, parameters.Schedule, lesson);
            if (ContainsPartition(filter.GroupPartition, partition))
                lessons.Add(id);
        }
        return new(lessons.ToList(), filter.GroupPartition);
    }

    internal static GroupPartitionKey ProjectPartition(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
        => ManualMerge(config, schedule, lesson) is not null
            ? lesson.GroupPartitionKey with { SubGroup = SubGroup.All } : lesson.GroupPartitionKey;

    private static bool IsCurrentLab(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
        => ManualMerge(config, schedule, lesson) is not { } merge || lesson.SubGroup == new SubGroup(merge.SourceSubGroup);

    private static RegistryLabMergeRule? ManualMerge(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
        => lesson.Type == LessonType.Lab && lesson.Groups.IsSingleGroup
            ? RegistryLabMerges.Find(config.LabMerges, schedule, lesson.Course, lesson.Groups,
                lesson.GroupPartitionKey with { SubGroup = SubGroup.All }) : null;

    private static bool ContainsPartition(GroupPartitionKey destination, GroupPartitionKey source)
    {
        if ((destination.SubGroup == SubGroup.All || destination.SubGroup == source.SubGroup)
            && (destination.Specialization == Specialization.All || destination.Specialization == source.Specialization)
            && (destination.Alternative == Alternative.All || destination.Alternative == source.Alternative)) return true;
        // Historical elective schedules use an Optional marker instead of the registry's named elective.
        return destination.SubGroup == SubGroup.All && destination.Specialization != Specialization.All
            && destination.Alternative == Alternative.All && source == new GroupPartitionKey(SpecialSubGroups.Optional, Specialization.All);
    }

    internal static Dictionary<Uri, Dictionary<AnyLessonId, LessonGroups>> Prioritize(Schedule schedule,
        IReadOnlyList<(GroupLink Group, RegistryLessonSelection Selection)> destinations,
        RegistrySyncDiagnostics? diagnostics)
    {
        var result = destinations.ToDictionary(destination => destination.Group.Uri,
            _ => new Dictionary<AnyLessonId, LessonGroups>());
        foreach (var id in destinations.SelectMany(destination => destination.Selection.Lessons).Distinct())
        {
            var lesson = schedule.Get(id).Lesson;
            foreach (var parent in lesson.Groups)
            {
                var eligible = destinations.Where(destination => destination.Selection.Lessons.Contains(id)
                    && destination.Group.Groups.Value.Contains(parent)).ToArray();
                if (eligible.Length == 0) continue;
                if (lesson.Type == LessonType.Lab)
                {
                    var best = eligible.Max(destination => Specificity(destination.Group));
                    eligible = eligible.Where(destination => Specificity(destination.Group) == best).ToArray();
                    if (eligible.Length > 1)
                    {
                        diagnostics?.Add("ambiguous-registry-destination", schedule.Get(lesson.Course).FullName,
                            schedule.Get(parent).Name,
                            "Multiple equally specific registry lists cover this lab audience. Correct the assignments; this audience was not routed by list order.");
                        continue;
                    }
                }
                foreach (var destination in eligible)
                {
                    if (!result[destination.Group.Uri].TryGetValue(id, out var audience)) audience = new LessonGroups();
                    audience.Add(parent);
                    result[destination.Group.Uri][id] = audience;
                }
            }
        }
        return result;
    }

    private static int Specificity(GroupLink group)
    {
        var explicitParent = !group.Groups.IsWildcard && !group.Groups.IsCombined && group.Groups.Value.IsSingleGroup;
        var partition = group.GroupPartition;
        return (explicitParent ? 1000 : 0)
            + (partition.SubGroup != SubGroup.All ? 100 : 0)
            + (partition.Specialization != Specialization.All ? 10 : 0)
            + (partition.Alternative != Alternative.All ? 10 : 0)
            - group.Groups.Value.Count;
    }
}
