using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using ScheduleLib.Helper;

namespace ScheduleLib;

// TODO: Separate the filters out (array of filters)
public struct ScheduleFilter()
{
    public EnumBitArray<LessonRegularity> IncludeRegularity = EnumBitArray<LessonRegularity>.Empty;
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
    public Specialization[]? Specializations = null;
    public Alternative[]? Alternatives = null;
    public GroupId[]? OneOfGroupIds = null;
    public EnumBitArray<AttendanceMode> AttendanceMode = EnumBitArray<AttendanceMode>.Empty;
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

public sealed class FilteredSchedule
{
    public required Schedule Source;
    public required AnyLessonId[] Lessons;
    public required GroupId[] Groups;
    public required TimeSlot[] TimeSlots;
    public required DayOfWeek[] Days;
    public required TeacherId[] Teachers;

    public bool IsEmpty => Days.Length == 0;
}

public static class FilterHelper
{
    extension(FilteredSchedule s)
    {
        public IEnumerable<AnyLessonAccessor> EnumerateLessons()
        {
            return s.Lessons.Select(x => s.Source.Get(x));
        }

        public IEnumerable<WeeklyLessonAccessor> EnumerateWeeklyLessons()
        {
            return s.EnumerateLessons().WhereNotNull(x => x.Weekly);
        }

        public IEnumerable<OneTimeLessonAccessor> EnumerateOneTimeLessons()
        {
            return s.EnumerateLessons().WhereNotNull(x => x.OneTime);
        }
    }

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

