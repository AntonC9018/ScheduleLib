namespace ScheduleLib.OnlineRegistry;

/// <summary>Chooses lab destinations before reconciling any list for a course.</summary>
internal static class RegistryLessonRouting
{
    internal static RegistryLessonSelection Candidates(BuiltRegistryConfig config, LessonMatchParams parameters)
    {
        var merged = RegistryLabMerges.Select(config.LabMerges, parameters);
        if (merged.IsLabMerge)
        {
            return merged;
        }

        var filter = parameters.Filter;
        var lessons = MatchLessonHelper.MatchLessonCandidates(parameters)
            .Where(id => { bool CalculateConditionResult() { if (IsCurrentLab(config, parameters.Schedule, parameters.Schedule.Get(id).Lesson)) { if (parameters.Schedule.Get(id).Lesson.Type != LessonType.Lab) { return true; } else { if (ContainsPartition(destination: filter.GroupPartition, source: ProjectPartition(config, parameters.Schedule, parameters.Schedule.Get(id).Lesson))) { return true; } return false; } } else { return false; } } bool conditionResult = CalculateConditionResult(); if (conditionResult) { return true; } return false; })
            .ToHashSet();
        foreach (var id in parameters.Lookup[filter.CourseId])
        {
            if (!parameters.Schedule.IsLessonActive(id))
            {
                continue;
            }

            var lesson = parameters.Schedule.Get(id).Lesson;
            if (lesson.Type != LessonType.Lab)
            {
                continue;
            }

            if (!IsCurrentLab(config, parameters.Schedule, lesson))
            {
                continue;
            }

            if (lesson.Groups.IsEmpty || !lesson.Groups.Any(filter.Groups.Value.Contains))
            {
                continue;
            }

            if (filter.Groups.IsCombined && !lesson.Groups.IsSubSetOf(filter.Groups.Value))
            {
                continue;
            }

            bool CheckCondition()
            {
                if (!(filter.Groups.IsWildcard))
                {
                    return false;
                }

                if (!(!lesson.Groups.IsSubSetOf(filter.Groups.Value)))
                {
                    return false;
                }

                if (!(!filter.Groups.Value.IsSubSetOf(lesson.Groups)))
                {
                    return false;
                }

                return true;
            }

            if (CheckCondition())
            {
                continue;
            }
            // A parent list can contain sessions of its subgroups; the topic identifies the audience.
            var partition = ProjectPartition(config, parameters.Schedule, lesson);
            if (ContainsPartition(destination: filter.GroupPartition, source: partition))
            {
                lessons.Add(id);
            }
        }
        return new(lessons.ToList(), filter.GroupPartition);
    }

    internal static GroupPartitionKey ProjectPartition(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
    {
        return ManualMerge(config, schedule, lesson) is not null
                ? lesson.GroupPartitionKey with
                {
                    SubGroup = SubGroup.All,
                } : lesson.GroupPartitionKey;
    }

    private static bool IsCurrentLab(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
        => ManualMerge(config, schedule, lesson) is not { } merge || lesson.SubGroup == new SubGroup(merge.SourceSubGroup);

    private static RegistryLabMergeRule? ManualMerge(BuiltRegistryConfig config, Schedule schedule, LessonData lesson)
    {
        bool CalculateConditionResult()
        {
            if (lesson.Type == LessonType.Lab)
            {
                return lesson.Groups.IsSingleGroup;
            }
            else
            {
                return false;
            }
        }

        bool conditionResult = CalculateConditionResult();


        return conditionResult
                ? RegistryLabMerges.Find(config.LabMerges, schedule, lesson.Course, lesson.Groups,
                    lesson.GroupPartitionKey with
                    {
                        SubGroup = SubGroup.All,
                    }) : null;
    }

    private static bool ContainsPartition(GroupPartitionKey destination, GroupPartitionKey source)
    {
        bool CalculateConditionResult()
        {
            if (destination.SubGroup == SubGroup.All)
            {
                return true;
            }
            else
            {
                if (destination.SubGroup == source.SubGroup)
                {
                    return true;
                }

                return false;
            }
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            if (destination.Specialization == Specialization.All)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = destination.Specialization == source.Specialization;
            }

            if (conditionResult)
            {
                if (destination.Alternative == Alternative.All)
                {
                    conditionResult = true;
                }
                else
                {
                    conditionResult = destination.Alternative == source.Alternative;
                }
            }
            else
            {
                conditionResult = false;
            }
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            return true;
        }
        // Historical elective schedules use an Optional marker instead of the registry's named elective.
        bool CalculateConditionResult2()
        {
            if (destination.SubGroup == SubGroup.All)
            {
                if (destination.Specialization != Specialization.All)
                {
                    if (destination.Alternative == Alternative.All)
                    {
                        if (source == new GroupPartitionKey(SpecialSubGroups.Optional, Specialization.All))
                        {
                            return true;
                        }

                        return false;
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        bool conditionResult2 = CalculateConditionResult2();


        if (conditionResult2)
        {
            return true;
        }

        return false;
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
        var result = destinations.ToDictionary(keySelector: destination => destination.Group.Uri, elementSelector: _ => new Dictionary<AnyLessonId, LessonGroups>());
        foreach (var id in destinations.SelectMany(destination => destination.Selection.Lessons)
            .Distinct())
        {
            var lesson = schedule.Get(id).Lesson;
            foreach (var parent in lesson.Groups)
            {
                var eligible = destinations.Where(destination => destination.Selection.Lessons.Contains(id)
                    && destination.Group.Groups.Value.Contains(parent))
                    .ToArray();
                if (eligible.Length == 0)
                {
                    continue;
                }

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
                    var winners = variant.Where(destination => Specificity(destination.Group) == best)
                        .ToArray();
                    if (winners.Length > 1)
                    {
                        hasUnresolvedAudience = true;
                        string CalculateGroup()
                        {
                            global::ScheduleLib.Group get = schedule.Get(parent);
                            string group2 = get.Name;
                            return group2;
                        }

                        diagnostics?.Add(kind: "ambiguous-registry-destination", course: schedule.Get(lesson.Course).FullName,
                                                    @group: CalculateGroup(),
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
            if (!result[destination.Group.Uri].TryGetValue(id, out var audience))
            {
                audience = new LessonGroups();
            }

            audience.Add(parent);
            result[destination.Group.Uri][id] = audience;
        }
    }

    private static int Specificity(GroupLink group)
    {
        bool CalculateConditionResult()
        {
            if (!group.Groups.IsWildcard)
            {
                if (!group.Groups.IsCombined)
                {
                    return group.Groups.Value.IsSingleGroup;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        bool conditionResult = CalculateConditionResult();


        var explicitParent = conditionResult;
        var partition = group.GroupPartition;
        int int32;
        if (explicitParent)
        {
            int32 = 1000;
        }
        else
        {
            int32 = 0;
        }

        int int322 = (int32);
        int int323;
        if (partition.SubGroup != SubGroup.All)
        {
            int323 = 100;
        }
        else
        {
            int323 = 0;
        }

        int int324 = int322 + (int323);
        int int325;
        if (partition.Specialization != Specialization.All)
        {
            int325 = 10;
        }
        else
        {
            int325 = 0;
        }

        int int326 = int324 + (int325);
        int int327;
        if (partition.Alternative != Alternative.All)
        {
            int327 = 10;
        }
        else
        {
            int327 = 0;
        }

        int int328 = int326 + (int327);
        int int329 = int328 - group.Groups.Value.Count;
        return int329;
    }
}
