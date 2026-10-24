namespace ScheduleLib.OnlineRegistry;

internal static class RegistryLectureAudience
{
    internal static string Format(Schedule schedule, AnyLessonId id)
    {
        var source = schedule.Get(id);
        var scopes = new List<string> { RegistryTopicScope.Format(schedule, source.Lesson.Groups, source.Lesson.GroupPartitionKey) };
        if (source.Lesson.SubGroup != SubGroup.All && !source.Lesson.SubGroup.IsLanguageSubGroup()) return scopes[0];
        foreach (var candidate in schedule.EnumerateAllLessons())
        {
            if (!schedule.IsLessonActive(candidate.Id) || candidate.Id == id
                || candidate.Lesson.SubGroup != SubGroup.All && !candidate.Lesson.SubGroup.IsLanguageSubGroup()
                || !(candidate.Lesson with { Groups = source.Lesson.Groups, SubGroup = source.Lesson.SubGroup }).Equals(source.Lesson))
                continue;
            bool conditionResult;
            if (source.Weekly is { } weekly)
            {
                if (candidate.Weekly is { } otherWeekly)
                {
                    conditionResult = weekly.Date == otherWeekly.Date;
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
                conditionResult = true;
            }
            else
            {
                if (source.OneTime is { } oneTime)
                {
                    if (candidate.OneTime is { } otherOneTime)
                    {
                        conditionResult = oneTime.Date == otherOneTime.Date;
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
            }

            var sameTime = conditionResult;
            if (!sameTime || candidate.Lesson.Groups.IsSetEquals(source.Lesson.Groups))
                continue;
            scopes.Add(RegistryTopicScope.Format(schedule, candidate.Lesson.Groups, candidate.Lesson.GroupPartitionKey));
        }
        return string.Join(", ", scopes.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
    }
}
