using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace ScheduleLib;

// TODO: Separate the filters out (array of filters)
public struct ScheduleFilter()
{
    public QualificationType? QualificationType;
    public Grade? Grade;
    public TeacherFilter TeacherFilter = new();
    public GroupFilter GroupFilter = new();
}

public struct GroupFilter()
{
    public SubGroup[]? SubGroups = null;
    public GroupId[]? GroupIds = null;
}

public struct TeacherFilter()
{
    public TeacherId[]? IncludeIds = null;
}

public sealed class FilteredSchedule
{
    public required Schedule Source;
    public required IEnumerable<RegularLesson> Lessons;
    public required GroupId[] Groups;
    public required TimeSlot[] TimeSlots;
    public required DayOfWeek[] Days;

    public bool IsEmpty => Days.Length == 0;
}

public static class FilterHelper
{
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public static FilteredSchedule Filter(this Schedule schedule, in ScheduleFilter filter)
    {
        var lessons = GetRegularLessons(filter);

        GroupId[] groups;
        TimeSlot[] timeSlots;
        DayOfWeek[] days;
        if (!lessons.Any())
        {
            groups = [];
            timeSlots = [];
            days = [];
        }
        else
        {
            groups = GroupsFromLessons();
            timeSlots = TimeSlotsFromLessons();
            days = UsedDaysOfWeek();
        }

        return new()
        {
            Source = schedule,
            Groups = groups,
            Lessons = lessons,
            TimeSlots = timeSlots,
            Days = days,
        };

        IEnumerable<RegularLesson> GetRegularLessons(ScheduleFilter filter)
        {
            foreach (var regularLesson in schedule.RegularLessons)
            {
                if (!PassesGradeTest())
                {
                    continue;
                }
                if (!PassesTeacherIdFilter())
                {
                    continue;
                }
                if (!PassesSubGroupFilter())
                {
                    continue;
                }
                if (!PassesGroupFilter())
                {
                    continue;
                }
                yield return regularLesson;

                bool PassesGradeTest()
                {
                    var groupId = regularLesson.Lesson.Group;
                    var g = schedule.Get(groupId);
                    if (filter.QualificationType is { } q)
                    {
                        if (g.QualificationType != q)
                        {
                            return false;
                        }
                    }
                    if (filter.Grade is { } grade)
                    {
                        if (g.Grade != grade)
                        {
                            return false;
                        }
                    }
                    return true;
                }

                bool PassesTeacherIdFilter()
                {
                    if (filter.TeacherFilter.IncludeIds is not { } includedIds)
                    {
                        return true;
                    }
                    foreach (var teacherId in regularLesson.Lesson.Teachers)
                    {
                        if (includedIds.Contains(teacherId))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                bool PassesSubGroupFilter()
                {
                    if (filter.GroupFilter.SubGroups is not { } subGroups)
                    {
                        return true;
                    }
                    foreach (var subGroup in subGroups)
                    {
                        if (subGroup == regularLesson.Lesson.SubGroup)
                        {
                            return true;
                        }
                    }
                    return false;
                }

                bool PassesGroupFilter()
                {
                    if (filter.GroupFilter.GroupIds is not { } groupIds)
                    {
                        return true;
                    }
                    foreach (var groupId1 in groupIds)
                    {
                        if (groupId1 == regularLesson.Lesson.Group)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        // Find out which groups have regular lessons.
        GroupId[] GroupsFromLessons()
        {
            HashSet<GroupId> groups1 = new();
            foreach (var lesson in lessons)
            {
                groups1.Add(lesson.Lesson.Group);
            }
            var ret = groups1.ToArray();
            // Sorting by index is fine here.
            Array.Sort(ret);
            return ret;
        }

        TimeSlot[] TimeSlotsFromLessons()
        {
            // Just do min max rather than checking if they exist.
            // Could just as well just hardcode.
            var min = FindMin();
            var max = FindMax();

            var len = max.Index - min.Index + 1;
            var ret = new TimeSlot[len];
            for (int i = min.Index; i <= max.Index; i++)
            {
                ret[i - min.Index] = new TimeSlot(i);
            }
            return ret;

            TimeSlot FindMin()
            {
                using var e = lessons.GetEnumerator();
                bool ok = e.MoveNext();
                Debug.Assert(ok);
                var min1 = e.Current.Date.TimeSlot;
                while (true)
                {
                    if (min1 == TimeSlot.First)
                    {
                        return min1;
                    }

                    if (!e.MoveNext())
                    {
                        return min1;
                    }

                    var t = e.Current.Date.TimeSlot;
                    if (t < min1)
                    {
                        min1 = t;
                    }
                }
            }

            TimeSlot FindMax()
            {
                var max1 = TimeSlot.First;
                foreach (var l in lessons)
                {
                    var t = l.Date.TimeSlot;
                    if (t > max1)
                    {
                        max1 = t;
                    }
                }
                return max1;
            }
        }

        // TODO: Use bit sets
        DayOfWeek[] UsedDaysOfWeek()
        {
            var ret = new HashSet<DayOfWeek>();
            foreach (var lesson in lessons)
            {
                ret.Add(lesson.Date.DayOfWeek);
            }
            return ret.Order().ToArray();
        }
    }
}
