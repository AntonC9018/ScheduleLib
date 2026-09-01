namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Moves known specialization labels out of the subgroup field into the specialization field.
    /// Raw sources and remapped aliases both store their label in the subgroup field during parsing;
    /// only here, once the lesson groups are known, does the value get classified.
    /// </summary>
    public static void ClassifySubGroups(this ScheduleBuilder s)
    {
        var remappings = s.Remappings.SubGroupNameRemappings;
        foreach (var lesson in s.WeeklyLessons.List)
        {
            Classify(lesson);
        }
        foreach (var lesson in s.OneTimeLessons.List)
        {
            Classify(lesson);
        }

        void Classify(ILessonBuilderModel lesson)
        {
            var group = lesson.Base.Group;
            if (group.SubGroup == SubGroup.All)
            {
                return;
            }
            var remapped = remappings.Remap(group.SubGroup);
            if (!Specializations.TryFromValue(remapped.Value, out var specialization))
            {
                return;
            }
            if (group.Specialization != Specialization.All
                && group.Specialization != specialization)
            {
                throw new InvalidOperationException(
                    $"A lesson may not have two specializations: '{group.Specialization.Value}' and '{specialization.Value}'.");
            }
            group.Specialization = specialization;
            group.SubGroup = SubGroup.All;
            lesson.Base.Group = group;
        }
    }

    /// <summary>
    /// Derives the non-beginner language proficiency state. Sources only annotate the beginner
    /// lessons; the unannotated counterpart lessons of the same group and course become
    /// <see cref="SpecialSubGroups.NonBeginners"/>. Already normalized data is left alone, so
    /// building over deserialized schedules is idempotent.
    /// </summary>
    public static void NormalizeLanguageProficiency(this ScheduleBuilder s)
    {
        var beginnerSplits = new HashSet<(GroupId Group, CourseId Course)>();
        var normalizedSplits = new HashSet<(GroupId Group, CourseId Course)>();
        foreach (var lesson in Lessons(s))
        {
            if (lesson.Base.General.Course is not { } course
                || course.IsInvalid)
            {
                continue;
            }
            var subGroup = lesson.Base.Group.SubGroup;
            if (subGroup == SpecialSubGroups.Beginners)
            {
                AddSplits(beginnerSplits, lesson, course);
            }
            else if (subGroup == SpecialSubGroups.NonBeginners)
            {
                AddSplits(normalizedSplits, lesson, course);
            }
        }
        if (beginnerSplits.Count == 0)
        {
            return;
        }

        var counterparts = new List<ILessonBuilderModel>();
        var coveredSplits = new HashSet<(GroupId Group, CourseId Course)>();
        foreach (var lesson in Lessons(s))
        {
            if (lesson.Base.Group.SubGroup != SubGroup.All)
            {
                continue;
            }
            if (lesson.Base.General.Course is not { } course
                || course.IsInvalid)
            {
                continue;
            }

            var splitGroups = CountSplitGroups(lesson, course);
            if (splitGroups == 0)
            {
                continue;
            }
            if (splitGroups != lesson.Base.Group.Groups.Count)
            {
                throw new InvalidOperationException(
                    $"The lesson for groups '{GroupNames(s, lesson.Base.Group.Groups)}' and course '{CourseName(s, course)}' "
                    + "mixes groups with and without the language proficiency split for that course. "
                    + "One stored subgroup value could not represent both meanings.");
            }

            bool alreadyNormalized = false;
            foreach (var g in lesson.Base.Group.Groups)
            {
                coveredSplits.Add((g, course));
                if (normalizedSplits.Contains((g, course)))
                {
                    alreadyNormalized = true;
                }
            }
            if (!alreadyNormalized)
            {
                counterparts.Add(lesson);
            }
        }

        foreach (var (group, course) in beginnerSplits)
        {
            if (!coveredSplits.Contains((group, course))
                && !normalizedSplits.Contains((group, course)))
            {
                throw new InvalidOperationException(
                    $"The beginner subgroup lesson for group '{GroupName(s, group)}' and course '{CourseName(s, course)}' "
                    + "has no unannotated counterpart lesson for the same group and course.");
            }
        }

        foreach (var lesson in counterparts)
        {
            lesson.Base.Group.SubGroup = SpecialSubGroups.NonBeginners;
        }

        int CountSplitGroups(ILessonBuilderModel lesson, CourseId course)
        {
            int count = 0;
            foreach (var g in lesson.Base.Group.Groups)
            {
                if (beginnerSplits.Contains((g, course)))
                {
                    count++;
                }
            }
            return count;
        }
    }

    private static void AddSplits(
        HashSet<(GroupId Group, CourseId Course)> splits,
        ILessonBuilderModel lesson,
        CourseId course)
    {
        foreach (var g in lesson.Base.Group.Groups)
        {
            splits.Add((g, course));
        }
    }

    private static IEnumerable<ILessonBuilderModel> Lessons(ScheduleBuilder s)
    {
        foreach (var lesson in s.WeeklyLessons.List)
        {
            yield return lesson;
        }
        foreach (var lesson in s.OneTimeLessons.List)
        {
            yield return lesson;
        }
    }

    private static string GroupNames(ScheduleBuilder s, LessonGroups groups)
    {
        var names = groups.Select(g => GroupName(s, g));
        return string.Join(", ", names);
    }

    private static string GroupName(ScheduleBuilder s, GroupId id)
    {
        if (id.Value < 0 || id.Value >= s.Groups.List.Count)
        {
            return id.Value.ToString();
        }
        return s.Groups.List[id.Value].Name;
    }

    private static string CourseName(ScheduleBuilder s, CourseId id)
    {
        if (id.Id < 0 || id.Id >= s.Courses.List.Count)
        {
            return id.Id.ToString();
        }
        return s.Courses.List[id.Id].FullName;
    }
}