    public static ScheduleFilter WithAttendanceMode(this ScheduleFilter b, params ReadOnlySpan<AttendanceMode> attendanceModes)
    {
        b.GroupFilter.AttendanceMode = EnumBitArray<AttendanceMode>.From(attendanceModes);
        return b;
    }
    public static ScheduleFilter WithLessonRegularity(
        this ScheduleFilter b,
        params ReadOnlySpan<LessonRegularity> regularity)
    {
        b.IncludeRegularity = EnumBitArray<LessonRegularity>.From(regularity);
        return b;
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

    public static ScheduleFilter WithLessonType(this ScheduleFilter b, LessonType t)
    {
        return b with
        {
            LessonFilter = new()
            {
                LessonType = t,
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

        IEnumerable<AnyLessonId> GetRegularLessons(ScheduleFilter filter)
        {
            // Observed specializations/alternatives per group, computed lazily: the
            // singleton rule needs them to decide whether a lesson's annotation
            // behaves as shared.
            Dictionary<GroupId, int>? specializationCounts = null;
            Dictionary<GroupId, int>? alternativeCounts = null;

            // Partition selections as dimension-agnostic values, hoisted: the
            // filter is constant across lessons.
            PartitionKey[]? subGroupSelection = filter.GroupFilter.SubGroups?.Select(s => (PartitionKey)s).ToArray();
            PartitionKey[]? specializationSelection = filter.GroupFilter.Specializations?.Select(s => (PartitionKey)s).ToArray();
            PartitionKey[]? alternativeSelection = filter.GroupFilter.Alternatives?.Select(a => (PartitionKey)a).ToArray();

            foreach (var l in schedule.EnumerateAllLessons())
            {
                // TODO: Can be optimized because these are in different arrays
                if (!PassesRegularityTest())
                {
                    continue;
                }

                if (!PassesGradeTest())
                {
                    continue;
                }
                if (!PassesTeacherIdFilter())
                {
                    continue;
                }
                if (!PassesPartitionFilters())
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
                yield return l.Id;
                continue;

                bool PassesRegularityTest()
                {
                    if (!filter.IncludeRegularity.IsEmpty)
                    {
                        return filter.IncludeRegularity.Contains(l.Regularity);
                    }
                    return true;
                }

                bool PassesGradeTest()
                {
                    var groupId = l.Lesson.Group;
                    if (groupId.IsInvalid)
                    {
                        return true;
                    }
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
                    foreach (var teacherId in l.Lesson.Teachers)
                    {
                        if (includedIds.Contains(teacherId))
                        {
                            return true;
                        }
                    }
                    return false;
                }

                bool PassesPartitionFilters()
                {
                    foreach (var dimension in PartitionDimensions.All)
                    {
                        if (!PassesPartitionFilter(dimension))
                        {
                            return false;
                        }
                    }
                    return true;
                }

                bool PassesPartitionFilter(PartitionDimension dimension)
                {
                    if (SelectedValues(dimension) is not { } selected)
                    {
                        return true;
                    }
                    PartitionKey lessonValue = l.Lesson.GroupPartitionKey.GetPartitionDimension(dimension);
                    if (lessonValue.Value is null)
                    {
                        return true;
                    }
                    if (ObservedCounts(dimension) is not { } counts)
                    {
                        // The subgroup partition has no singleton rule: plain membership.
                        return selected.Contains(lessonValue);
                    }
                    if (l.Lesson.Groups.IsEmpty)
                    {
                        return true;
                    }
                    foreach (var groupId in RelevantGroups())
                    {
                        // The effective value depends on the group being
                        // filtered: a group with fewer than two observed values treats
                        // every annotation as shared. A filter spanning
                        // several groups includes the lesson when it matches at least
                        // one of those group contexts.
                        if (!counts.TryGetValue(groupId, out var count)
                            || count < 2)
                        {
                            return true;
                        }
                        if (selected.Contains(lessonValue))
                        {
                            return true;
                        }
                    }
                    return false;

                    IEnumerable<GroupId> RelevantGroups()
                    {
                        foreach (var lessonGroup in l.Lesson.Groups)
                        {
                            if (filter.GroupFilter.OneOfGroupIds is not { } groupIds
                                || groupIds.Contains(lessonGroup))
                            {
                                yield return lessonGroup;
                            }
                        }
                    }
                }

                PartitionKey[]? SelectedValues(PartitionDimension dimension) => dimension switch
                {
                    PartitionDimension.SubGroup => subGroupSelection,
                    PartitionDimension.Specialization => specializationSelection,
                    PartitionDimension.Alternative => alternativeSelection,
                    _ => throw Unreachable(),
                };

                Dictionary<GroupId, int>? ObservedCounts(PartitionDimension dimension) => dimension switch
                {
                    PartitionDimension.SubGroup => null,
                    PartitionDimension.Specialization => specializationCounts ??= CountObservedValues(schedule, dimension),
                    PartitionDimension.Alternative => alternativeCounts ??= CountObservedValues(schedule, dimension),
                    _ => throw Unreachable(),
                };

                static Dictionary<GroupId, int> CountObservedValues(Schedule schedule, PartitionDimension dimension)
                {
                    var sets = new Dictionary<GroupId, HashSet<PartitionKey>>();
                    foreach (var l1 in schedule.EnumerateAllLessons())
                    {
                        ref readonly var lesson = ref l1.Lesson;
                        PartitionKey value = lesson.GroupPartitionKey.GetPartitionDimension(dimension);
                        if (value.Value is null)
                        {
                            continue;
                        }
                        foreach (var groupId in lesson.Groups)
                        {
                            if (!sets.TryGetValue(groupId, out var set))
                            {
                                set = [];
                                sets[groupId] = set;
                            }
                            set.Add(value);
                        }
                    }
                    return sets.ToDictionary(x => x.Key, x => x.Value.Count);
                }

                bool PassesGroupFilter()
                {
                    {
                        var a = filter.GroupFilter.AttendanceMode;
                        if (!a.IsEmpty)
                        {
                            var firstGroup = l.Lesson.Group;
                            if (firstGroup.IsInvalid)
                            {
                                return false;
                            }
                            var attendanceOfFirstGroup = schedule.Get(firstGroup).AttendanceMode;
                            if (!a.Contains(attendanceOfFirstGroup))
                            {
                                return false;
                            }
                        }
                    }
                    if (filter.GroupFilter.OneOfGroupIds is not { } groupIds)
                    {
                        return true;
                    }
                    bool CheckId(GroupId groupId)
                    {
                        foreach (var x in l.Lesson.Groups)
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
                    if (l.Weekly is not { } weekly)
                    {
                        return true;
                    }

                    var p = weekly.Date.Period;
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
                        if (l.Lesson.Type == lessonType)
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
                    var courseId = l.Lesson.Course;
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
                foreach (var group in schedule.Get(lesson).Lesson.Groups)
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

                var min1 = schedule.Get(e.Current).GetTimeSlot();
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

                    var t = schedule.Get(e.Current).GetTimeSlot();
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
                    var t = schedule.Get(l).GetTimeSlot();
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
                var l = schedule.Get(lesson);
                if (l.Weekly is { } weekly)
                {
                    ret.Add(weekly.Date.DayOfWeek);
                }
                else if (l.OneTime is { } oneTime)
                {
                    ret.Add(oneTime.Date.Date.DayOfWeek);
                }
                else
                {
                    throw Unreachable();
                }
            }
            return ret.Order().ToArray();
        }

        TeacherId[] TeachersFromLessons()
        {
            return lessons
                .SelectMany(x => schedule.Get(x).Lesson.Teachers)
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

