using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace App;

public readonly struct GroupAccessor
{
    private readonly Schedule _schedule;

    public GroupAccessor(Schedule schedule, GroupId id)
    {
        _schedule = schedule;
        Id = id;
    }

    public GroupId Id { get; }
    public Group Ref => _schedule.Groups[Id.Value];
}

public struct ScheduleFilter
{
    public required QualificationType QualificationType;
    public required int Grade;
}

public struct FilteredSchedule
{
    public required IEnumerable<RegularLesson> Lessons;
    public required IEnumerable<GroupId> Groups;
    public required TimeSlot[] TimeSlots;
}

public static class FilterHelper
{
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public static FilteredSchedule Filter(this Schedule schedule, ScheduleFilter filter)
    {
        var lessons = GetRegularLessons();
        var groups = GroupsFromLessons();
        var timeSlots = TimeSlotsFromLessons();

        return new()
        {
            Groups = groups,
            Lessons = lessons,
            TimeSlots = timeSlots,
        };

        IEnumerable<RegularLesson> GetRegularLessons()
        {
            foreach (var regularLesson in schedule.RegularLessons)
            {
                var groupId = regularLesson.Lesson.Group;
                var g = new GroupAccessor(schedule, groupId).Ref;
                if (g.QualificationType != filter.QualificationType)
                {
                    continue;
                }
                if (g.Grade == filter.Grade)
                {
                    continue;
                }
                yield return regularLesson;
            }
        }

        // Find out which groups have regular lessons.
        IEnumerable<GroupId> GroupsFromLessons()
        {
            HashSet<GroupId> groups1 = new();
            foreach (var lesson in lessons)
            {
                groups1.Add(lesson.Lesson.Group);
            }
            var ret = groups1.ToArray();
            // Sorting by name is fine here.
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
                ret[i] = new TimeSlot(i);
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
                    if (min1 > t)
                    {
                        min1 = t;
                    }
                }
            }

            TimeSlot FindMax()
            {
                TimeSlot max1 = TimeSlot.First;
                foreach (var l in lessons)
                {
                    var t = l.Date.TimeSlot;
                    if (max1 > t)
                    {
                        max1 = t;
                    }
                }
                return max1;
            }

        }
    }
}

public static class AllGroupsGenerator
{
    public struct Params
    {
        public required FilteredSchedule Schedule;
    }

    public static void GeneratePdfZi(Params p)
    {
    }
}