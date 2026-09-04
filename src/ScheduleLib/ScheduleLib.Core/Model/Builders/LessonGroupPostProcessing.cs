using ScheduleLib.Helper;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Moves registered specialization labels out of the subgroup field into the specialization field.
    /// Raw sources and remapped aliases both store their label in the subgroup field during parsing;
    /// only here, once the lesson groups are known, does the value get classified.
    /// </summary>
    internal static void ClassifySubGroups(this ScheduleBuilder s)
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
            if (!s.TryGetSpecialization(remapped, out var specialization))
            {
                group.SubGroup = remapped;
                lesson.Base.Group = group;
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
    /// Resolves a raw label using the built-in values and the configured registry.
    /// The registry supports future values without making them enum members.
    /// </summary>
    internal static bool TryGetSpecialization(
        this ScheduleBuilder s,
        SubGroup label,
        out Specialization specialization)
    {
        if (Specializations.TryFromValue(label.Value, out specialization))
        {
            return true;
        }
        if (s.SpecializationRegistry is { } registry
            && registry.TryFromValue(label.Value, out specialization))
        {
            return true;
        }
        specialization = default;
        return false;
    }

    /// <summary>
    /// Derives the non-beginner language proficiency state. Sources only annotate the beginner
    /// lessons; the unannotated counterpart lessons of the same group and course become
    /// <see cref="SpecialSubGroups.NonBeginners"/>. Already normalized data is left alone, so
    /// building over deserialized schedules is idempotent.
    /// </summary>
    internal static void NormalizeLanguageProficiency(this ScheduleBuilder s)
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

            foreach (var g in lesson.Base.Group.Groups)
            {
                coveredSplits.Add((g, course));
            }
            counterparts.Add(lesson);
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

    /// <summary>
    /// Groups the subgroup values observed per participating group.
    /// The <c>opțional</c> marker and <see cref="SubGroup.All"/> are ignored.
    /// </summary>
    private static void CollectObservedSubGroups(
        ScheduleBuilder s,
        Dictionary<GroupId, HashSet<SubGroup>> observed)
    {
        foreach (var lesson in Lessons(s))
        {
            var subGroup = lesson.Base.Group.SubGroup;
            if (subGroup == SubGroup.All
                || subGroup == SpecialSubGroups.Optional)
            {
                continue;
            }
            foreach (var g in lesson.Base.Group.Groups)
            {
                if (!observed.TryGetValue(g, out var set))
                {
                    set = [];
                    observed[g] = set;
                }
                set.Add(subGroup);
            }
        }
    }

    /// <summary>
    /// Observed numeric subgroups must form a contiguous prefix starting at I:
    /// if X occurs, every value from I through X must occur somewhere for the group.
    /// </summary>
    private static void CheckNumericSubGroupsAreContiguous(this ScheduleBuilder s)
    {
        var observed = new Dictionary<GroupId, HashSet<SubGroup>>();
        CollectObservedSubGroups(s, observed);
        foreach (var (groupId, values) in observed)
        {
            var numbers = values
                .Select(x => NumberHelper.FromRoman(x.Value))
                .Where(x => x is not null)
                .Select(x => x!.Value)
                .OrderBy(x => x)
                .ToArray();
            if (numbers.Length == 0)
            {
                continue;
            }
            for (int expected = 1; expected <= numbers[^1]; expected++)
            {
                if (!numbers.Contains(expected))
                {
                    throw new InvalidOperationException(
                        $"The numeric subgroups of group '{GroupName(s, groupId)}' must form a contiguous prefix starting at I. "
                        + $"Missing '{NumberHelper.ToRoman(expected)}' while '{NumberHelper.ToRoman(numbers[^1])}' occurs.");
                }
            }
        }
    }

    /// <summary>
    /// A group has either zero observed language subgroup values or at least two.
    /// </summary>
    private static void CheckLanguageSubGroupCount(this ScheduleBuilder s)
    {
        var observed = new Dictionary<GroupId, HashSet<SubGroup>>();
        CollectObservedSubGroups(s, observed);
        foreach (var (groupId, values) in observed)
        {
            int languageCount = values.Count(x => x == SpecialSubGroups.Ro
                || x == SpecialSubGroups.Ru
                || x == SpecialSubGroups.Eng);
            if (languageCount == 1)
            {
                throw new InvalidOperationException(
                    $"The group '{GroupName(s, groupId)}' has a single language subgroup, but a group "
                    + "must have either zero observed language subgroups or at least two.");
            }
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
