using System.Text;

namespace ScheduleLib.Builders;

public static partial class ScheduleBuilderHelper
{
    /// <summary>
    /// Fails when two weekly lessons occupy the same period, day and time slot, share a
    /// group, and their student populations are not separated on any split dimension:
    /// week parity, subgroup, specialization or alternative. One-time lessons are not
    /// checked.
    /// <para>
    /// Pairs listed in <see cref="ScheduleBuilder.OverlapValidationConfig"/>'s allowlist
    /// are tolerated; every other offending pair ends up in a single error.
    /// </para>
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
        var errors = CollectOverlapErrors(s, lessons, config);
        if (errors.Count > 0)
        {
            throw OverlappingLessonsException.ForOverlaps(errors);
        }
    }

    private static List<string> CollectOverlapErrors(
        ScheduleBuilder s,
        List<WeeklyLessonBuilderModel> lessons,
        LessonOverlapValidationConfig config)
    {
        var errors = new List<string>();
        for (int i = 0; i < lessons.Count; i++)
        {
            for (int j = i + 1; j < lessons.Count; j++)
            {
                if (DescribeOverlapIfConflicting(s, config, a: lessons[i].Data, b: lessons[j].Data) is { } error)
                {
                    errors.Add(error);
                }
            }
        }
        return errors;
    }

    private static string? DescribeOverlapIfConflicting(
        ScheduleBuilder s,
        LessonOverlapValidationConfig config,
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
        if (!ParitiesIntersect(a: a.Date.Parity, b: b.Date.Parity))
        {
            return null;
        }
        var shared = SharedGroup(a: a.Base.Group.Groups, b: b.Base.Group.Groups);
        if (shared is null)
        {
            return null;
        }
        if (!SplitDimensionsIntersect(a: a.Base.Group, b: b.Base.Group))
        {
            return null;
        }
        if (IsAllowlisted(config.Allowlist, s, a: a, b: b, shared.Value))
        {
            return null;
        }
        return Describe(s, a: a, b: b, shared.Value);
    }

    private static bool ParitiesIntersect(Parity? a, Parity? b)
    {
        if (a is not { } pa || b is not { } pb)
        {
            // Without a parity the lesson cannot be proven disjoint.
            return true;
        }

        bool CheckCondition()
        {
            if (pa == Parity.EveryWeek)
            {
                return true;
            }

            if (pb == Parity.EveryWeek)
            {
                return true;
            }

            if (pa == pb)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            return true;
        }

        return false;
    }

    private static bool SplitDimensionsIntersect(in LessonBuilderGroupData a, in LessonBuilderGroupData b)
    {
        var aKey = new GroupPartitionKey(a.SubGroup, a.Specialization, a.Alternative);
        var bKey = new GroupPartitionKey(b.SubGroup, b.Specialization, b.Alternative);
        foreach (var dimension in PartitionDimensions.All)
        {
            if (!ValuesIntersect(
                a: aKey.GetPartitionDimension(dimension).Value,
                b: bKey.GetPartitionDimension(dimension).Value))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValuesIntersect(string? a, string? b)
    {
        if (a is null)
        {
            return true;
        }

        if (b is null)
        {
            return true;
        }

        if (a == b)
        {
            return true;
        }

        return false;
    }

    private static bool Intersects(SubGroup a, SubGroup b)
        => ValuesIntersect(a: a.Value, b: b.Value);
    private static bool Intersects(Specialization a, Specialization b)
        => ValuesIntersect(a: a.Value, b: b.Value);
    private static bool Intersects(Alternative a, Alternative b)
        => ValuesIntersect(a: a.Value, b: b.Value);

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

    private static bool IsAllowlisted(
        List<LessonOverlapAllowlistEntry> allowlist,
        ScheduleBuilder s,
        in WeeklyLessonBuilderModelData a,
        in WeeklyLessonBuilderModelData b,
        GroupId shared)
    {
        if (allowlist.Count == 0)
        {
            return false;
        }
        var aName = s.Courses.Ref(a.Base.General.Course!.Value.Id).FullName;
        var bName = s.Courses.Ref(b.Base.General.Course!.Value.Id).FullName;
        var groupName = s.Groups.Ref(shared.Value).Name;
        foreach (var entry in allowlist)
        {
            bool CalculateConditionResult()
            {
                if (aName == entry.CourseA)
                {
                    if (bName == entry.CourseB)
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

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                conditionResult = true;
            }
            else
            {
                if (aName == entry.CourseB)
                {
                    conditionResult = bName == entry.CourseA;
                }
                else
                {
                    conditionResult = false;
                }
            }

            bool coursesMatch = conditionResult;
            if (!coursesMatch)
            {
                continue;
            }
            if (entry.GroupName is { } entryGroup && entryGroup != groupName)
            {
                continue;
            }
            if (entry.Day is { } entryDay && entryDay != a.Date.DayOfWeek)
            {
                continue;
            }
            if (entry.TimeSlot is { } entrySlot && !entrySlot.Equals(a.Date.TimeSlot))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    private static string Describe(
        ScheduleBuilder s,
        in WeeklyLessonBuilderModelData a,
        in WeeklyLessonBuilderModelData b,
        GroupId shared)
    {
        var sb = new StringBuilder();
        sb.Append("  shared group '")
            .Append(s.Groups.Ref(shared.Value).Name)
            .Append("':\n    ");
        AppendLesson(sb, s, a);
        sb.Append("\n    ");
        AppendLesson(sb, s, b);
        return sb.ToString();

        static void AppendLesson(StringBuilder sb, ScheduleBuilder s, in WeeklyLessonBuilderModelData lesson)
        {
            var course = s.Courses.Ref(lesson.Base.General.Course!.Value.Id);
            global::System.Text.StringBuilder append = sb.Append('\'');
            string value = course.FullName;
            global::System.Text.StringBuilder append2 = append.Append(value);
            char value2 = '\'';
            global::System.Text.StringBuilder append3 = append2.Append(value2);
            string value3 = " on ";
            global::System.Text.StringBuilder append4 = append3.Append(value3);
            object? value4 = lesson.Date.DayOfWeek;
            global::System.Text.StringBuilder append5 = append4.Append(value4);
            string value5 = " slot ";
            global::System.Text.StringBuilder append6 = append5.Append(value5);
            int value6 = lesson.Date.TimeSlot!.Value.Index;
            global::System.Text.StringBuilder append7 = append6.Append(value6);
            string value7 = ", ";
            global::System.Text.StringBuilder append8 = append7.Append(value7);
            string value8;
            if (lesson.Date.Parity is { } parity)
            {
                value8 = parity.ToString();
            }
            else
            {
                value8 = "no parity";
            }

            global::System.Text.StringBuilder append9 = append8.Append(value8);
            string value9 = ", subgroup: ";
            global::System.Text.StringBuilder append10 = append9.Append(value9);
            string value10 = lesson.Base.Group.SubGroup.Value ?? "all";
            global::System.Text.StringBuilder append11 = append10.Append(value10);
            string value11 = ", specialization: ";
            global::System.Text.StringBuilder append12 = append11.Append(value11);
            string value12 = lesson.Base.Group.Specialization.Value ?? "all";
            global::System.Text.StringBuilder append13 = append12.Append(value12);
            string value13 = ", alternative: ";
            global::System.Text.StringBuilder append14 = append13.Append(value13);
            string value14 = lesson.Base.Group.Alternative.Value ?? "all";
            append14.Append(value14);
        }
    }
}
