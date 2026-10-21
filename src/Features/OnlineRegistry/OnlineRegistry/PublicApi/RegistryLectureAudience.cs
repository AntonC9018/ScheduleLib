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
                || !(candidate.Lesson with { Groups = source.Lesson.Groups, SubGroup = source.Lesson.SubGroup }).Equals(source.Lesson)) continue;
            var sameTime = source.Weekly is { } weekly && candidate.Weekly is { } otherWeekly && weekly.Date == otherWeekly.Date
                || source.OneTime is { } oneTime && candidate.OneTime is { } otherOneTime && oneTime.Date == otherOneTime.Date;
            if (!sameTime || candidate.Lesson.Groups.IsSetEquals(source.Lesson.Groups)) continue;
            scopes.Add(RegistryTopicScope.Format(schedule, candidate.Lesson.Groups, candidate.Lesson.GroupPartitionKey));
        }
        return string.Join(", ", scopes.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal));
    }
}
