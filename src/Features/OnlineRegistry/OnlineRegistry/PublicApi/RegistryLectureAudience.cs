namespace ScheduleLib.OnlineRegistry;

internal static class RegistryLectureAudience
{
    internal static string Format(Schedule schedule, AnyLessonId id)
    {
        var source = schedule.Get(id);
        var scopes = new List<string>
        {
            RegistryTopicScope.Format(
                schedule,
                source.Lesson.Groups,
                source.Lesson.GroupPartitionKey),
        };
        if (source.Lesson.SubGroup != SubGroup.All && !source.Lesson.SubGroup.IsLanguageSubGroup())
        {
            return scopes[0];
        }

        foreach (var candidate in schedule.EnumerateAllLessons())
        {
            if (!schedule.IsLessonActive(candidate.Id)
                || candidate.Id == id
                || candidate.Lesson.SubGroup != SubGroup.All && !candidate.Lesson.SubGroup.IsLanguageSubGroup()
                || !(candidate.Lesson with
                {
                    Groups = source.Lesson.Groups,
                    SubGroup = source.Lesson.SubGroup,
                }).Equals(source.Lesson))
            {
                continue;
            }

            bool MatchesLessonDate()
            {
                if (source.Weekly is { } weekly && candidate.Weekly is { } otherWeekly
                    && weekly.Date == otherWeekly.Date)
                {
                    return true;
                }
                if (source.OneTime is not { } oneTime)
                {
                    return false;
                }
                if (candidate.OneTime is not { } otherOneTime)
                {
                    return false;
                }
                return oneTime.Date == otherOneTime.Date;
            }

            var sameTime = MatchesLessonDate();
            if (!sameTime || candidate.Lesson.Groups.IsSetEquals(source.Lesson.Groups))
            {
                continue;
            }

            string scope = RegistryTopicScope.Format(
                schedule,
                candidate.Lesson.Groups,
                candidate.Lesson.GroupPartitionKey);
            scopes.Add(scope);
        }
        return string.Join(
            ", ",
            scopes.Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal));
    }
}
