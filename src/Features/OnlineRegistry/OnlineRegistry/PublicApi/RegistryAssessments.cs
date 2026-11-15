using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace ScheduleLib.OnlineRegistry;

public sealed class RegistryAssessmentRule
{
    public required string Course { get; set; }
    public required string Group { get; set; }
    public required LessonType LessonType { get; set; }
    public string? SubGroup { get; set; }
    public string? Specialization { get; set; }
    public string? Alternative { get; set; }
    public List<DateOnly> Dates { get; set; } = [];
}

public static class RegistryAssessmentTopics
{
    public static bool IsAssessment([NotNullWhen(true)] string? topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return false;
        }

        var text = topic.Normalize(NormalizationForm.FormD);
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetter(c))
            {
                word.Append(char.ToLowerInvariant(c));
            }
            else
            {
                if (IsAssessmentWord(word.ToString()))
                {
                    return true;
                }

                word.Clear();
            }
        }

        if (IsAssessmentWord(word.ToString()))
        {
            return true;
        }

        return false;
    }

    private static bool IsAssessmentWord(string word) =>
        word is "atestare" or "atestarea" or "atestari" or "atestarile";
}

internal static class RegistryAssessments
{
    /// <summary>Local lessons and remote instances after assessment topics were arranged.</summary>
    internal readonly record struct ArrangedAssessments(LessonInstance[] Local, RemoteLessonInstance[] Remote);

