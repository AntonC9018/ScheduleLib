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
            return false;
        var text = topic.Normalize(NormalizationForm.FormD);
        var word = new StringBuilder();
        foreach (var c in text)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetter(c))
                word.Append(char.ToLowerInvariant(c));
            else
            {
                if (IsAssessmentWord(word.ToString()))
                    return true;
                word.Clear();
            }
        }

        if (IsAssessmentWord(word.ToString()))
        {
            return true;
        }

        return false;
    }

    private static bool IsAssessmentWord(string word) => word is "atestare" or "atestarea" or "atestari" or "atestarile";
}

internal static class RegistryAssessments
{
    internal static IReadOnlyList<RegistryAssessmentRule> Find(IReadOnlyList<RegistryAssessmentRule> rules,
        Schedule schedule, CourseId course, in LessonGroups groups, GroupPartitionKey partition)
    {
        bool conditionResult;
        if (groups.IsEmpty)
        {
            conditionResult = true;
        }
        else
        {
            conditionResult = groups.IsSingleGroup;
            conditionResult = !(conditionResult);
        }

        if (conditionResult)
            return [];
        var groupName = schedule.Get(groups[0]).Name;
        return rules.Where(rule =>
        {
            bool CheckCondition()
            {
                if (!(string.Equals(rule.Course, schedule.Get(course).FullName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (!(string.Equals(rule.Group, groupName, StringComparison.OrdinalIgnoreCase)))
                {
                    return false;
                }

                if (!(partition == new GroupPartitionKey(new SubGroup(rule.SubGroup),
                                new Specialization(rule.Specialization), new Alternative(rule.Alternative))))
                {
                    return false;
                }

                return true;
            }

            return CheckCondition();
        }).ToArray();
    }

    // Attendance stays tied to the timetable occurrence. Only regular topics use the compacted index.
    internal static (LessonInstance[] Local, RemoteLessonInstance[] Remote) Arrange(
        LessonInstance[] local, RemoteLessonInstance[] remote, IReadOnlyList<RegistryAssessmentRule> rules,
        Func<LessonInstance, int, string?> getTopic,
        Func<LessonInstance, LessonGroups>? getTopicGroups = null)
    {
        global::ScheduleLib.OnlineRegistry.RemoteLessonInstance ProjectValue(global::ScheduleLib.OnlineRegistry.RemoteLessonInstance assessment)
        {
            if (assessment.LessonType != LessonType.Unspecified || !RegistryAssessmentTopics.IsAssessment(assessment.Topic))
                return assessment;
            var types = local.Where(lesson => lesson.DateTime == assessment.DateTime)
                .Select(lesson => lesson.RegistryLessonType).Distinct().ToArray();
            if (types.Length == 0)
                types = local.Where(lesson => lesson.DateTime.Date == assessment.DateTime.Date)
                    .Select(lesson => lesson.RegistryLessonType).Distinct().ToArray();
            if (types.Length == 0)
            {
                if (rules.Count != 0)
                    throw new InvalidOperationException("An assessment outside the timetable with no lesson type cannot be assigned a configured date.");
                return assessment;
            }
            if (types.Length != 1)
                throw new InvalidOperationException("An assessment without a lesson type requires an unambiguous scheduled lesson type.");
            return assessment with { LessonType = types[0] };
        }
        remote = remote.Select(ProjectValue).ToArray();
        var reserved = new Dictionary<int, string>();
        var protectedRemote = new HashSet<int>();
        var configuredTypes = new HashSet<LessonType>();
        foreach (var rule in rules)
        {
            if (rule.LessonType == LessonType.Unspecified || !configuredTypes.Add(rule.LessonType))
                throw new InvalidOperationException("Assessment rules require one explicit lesson type per scope.");
            if (rule.Dates.Count != rule.Dates.Distinct().Count())
                throw new InvalidOperationException("Assessment dates must be distinct within a scope.");
            var existing = remote.Where(x => x.LessonType == rule.LessonType && RegistryAssessmentTopics.IsAssessment(x.Topic))
                .OrderBy(x => x.DateTime).ToArray();
            var dates = rule.Dates.Order().ToArray();
            for (int i = 0; i < dates.Length; i++)
            {
                var candidates = Enumerable.Range(start: 0, count: local.Length).Where(index => local[index].RegistryLessonType == rule.LessonType
                    && DateOnly.FromDateTime(local[index].DateTime) == dates[i]).ToArray();
                if (candidates.Length != 1)
                    throw new InvalidOperationException("An assessment date must identify exactly one scheduled lesson of its type.");
                reserved.Add(candidates[0], i < existing.Length ? existing[i].Topic! : "Atestare " + (i + 1));
            }
            // Unconfigured additional assessments retain their original dates.
            foreach (var extra in existing.Skip(dates.Length))
                if (!ReserveExisting(extra)) protectedRemote.Add(Array.IndexOf(remote, extra));
            // Moving an assessment outside the known timetable could leave an orphaned entry.
            foreach (var moved in existing.Take(dates.Length))
                if (!local.Any(x => x.DateTime.Date == moved.DateTime.Date && x.RegistryLessonType == moved.LessonType))
                    throw new InvalidOperationException("Moving an assessment requires its original lesson in the selected timetable.");
        }
        for (int i = 0; i < remote.Length; i++)
        {
            var assessment = remote[i];
            if (!RegistryAssessmentTopics.IsAssessment(assessment.Topic)) continue;
            if (configuredTypes.Contains(assessment.LessonType)) continue;
            if (!ReserveExisting(assessment)) protectedRemote.Add(i);
        }
        var indexes = new Dictionary<(LessonType Type, LessonGroups Groups), int>();
        var registryIndexes = new Dictionary<LessonType, int>();
        var useRegistryTopics = new HashSet<LessonType>();
        var registryTopics = remote.Where(lesson => !string.IsNullOrWhiteSpace(lesson.Topic)
                && !RegistryAssessmentTopics.IsAssessment(lesson.Topic))
            .GroupBy(lesson => lesson.LessonType)
            .ToDictionary(keySelector: group => group.Key, elementSelector: group => group.OrderBy(lesson => lesson.DateTime).Select(lesson => lesson.Topic!).ToArray());
        for (int i = 0; i < local.Length; i++)
        {
            var lesson = local[i];
            if (reserved.TryGetValue(i, out var topic)) local[i] = lesson with { Topic = topic };
            else
            {
                var topicGroups = getTopicGroups is null ? new LessonGroups() : getTopicGroups(lesson).Ordered();
                var topicKey = (Type: lesson.RegistryLessonType, Groups: topicGroups);
                var index = indexes.GetValueOrDefault(topicKey);
                var registryIndex = registryIndexes.GetValueOrDefault(lesson.RegistryLessonType);
                var regularTopic = getTopic(lesson, index);
                if (registryIndex == 0 && regularTopic is null && configuredTypes.Contains(lesson.RegistryLessonType))
                    useRegistryTopics.Add(lesson.RegistryLessonType);
                if (useRegistryTopics.Contains(lesson.RegistryLessonType))
                    regularTopic = registryTopics.GetValueOrDefault(lesson.RegistryLessonType)?.ElementAtOrDefault(registryIndex) ?? "";
                if (regularTopic is null && configuredTypes.Contains(lesson.RegistryLessonType)
                    && remote.Any(existing => existing.DateTime.Date == lesson.DateTime.Date
                        && existing.LessonType == lesson.RegistryLessonType && RegistryAssessmentTopics.IsAssessment(existing.Topic)))
                    throw new InvalidOperationException("Moving an assessment requires a regular topic for the freed occurrence.");
                local[i] = lesson with { Topic = regularTopic };
                indexes[topicKey] = index + 1;
                registryIndexes[lesson.RegistryLessonType] = registryIndex + 1;
            }
        }
        return (local, remote.Where((_, index) => !protectedRemote.Contains(index)).ToArray());

        bool ReserveExisting(RemoteLessonInstance assessment)
        {
            var candidates = Enumerable.Range(start: 0, count: local.Length).Where(index => { bool conditionResult; if (local[index].DateTime == assessment.DateTime) { if (assessment.LessonType == LessonType.Unspecified) { conditionResult = true; } else { conditionResult = local[index].RegistryLessonType == assessment.LessonType; } } else { conditionResult = false; } if (conditionResult) { return true; } return false; }).ToArray();
            if (candidates.Length == 0)
                candidates = Enumerable.Range(start: 0, count: local.Length).Where(index => { bool conditionResult; if (local[index].DateTime.Date == assessment.DateTime.Date) { if (assessment.LessonType == LessonType.Unspecified) { conditionResult = true; } else { conditionResult = local[index].RegistryLessonType == assessment.LessonType; } } else { conditionResult = false; } if (conditionResult) { return true; } return false; }).ToArray();
            if (candidates.Length == 0) return false;
            if (candidates.Length != 1 || reserved.ContainsKey(candidates[0]))
                throw new InvalidOperationException("Multiple assessments identify the same scheduled lesson.");
            reserved.Add(candidates[0], assessment.Topic!);
            return true;
        }
    }
}
