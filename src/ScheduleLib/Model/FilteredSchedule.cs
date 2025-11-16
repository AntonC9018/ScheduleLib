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
    public LessonFilter LessonFilter = new();
    public CourseFilter CourseFilter = new();
    public PeriodFilter PeriodFilter = new()
    {
        MatchAny = true,
    };
}

public struct PeriodFilter()
{
    public PeriodId PeriodId = PeriodId.Unspecified;
    public bool UnspecifiedIsAll = false;
    public bool MatchAny = false;
}

public struct GroupFilter()
{
    public SubGroup[]? SubGroups = null;
    public GroupId[]? OneOfGroupIds = null;
}

public record struct TeacherFilter()
{
    // ReSharper disable once TypeWithSuspiciousEqualityIsUsedInRecord.Global
    public TeacherId[]? IncludeIds = null;
}

public struct LessonFilter()
{
    public LessonType? LessonType = null;
}

public struct CourseFilter()
{
    public CourseId[]? IncludeIds = null;
}

public readonly struct RegularLessonAccessor
{
    public required RegularLessonId Id { get; init; }

    // TODO: Make this a struct and pull it from the schedule
    public required RegularLesson Item { get; init; }

    public readonly ref readonly LessonData Lesson => ref Item.Lesson;
    public readonly ref readonly RegularLessonDate Date => ref Item.Date;
}

public sealed class FilteredSchedule
{
    public required Schedule Source;
    public required RegularLessonAccessor[] Lessons;
    public required GroupId[] Groups;
    public required TimeSlot[] TimeSlots;
    public required DayOfWeek[] Days;
    public required TeacherId[] Teachers;

    public bool IsEmpty => Days.Length == 0;
}

public static class FilterHelper
{
    // NOTE: conceptually returns a builder, even though I'm using the same type here.
    public static ScheduleFilter Builder()
    {
        return new();
    }

    public static ScheduleFilter WithLatestPeriod(this ScheduleFilter b, Schedule schedule)
    {
        return b with
        {
            PeriodFilter = new()
            {
                PeriodId = schedule.LatestPeriodId(),
                UnspecifiedIsAll = true,
            },
        };
    }

    public static ScheduleFilter WithTeacher(this ScheduleFilter b, TeacherId teacher)
    {
        var prevIds = b.TeacherFilter.IncludeIds ?? [];
        return b with
        {
            TeacherFilter = new()
            {
                IncludeIds = [..prevIds, teacher],
            },
        };
    }

    public static ScheduleFilter WithCourse(this ScheduleFilter b, CourseId courseId)
    {
        var prevIds = b.CourseFilter.IncludeIds ?? [];
        return b with
        {
            CourseFilter = new()
            {
                IncludeIds = [..prevIds, courseId],
            },
        };
    }


    public static FilterGrouping<Accessor<Teacher, TeacherId>> TeacherGrouping(
        this Schedule schedule,
        in ScheduleFilter filter)
    {
        Debug.Assert(filter.TeacherFilter == default);
        return FilterGrouping(
            filter,
            schedule.EnumerateTeachers(),
            (teacher, filter) =>
            {
                filter.TeacherFilter.IncludeIds = [teacher.Id];
                return filter;
            });
    }

    public static FilterGrouping<T> FilterGrouping<T>(
        ScheduleFilter filter,
        IEnumerable<T> items,
        Func<T, ScheduleFilter, ScheduleFilter> producer)
    {
        return new FilterGrouping<T>(
            filter,
            items,
            producer);
    }

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public static FilteredSchedule Filter(this Schedule schedule, in ScheduleFilter filter)
    {
        var lessons = GetRegularLessons(filter).ToArray();

        GroupId[] groups;
        TimeSlot[] timeSlots;
        DayOfWeek[] days;
        TeacherId[] teachers;
        if (!lessons.Any())
        {
            groups = [];
            timeSlots = [];
            days = [];
            teachers = [];
        }
        else
        {
            groups = GroupsFromLessons();
            timeSlots = TimeSlotsFromLessons();
            days = UsedDaysOfWeek();
            teachers = TeachersFromLessons();
        }

        return new()
        {
            Source = schedule,
            Groups = groups,
            Lessons = lessons,
            TimeSlots = timeSlots,
            Days = days,
            Teachers = teachers,
        };

        IEnumerable<RegularLessonAccessor> GetRegularLessons(ScheduleFilter filter)
        {
            foreach (var l in schedule.EnumerateLessons())
            {
                var regularLesson = new RegularLessonAccessor
                {
                    Id = l.Id,
                    Item = l.Item,
                };
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
                if (!PassesPeriodFilter())
                {
                    continue;
                }
                if (!PassesLessonFilter())
                {
                    continue;
                }
                if (!PassesCourseFilter())
                {
                    continue;
                }
                yield return regularLesson;
                continue;

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
                    if (filter.GroupFilter.OneOfGroupIds is not { } groupIds)
                    {
                        return true;
                    }
                    bool CheckId(GroupId groupId)
                    {
                        foreach (var x in regularLesson.Lesson.Groups)
                        {
                            if (x == groupId)
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                    foreach (var groupId1 in groupIds)
                    {
                        if (CheckId(groupId1))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                bool PassesPeriodFilter()
                {
                    if (filter.PeriodFilter.MatchAny)
                    {
                        return true;
                    }
                    var p = regularLesson.Date.Period;
                    if (p.IsUnspecified
                        && filter.PeriodFilter.UnspecifiedIsAll)
                    {
                        return true;
                    }
                    if (p == filter.PeriodFilter.PeriodId)
                    {
                        return true;
                    }
                    return false;
                }

                bool PassesLessonFilter()
                {
                    if (LessonTypeFilter())
                    {
                        return true;
                    }
                    return false;

                    bool LessonTypeFilter()
                    {
                        if (filter.LessonFilter.LessonType is not { } lessonType)
                        {
                            return true;
                        }
                        if (regularLesson.Lesson.Type == lessonType)
                        {
                            return true;
                        }
                        return false;
                    }
                }

                bool PassesCourseFilter()
                {
                    if (filter.CourseFilter.IncludeIds is not { } courseIds)
                    {
                        return true;
                    }
                    var courseId = regularLesson.Lesson.Course;
                    if (courseIds.Contains(courseId))
                    {
                        return true;
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
                foreach (var group in lesson.Lesson.Groups)
                {
                    groups1.Add(group);
                }
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
                using var e = lessons.AsEnumerable().GetEnumerator();
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

        TeacherId[] TeachersFromLessons()
        {
            return lessons
                .SelectMany(x => x.Lesson.Teachers)
                .Distinct()
                .OrderBy(x => x.Id)
                .ToArray();
        }
    }
}

// TODO: O(n^2), make this linear
// TODO: Idk about this abstractions, think about this more.
public sealed class FilterGrouping<T>
{
    private readonly ScheduleFilter _default;
    private readonly IEnumerable<T> _items;
    private readonly Func<T, ScheduleFilter, ScheduleFilter> _producer;

    public FilterGrouping(
        ScheduleFilter defaultFilter,
        IEnumerable<T> items,
        Func<T, ScheduleFilter, ScheduleFilter> producer)
    {
        _default = defaultFilter;
        _items = items;
        _producer = producer;
    }

    public IEnumerable<(T Item, FilteredSchedule Schedule)> Filter(Schedule schedule)
    {
        foreach (var it in _items)
        {
            var filter = _producer(it, _default);
            var ret = schedule.Filter(filter);
            yield return (it, ret);
        }
    }
}

