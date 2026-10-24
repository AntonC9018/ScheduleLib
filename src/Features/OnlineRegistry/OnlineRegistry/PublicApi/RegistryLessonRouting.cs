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
                    || ContainsPartition(destination: filter.GroupPartition, source: ProjectPartition(config, parameters.Schedule, parameters.Schedule.Get(id).Lesson))))
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
            if (ContainsPartition(destination: filter.GroupPartition, source: partition))
                lessons.Add(id);
        }
        return new(lessons.ToList(), filter.GroupPartition);
    }

    internal static GroupPartitionKey ProjectPartition(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
    {
        return ManualMerge(config, schedule, lesson) is not null
                ? lesson.GroupPartitionKey with { SubGroup = SubGroup.All } : lesson.GroupPartitionKey;
    }

    private static bool IsCurrentLab(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
        => ManualMerge(config, schedule, lesson) is not { } merge || lesson.SubGroup == new SubGroup(merge.SourceSubGroup);

    private static RegistryLabMergeRule? ManualMerge(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
    {
        return lesson.Type == LessonType.Lab && lesson.Groups.IsSingleGroup
                ? RegistryLabMerges.Find(config.LabMerges, schedule, lesson.Course, lesson.Groups,
                    lesson.GroupPartitionKey with { SubGroup = SubGroup.All }) : null;
    }

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
        => Prioritize(schedule, destinations, diagnostics, out _);

    internal static Dictionary<Uri, Dictionary<AnyLessonId, LessonGroups>> Prioritize(Schedule schedule,
        IReadOnlyList<(GroupLink Group, RegistryLessonSelection Selection)> destinations,
        RegistrySyncDiagnostics? diagnostics, out bool hasUnresolvedAudience)
    {
        hasUnresolvedAudience = false;
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
                if (lesson.Type != LessonType.Lab)
                {
                    foreach (var destination in eligible)
                    {
                        AddAudience(result, destination, id, parent);
                    }
                    continue;
                }
                // Ordinary and additional-courses (SE) rosters share the same schedule
                // audience and attendance source, but lab specificity competes only
                // within each variant. A normal subgroup and an SE parent therefore
                // both receive the same lab; equal specificity ties within one
                // variant remain ambiguous while ordinary-vs-SE never tie.
                foreach (var variant in eligible.GroupBy(destination => destination.Group.IsAdditionalCourses))
                {
                    var best = variant.Max(destination => Specificity(destination.Group));
                    var winners = variant.Where(destination => Specificity(destination.Group) == best).ToArray();
                    if (winners.Length > 1)
                    {
                        hasUnresolvedAudience = true;
                        diagnostics?.Add(kind: "ambiguous-registry-destination", course: schedule.Get(lesson.Course).FullName,
                            @group: schedule.Get(parent).Name,
                            detail: "Multiple equally specific registry lists cover this lab audience. Correct the assignments; this audience was not routed by list order.");
                        continue;
                    }
                    foreach (var destination in winners)
                    {
                        AddAudience(result, destination, id, parent);
                    }
                }
            }
        }
        return result;

        static void AddAudience(
            Dictionary<Uri, Dictionary<AnyLessonId, LessonGroups>> result,
            (GroupLink Group, RegistryLessonSelection Selection) destination,
            AnyLessonId id,
            GroupId parent)
        {
            if (!result[destination.Group.Uri].TryGetValue(id, out var audience)) audience = new LessonGroups();
            audience.Add(parent);
            result[destination.Group.Uri][id] = audience;
        }
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
