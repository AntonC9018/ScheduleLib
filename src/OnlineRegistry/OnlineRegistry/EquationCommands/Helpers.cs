using System.Diagnostics;

namespace ScheduleLib.OnlineRegistry;

internal enum LessonProperty
{
    Time,
    Date,
    DateTime,
    Type,
    Topic,
    Attendance,
}

internal struct MatchedLessonData
{
    public required LessonInstance Local;
    public required RemoteLessonInstance Remote;

    public readonly bool Equals(Schedule s)
    {
        ReadOnlySpan<LessonProperty> criteria =
        [
            LessonProperty.Type,
            LessonProperty.Topic,
            LessonProperty.Attendance,
            LessonProperty.DateTime,
        ];
        foreach (var criterion in criteria)
        {
            if (!CriterionEquals(criterion, s))
            {
                return false;
            }
        }
        return true;
    }

    public readonly bool CriterionEquals(LessonProperty criterion, Schedule s)
    {
        return criterion switch
        {
            LessonProperty.Time => TimeEquals(),
            LessonProperty.Type => LessonTypesEqual(s),
            LessonProperty.Topic => TopicEquals(),
            LessonProperty.Attendance => AttendanceEquals(),
            LessonProperty.Date => DateEquals(),
            LessonProperty.DateTime => DateTimeEquals(),
            _ => throw new NotSupportedException(),
        };
    }

    public readonly bool TimeEquals() => Local.DateTime == Remote.DateTime;
    public readonly bool DateEquals() => Local.DateTime.Date == Remote.DateTime.Date;
    public readonly bool DateTimeEquals() => Local.DateTime == Remote.DateTime;

    public readonly bool LessonTypesEqual(Schedule s)
    {
        if (Remote.LessonType == LessonType.Unspecified)
        {
            return true;
        }

        var lesson = s.Get(Local.LessonId).Lesson;
        if (lesson.Type == LessonType.Unspecified)
        {
            return true;
        }

        return lesson.Type == Remote.LessonType;
    }
    public readonly bool TopicEquals()
    {
        if (Local.Topic is null)
        {
            return true;
        }
        if (Local.Topic == Remote.Topic)
        {
            return true;
        }
        return false;
    }
    public readonly bool AttendanceEquals()
    {
        if (Local.Attendance is null)
        {
            return true;
        }
        if (Equal(Local.Attendance, Remote.Attendance))
        {
            return true;
        }
        return false;

        static bool Equal(Attendance[] all, Attendance[] existing)
        {
            if (all.Length != existing.Length)
            {
                return false;
            }
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                var b = existing[i];
                if (a == Attendance.None)
                {
                    continue;
                }
                if (a != b)
                {
                    return false;
                }
            }
            return true;
        }
    }

}

internal interface IDateTime
{
    DateTime DateTime { get; }
}

public static class MissingLessonDetection
{
    // Could be made to rely on a T1 : IDateTime, T2 : IDateTime,
    // and take a custom diff impl.
    internal static DateOnly GetDateOnly<T>(this T item) where T : struct, IDateTime
    {
        return DateOnly.FromDateTime(item.DateTime);
    }

    [Conditional("DEBUG")]
    internal static void AssertOrdered<T, U>(ref IEnumerable<T> items, Func<T, U> selector)
    {
        items = items.ToArray();
        var sorted = items.OrderBy(selector);
        Debug.Assert(items.SequenceEqual(sorted));
    }
}