    internal static IReadOnlyList<RegistryAssessmentRule> Find(
        IReadOnlyList<RegistryAssessmentRule> rules,
        Schedule schedule,
        CourseId course,
        in LessonGroups groups,
        GroupPartitionKey partition)
    {
        if (groups.IsEmpty || !groups.IsSingleGroup)
        {
            return [];
        }

        var groupName = schedule.Get(groups[0]).Name;
        return rules.Where(rule =>
            {
                var courseName = schedule.Get(course).FullName;
                if (!string.Equals(
                        rule.Course,
                        courseName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (!string.Equals(
                        rule.Group,
                        groupName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var ruleSubGroup = new SubGroup(rule.SubGroup);
                var ruleSpecialization = new Specialization(rule.Specialization);
                var ruleAlternative = new Alternative(rule.Alternative);
                var rulePartition = new GroupPartitionKey(
                    ruleSubGroup,
                    ruleSpecialization,
                    ruleAlternative);
                if (partition == rulePartition)
                {
                    return true;
                }

                return false;
            })
            .ToArray();
    }

    // Attendance stays tied to the timetable occurrence. Only regular topics use the compacted index.
    internal static ArrangedAssessments Arrange(
        LessonInstance[] local,
        RemoteLessonInstance[] remote,
        IReadOnlyList<RegistryAssessmentRule> rules,
        Func<LessonInstance, int, string?> getTopic,
        Func<LessonInstance, LessonGroups>? getTopicGroups = null)
    {
        RemoteLessonInstance NormalizeAssessmentType(RemoteLessonInstance assessment)
        {
            if (assessment.LessonType != LessonType.Unspecified
                || !RegistryAssessmentTopics.IsAssessment(assessment.Topic))
            {
                return assessment;
            }

            var types = local.Where(lesson => lesson.DateTime == assessment.DateTime)
                .Select(lesson => lesson.RegistryLessonType)
                .Distinct()
                .ToArray();
            if (types.Length == 0)
            {
                types = local.Where(lesson => lesson.DateTime.Date == assessment.DateTime.Date)
                    .Select(lesson => lesson.RegistryLessonType)
                    .Distinct()
                    .ToArray();
            }

            if (types.Length == 0)
            {
                if (rules.Count != 0)
                {
                    throw new UnscheduledAssessmentWithoutLessonTypeException(assessment.DateTime);
                }

                return assessment;
            }
            if (types.Length != 1)
            {
                throw new AmbiguousAssessmentLessonTypeException(assessment.DateTime);
            }

            return assessment with
            {
                LessonType = types[0],
            };
        }

        remote = remote.Select(NormalizeAssessmentType)
            .ToArray();
        var reserved = new Dictionary<int, string>();
        var protectedRemote = new HashSet<int>();
        var configuredTypes = new HashSet<LessonType>();
        foreach (var rule in rules)
        {
            if (rule.LessonType == LessonType.Unspecified || !configuredTypes.Add(rule.LessonType))
            {
                throw new InvalidAssessmentRuleLessonTypeException(rule.LessonType);
            }

            if (rule.Dates.Count
                != rule.Dates.Distinct()
                    .Count())
            {
                throw new DuplicateAssessmentDatesException(rule.LessonType);
            }

            var existing = remote.Where(x =>
                    x.LessonType == rule.LessonType && RegistryAssessmentTopics.IsAssessment(x.Topic))
                .OrderBy(x => x.DateTime)
                .ToArray();
            var dates = rule.Dates.Order()
                .ToArray();
            for (int i = 0; i < dates.Length; i++)
            {
                var candidates = Enumerable.Range(start: 0, count: local.Length)
                    .Where(index => local[index].RegistryLessonType == rule.LessonType
                        && DateOnly.FromDateTime(local[index].DateTime) == dates[i])
                    .ToArray();
                if (candidates.Length != 1)
                {
                    throw new AmbiguousAssessmentDateLessonException(
                        rule.LessonType,
                        dates[i],
                        candidates.Length);
                }

                string topic = i < existing.Length
                    ? existing[i].Topic!
                    : "Atestare " + (i + 1);
                reserved.Add(candidates[0], topic);
            }
            // Unconfigured additional assessments retain their original dates.
            foreach (var extra in existing.Skip(dates.Length))
            {
                if (!ReserveExisting(extra))
                {
                    protectedRemote.Add(Array.IndexOf(remote, extra));
                }
            }
            // Moving an assessment outside the known timetable could leave an orphaned entry.
            foreach (var moved in existing.Take(dates.Length))
            {
                if (!local.Any(x => x.DateTime.Date == moved.DateTime.Date && x.RegistryLessonType == moved.LessonType))
                {
                    throw new MovedAssessmentOriginalLessonMissingException(
                        moved.DateTime,
                        moved.LessonType);
                }
            }
        }
        for (int i = 0; i < remote.Length; i++)
        {
            var assessment = remote[i];
            if (!RegistryAssessmentTopics.IsAssessment(assessment.Topic))
            {
                continue;
            }

            if (configuredTypes.Contains(assessment.LessonType))
            {
                continue;
            }

            if (!ReserveExisting(assessment))
            {
                protectedRemote.Add(i);
            }
        }
        var indexes = new Dictionary<(LessonType Type, LessonGroups Groups), int>();
        var registryIndexes = new Dictionary<LessonType, int>();
        var useRegistryTopics = new HashSet<LessonType>();
        var registryTopics = remote.Where(lesson =>
            {
                if (string.IsNullOrWhiteSpace(lesson.Topic))
                {
                    return false;
                }

                if (!RegistryAssessmentTopics.IsAssessment(lesson.Topic))
                {
                    return true;
                }

                return false;
            })
            .GroupBy(lesson => lesson.LessonType)
            .ToDictionary(keySelector: group => group.Key,
                elementSelector: group =>
                {
                    var orderedTopics = group.OrderBy(lesson => lesson.DateTime)
                        .Select(lesson => lesson.Topic!)
                        .ToArray();
                    return orderedTopics;
                });
        for (int i = 0; i < local.Length; i++)
        {
            var lesson = local[i];
            if (reserved.TryGetValue(i, out var topic))
            {
                local[i] = lesson with
                {
                    Topic = topic,
                };
            }
            else
            {
                var topicGroups = getTopicGroups is null
                    ? new LessonGroups()
                    : getTopicGroups(lesson)
                        .Ordered();
                var topicKey = (Type: lesson.RegistryLessonType, Groups: topicGroups);
                var index = indexes.GetValueOrDefault(topicKey);
                var registryIndex = registryIndexes.GetValueOrDefault(lesson.RegistryLessonType);
                var regularTopic = getTopic(lesson, index);

                bool ShouldEnableRegistryTopics()
                {
                    if (registryIndex != 0)
                    {
                        return false;
                    }
                    if (regularTopic is not null)
                    {
                        return false;
                    }
                    if (configuredTypes.Contains(lesson.RegistryLessonType))
                    {
                        return true;
                    }
                    return false;
                }

                if (ShouldEnableRegistryTopics())
                {
                    useRegistryTopics.Add(lesson.RegistryLessonType);
                }

                if (useRegistryTopics.Contains(lesson.RegistryLessonType))
                {
                    regularTopic = registryTopics.GetValueOrDefault(lesson.RegistryLessonType)
                            ?.ElementAtOrDefault(registryIndex)
                        ?? "";
                }

                bool HasExistingAssessmentWithoutRegularTopic()
                {
                    if (regularTopic is not null)
                    {
                        return false;
                    }

                    if (!configuredTypes.Contains(lesson.RegistryLessonType))
                    {
                        return false;
                    }

                    if (remote.Any(existing =>
                        {
                            if (existing.DateTime.Date != lesson.DateTime.Date)
                            {
                                return false;
                            }
                            if (existing.LessonType != lesson.RegistryLessonType)
                            {
                                return false;
                            }
                            if (RegistryAssessmentTopics.IsAssessment(existing.Topic))
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

                if (HasExistingAssessmentWithoutRegularTopic())
                {
                    throw new MovedAssessmentRegularTopicMissingException(
                        lesson.DateTime,
                        lesson.RegistryLessonType);
                }

                local[i] = lesson with
                {
                    Topic = regularTopic,
                };
                indexes[topicKey] = index + 1;
                registryIndexes[lesson.RegistryLessonType] = registryIndex + 1;
            }
        }
        return new(
            local,
            remote.Where((_, index) => !protectedRemote.Contains(index))
                .ToArray());

        bool ReserveExisting(RemoteLessonInstance assessment)
        {
            var candidates = Enumerable.Range(start: 0, count: local.Length)
                .Where(index =>
                {
                    if (local[index].DateTime != assessment.DateTime)
                    {
                        return false;
                    }

                    if (assessment.LessonType == LessonType.Unspecified)
                    {
                        return true;
                    }

                    if (local[index].RegistryLessonType == assessment.LessonType)
                    {
                        return true;
                    }

                    return false;
                })
                .ToArray();
            if (candidates.Length == 0)
            {
                candidates = Enumerable.Range(start: 0, count: local.Length)
                    .Where(index =>
                    {
                        if (local[index].DateTime.Date != assessment.DateTime.Date)
                        {
                            return false;
                        }

                        if (assessment.LessonType == LessonType.Unspecified)
                        {
                            return true;
                        }

                        if (local[index].RegistryLessonType == assessment.LessonType)
                        {
                            return true;
                        }

                        return false;
                    })
                    .ToArray();
            }

            if (candidates.Length == 0)
            {
                return false;
            }

            if (candidates.Length != 1 || reserved.ContainsKey(candidates[0]))
            {
                throw new MultipleAssessmentsForSameLessonException(
                    assessment.DateTime,
                    assessment.LessonType);
            }

            reserved.Add(candidates[0], assessment.Topic!);
            return true;
        }
    }
}
