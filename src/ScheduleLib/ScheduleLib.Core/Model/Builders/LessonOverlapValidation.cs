using System.Text;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Fails when two weekly lessons occupy the same period, day and time slot, share a
    /// group, and their student populations are not separated on any split dimension:
    /// week parity, subgroup, specialization or alternative. One-time lessons are not
    /// checked. Every offending pair ends up in a single error.
    /// </summary>
    private static void ValidateLessonOverlaps(this ScheduleBuilder s)
    {
        if (s.OverlapValidationConfig is not { } config)
        {
            return;
        }
        if (config.StudyYear is { } year && s.GroupParseContext?.CurrentStudyYear != year)
        {
            return;
        }
        var lessons = s.WeeklyLessons.List;
        var errors = CollectOverlapErrors(s, lessons);
        if (errors.Count > 0)
        {
            throw OverlappingLessonsException.ForOverlaps(errors);
        }
    }

    private static List<string> CollectOverlapErrors(
        ScheduleBuilder s,
        List<WeeklyLessonBuilderModel> lessons)
    {
        var errors = new List<string>();
        for (int i = 0; i < lessons.Count; i++)
        {
            for (int j = i + 1; j < lessons.Count; j++)
            {
                if (DescribeOverlapIfConflicting(s, lessons[i].Data, lessons[j].Data) is { } error)
                {
                    errors.Add(error);
                }
            }
        }
        return errors;
    }

    private static string? DescribeOverlapIfConflicting(
        ScheduleBuilder s,
        in WeeklyLessonBuilderModelData a,
        in WeeklyLessonBuilderModelData b)
    {
        if (a.Base.General.Period != b.Base.General.Period)
        {
            return null;
        }
        if (a.Date.DayOfWeek is not { } day || a.Date.DayOfWeek != b.Date.DayOfWeek)
        {
            return null;
        }
        if (a.Date.TimeSlot is not { } slot || !a.Date.TimeSlot.Equals(b.Date.TimeSlot))
        {
            return null;
        }
        if (!ParitiesIntersect(a.Date.Parity, b.Date.Parity))
        {
            return null;
        }
        var shared = SharedGroup(a.Base.Group.Groups, b.Base.Group.Groups);
        if (shared is null)
        {
            return null;
        }
        if (!SplitDimensionsIntersect(a.Base.Group, b.Base.Group))
        {
            return null;
        }
        return Describe(s, a, b, shared.Value);
    }

    private static bool ParitiesIntersect(Parity? a, Parity? b)
    {
        if (a is not { } pa || b is not { } pb)
        {
            // Without a parity the lesson cannot be proven disjoint.
            return true;
        }
        return pa == Parity.EveryWeek || pb == Parity.EveryWeek || pa == pb;
    }

    private static bool SplitDimensionsIntersect(in LessonBuilderGroupData a, in LessonBuilderGroupData b)
    {
        var aKey = new GroupPartitionKey(a.SubGroup, a.Specialization, a.Alternative);
        var bKey = new GroupPartitionKey(b.SubGroup, b.Specialization, b.Alternative);
        foreach (var dimension in PartitionDimensions.All)
        {
            if (!ValuesIntersect(
                aKey.GetPartitionDimension(dimension).Value,
                bKey.GetPartitionDimension(dimension).Value))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValuesIntersect(string? a, string? b)
        => a is null || b is null || a == b;

    private static bool Intersects(SubGroup a, SubGroup b)
        => ValuesIntersect(a.Value, b.Value);
    private static bool Intersects(Specialization a, Specialization b)
        => ValuesIntersect(a.Value, b.Value);
    private static bool Intersects(Alternative a, Alternative b)
        => ValuesIntersect(a.Value, b.Value);

    private static GroupId? SharedGroup(in LessonGroups a, in LessonGroups b)
    {
        foreach (var ga in a)
        {
            foreach (var gb in b)
            {
                if (ga.Value == gb.Value)
                {
                    return ga;
                }
            }
        }
        return null;
    }

    private static string Describe(
        ScheduleBuilder s,
        in WeeklyLessonBuilderModelData a,
        in WeeklyLessonBuilderModelData b,
        GroupId shared)
    {
        var sb = new StringBuilder();
        sb.Append("  shared group '").Append(s.Groups.Ref(shared.Value).Name).Append("':\n    ");
        AppendLesson(sb, s, a);
        sb.Append("\n    ");
        AppendLesson(sb, s, b);
        return sb.ToString();

        static void AppendLesson(StringBuilder sb, ScheduleBuilder s, in WeeklyLessonBuilderModelData lesson)
        {
            var course = s.Courses.Ref(lesson.Base.General.Course!.Value.Id);
            sb.Append('\'').Append(course.FullName).Append('\'')
                .Append(" on ").Append(lesson.Date.DayOfWeek)
                .Append(" slot ").Append(lesson.Date.TimeSlot!.Value.Index)
                .Append(", ").Append(lesson.Date.Parity is { } parity ? parity.ToString() : "no parity")
                .Append(", subgroup: ").Append(lesson.Base.Group.SubGroup.Value ?? "all")
                .Append(", specialization: ").Append(lesson.Base.Group.Specialization.Value ?? "all")
                .Append(", alternative: ").Append(lesson.Base.Group.Alternative.Value ?? "all");
        }
    }
}
