using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

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
    public required Schedule Source;
    public required IEnumerable<RegularLesson> Lessons;
    public required GroupId[] Groups;
    public required TimeSlot[] TimeSlots;
    public required DayOfWeek[] Days;
}

public static class FilterHelper
{
    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public static FilteredSchedule Filter(this Schedule schedule, ScheduleFilter filter)
    {
        var lessons = GetRegularLessons();
        var groups = GroupsFromLessons();
        var timeSlots = TimeSlotsFromLessons();
        var days = UsedDaysOfWeek();

        return new()
        {
            Source = schedule,
            Groups = groups,
            Lessons = lessons,
            TimeSlots = timeSlots,
            Days = days,
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
        GroupId[] GroupsFromLessons()
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

public sealed class DayNameProvider
{
    public string GetDayName(DayOfWeek day)
    {
        return day switch
        {
            DayOfWeek.Monday => "Luni",
            DayOfWeek.Tuesday => "Marți",
            DayOfWeek.Wednesday => "Miercuri",
            DayOfWeek.Thursday => "Joi",
            DayOfWeek.Friday => "Vineri",
            DayOfWeek.Saturday => "Sâmbătă",
            DayOfWeek.Sunday => "Duminică",
            _ => throw new ArgumentOutOfRangeException(nameof(day), day, null),
        };
    }
}

public sealed class ScheduleTableDocument : IDocument
{
    public struct Params
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required FilteredSchedule Schedule;
        public required DayNameProvider DayNameProvider;
    }

    private readonly Params _params;
    private readonly Cache _cache = default;

    private struct CacheDicts()
    {
        public Dictionary<GroupId, int> GroupOrder = new();
        public Dictionary<AllKey, List<RegularLesson>> MappingByAll = new();
        public Dictionary<DayKey, List<RegularLesson>> MappingByDay = new();
        public Dictionary<RegularLesson, int> LessonToOrder = new();
    }

    private struct Cache
    {
        public required CacheDicts Dicts;
        public required int MaxLessonsInOneCell;

        public bool IsInitialized => Dicts.GroupOrder == null;
    }

    private record struct AllKey
    {
        public required TimeSlot TimeSlot;
        public required DayOfWeek DayOfWeek;
        public required GroupId GroupId;
    }

    private record struct DayKey
    {
        public required TimeSlot TimeSlot;
        public required DayOfWeek DayOfWeek;
    }

    public ScheduleTableDocument(Params p)
    {
        _params = p;
        _cache = CreateCache(_params.Schedule);
    }

    private GroupId[] Groups() => _params.Schedule.Groups;
    private TimeSlot[] TimeSlots() => _params.Schedule.TimeSlots;
    private DayOfWeek[] Days() => _params.Schedule.Days;
    const int maxGroupsPerPage = 5;


    public void Compose(IDocumentContainer container)
    {
        for (int i = 0; i < Groups().Length; i += maxGroupsPerPage)
        {
            ArraySegment<GroupId> CurrentGroups()
            {
                var groups = Groups();
                // ReSharper disable once AccessToModifiedClosure
                var count = int.Min(groups.Length - i, maxGroupsPerPage);
                // ReSharper disable once AccessToModifiedClosure
                var ret = new ArraySegment<GroupId>(groups, i, count);
                return ret;
            }

            container.Page(page =>
            {
                page.Margin(50);

                var content = page.Content();
                content.Table(table =>
                {
                    table.Header(h =>
                    {
                        var corner = h.Cell();
                        _ = corner;
                        corner.Column(1);

                        var time = h.Cell();
                        _ = time;
                        time.Column(2);

                        var ids = CurrentGroups();
                        for (uint groupIdIndex = 0; groupIdIndex < ids.Count; groupIdIndex++)
                        {
                            var groupId = ids[(int) groupIdIndex];
                            var g = new GroupAccessor(_params.Schedule.Source, groupId);
                            var nameCell = h.Cell();
                            nameCell.Text($"{g.Ref.Name}({g.Ref.Language.GetName()})");
                            nameCell.Column(2 + groupIdIndex);
                        }
                    });

                    TableBody(table, CurrentGroups());
                });
            });
        }
    }

    private struct SearchContext
    {
        public required BitArray32 OccupiedGroupPositions;
        public required Dictionary<GroupId, int> GroupOrder;
        public required HashSet<GroupId>[] AllGroupingSets;
        public required GroupId[][] AllGroupingArrays;
        public int Index;

        public readonly (HashSet<GroupId> Set, GroupId[] Array) Current
            => (AllGroupingSets[Index], AllGroupingArrays[Index]);
        public readonly bool IsDone => Index == AllGroupingSets.Length;
        public readonly int GroupCount => OccupiedGroupPositions.Length;
    }

    private static Cache CreateCache(in FilteredSchedule schedule)
    {
        var dicts = new CacheDicts();

        foreach (var lesson in schedule.Lessons)
        {
            {
                var dayKey = new DayKey
                {
                    TimeSlot = lesson.Date.TimeSlot,
                    DayOfWeek = lesson.Date.DayOfWeek,
                };
                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(dicts.MappingByDay, dayKey, out bool exists);
                if (!exists)
                {
                    list = new();
                }

                list!.Add(lesson);
            }

            foreach (var group in lesson.Lesson.Groups)
            {
                var key = new AllKey
                {
                    GroupId = group,
                    TimeSlot = lesson.Date.TimeSlot,
                    DayOfWeek = lesson.Date.DayOfWeek,
                };
                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(dicts.MappingByAll, key, out bool exists);
                if (!exists)
                {
                    list = new(4);
                }

                list!.Add(lesson);
            }
        }

        {
            var groupings = new Dictionary<DayKey, HashSet<GroupId>>();

            foreach (var (day, lessons) in dicts.MappingByDay)
            {
                foreach (var lesson in lessons)
                {
                    ref var ids = ref CollectionsMarshal.GetValueRefOrAddDefault(groupings, day, out bool exists);
                    if (!exists)
                    {
                        ids = new();
                    }

                    foreach (var groupId in lesson.Lesson.Groups)
                    {
                        ids!.Add(groupId);
                    }
                }
            }

            var order = dicts.GroupOrder;

            for (int gindex = 0; gindex < schedule.Groups.Length; gindex++)
            {
                var g = schedule.Groups[gindex];
                order.Add(g, gindex);
            }

            var groupingsValues = groupings.Values.ToArray();
            var searchContext = new SearchContext
            {
                AllGroupingSets = groupingsValues,
                OccupiedGroupPositions = BitArray32.Empty(schedule.Groups.Length),
                AllGroupingArrays = groupingsValues.Select(x => x.ToArray()).ToArray(),
                GroupOrder = order,
            };

            DoSearch(ref searchContext);

            // Just do a recursive search
            bool DoSearch(ref SearchContext context)
            {
                if (context.IsDone)
                {
                    return true;
                }

                var (idSet, idArray) = context.Current;

                // Try to apply one set
                // Backtrack if can't apply
                // Recurse if can't
                var addedMask = BitArray32.Empty(idArray.Length);
                var existingMask = BitArray32.Empty(idArray.Length);
                {
                    for (int index = 0; index < idArray.Length; index++)
                    {
                        if (context.GroupOrder.ContainsKey(idArray[index]))
                        {
                            existingMask.Set(index);
                        }
                        else
                        {
                            addedMask.Set(index);
                        }
                        index++;
                    }
                }

                BitArray32 MaskOfPositionsOfIncludedGroups(ref SearchContext context)
                {
                    var ret = BitArray32.Empty(context.OccupiedGroupPositions.Length);
                    foreach (var id in idArray)
                    {
                        if (!context.GroupOrder.TryGetValue(id, out int index))
                        {
                            continue;
                        }

                        ret.Set(index);
                    }
                    return ret;
                }


                // 0 indices existing -> try all positions
                // 1 index existing -> Find positions conjoined to the index
                // more than 1 existing -> must be conjoined or reachable, fill the gap in all ways,
                //                         then spill to left or right.

                if (existingMask.IsEmpty)
                {
                    var result = new int[idArray.Length];
                    var allowedIndices = new int[idArray.Length];

                    // Limit the positions to ones where X are in a row.
                    // For this we find the X in a row pattern in the bits of the set.
                    IEnumerable<int[]> GetBasePositions(BitArray32 occupiedPositions)
                    {
                        foreach (var (offset, slice) in occupiedPositions.SlidingWindowLowToHigh(result.Length))
                        {
                            if (!slice.AreNoneSet)
                            {
                                continue;
                            }
                            for (int i = 0; i < allowedIndices.Length; i++)
                            {
                                allowedIndices[i] = offset + i;
                            }
                            yield return allowedIndices;
                        }
                    }

                    var allBasePositions = GetBasePositions(context.OccupiedGroupPositions);
                    foreach (var basePositions in allBasePositions)
                    {
                        var arrangements = ArrangementHelper.Generate(basePositions, result);
                        foreach (var indexArrangement in arrangements)
                        {
                            foreach (var addedIndex in indexArrangement)
                            {
                                context.OccupiedGroupPositions.Set(addedIndex);
                            }

                            foreach (var addedIndex in indexArrangement)
                            {
                                context.OccupiedGroupPositions.Clear(addedIndex);
                            }
                        }
                    }
                }
                else if (existingMask.SetCount == 1)
                {
                    var allowedIndices = new int[addedMask.SetCount];

                    var maskOfIncludedGroups = MaskOfPositionsOfIncludedGroups(ref context);
                    var occupiedPositionsWithoutIncludedGroups = context.OccupiedGroupPositions.Intersect(maskOfIncludedGroups.Not);
                    var interval = occupiedPositionsWithoutIncludedGroups.SetBitInterval;
                    if (interval.Length > idArray.Length)
                    {
                        return false;
                    }

                    // Do sliding windows that contain this position.
                    var length = idArray.Length;
                    int wiggleRoom = length - interval.Length;
                    int startIndex = Math.Max(interval.Start - wiggleRoom, 0);
                    for (int i = startIndex; i < startIndex + interval.Length; i++)
                    {
                        var slice = occupiedPositionsWithoutIncludedGroups.Slice(i, length);
                        if (!slice.AreNoneSet)
                        {
                            continue;
                        }

                        {
                            int resultIndex = 0;
                            for (int offset = 0; offset < length; offset++)
                            {
                                int groupStartIndex = offset + i;
                                if (maskOfIncludedGroups.IsSet(groupStartIndex))
                                {
                                    continue;
                                }

                                allowedIndices[resultIndex] = groupStartIndex;
                                resultIndex++;
                            }
                        }
                    }
                }
                else
                {
                    // Do sliding windows around the included groups
                }

            }
        }

        // We assume at this point that the sort has determined how to position
        // the groups such that the ones that have a shared lecture are
        // put next to each other.
        {
            var groups = schedule.Groups;
            for (int gindex = 0; gindex < groups.Length; gindex++)
            {
                var g = groups[gindex];
                dicts.GroupOrder.Add(g, gindex);
            }
        }

        int MaxLessonsInOneCell()
        {
            int ret = 0;
            foreach (var lessons in dicts.MappingByAll.Values)
            {
                ret = Math.Max(ret, lessons.Count);
            }
            return ret;
        }

        return new Cache
        {
            Dicts = dicts,
            MaxLessonsInOneCell = MaxLessonsInOneCell(),
        };
    }

    private (int Order, GroupId Group) GetLessonMinGroup(RegularLesson lesson)
    {
        var e = lesson.Lesson.Groups.GetEnumerator();
        bool ok = e.MoveNext();
        Debug.Assert(ok);

        int minOrder = _cache.Dicts.GroupOrder[e.Current];
        GroupId id = e.Current;
        while (e.MoveNext())
        {
            var order = _cache.Dicts.GroupOrder[e.Current];
            if (order < minOrder)
            {
                minOrder = order;
                id = e.Current;
            }
        }
        return (minOrder, id);
    }

    private uint GetLessonCol(int minOrder)
    {
        var ret = minOrder % maxGroupsPerPage;
        // day and time columns
        ret += 2;
        return (uint) ret;
    }

    private void TableBody(TableDescriptor table, ArraySegment<GroupId> currentGroups)
    {
        TableLeftHeader(table);

        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            uint dayRowIndex = timeSlotCount * dayIndex;

            var timeSlots = TimeSlots();
            for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
            {
                var timeSlot = timeSlots[timeSlotIndex];

                for (int currentGroupIndex = 0; currentGroupIndex < currentGroups.Count; currentGroupIndex++)
                {
                    var currentGroupId = currentGroups[currentGroupIndex];

                    {
                        var allKey = new AllKey
                        {
                            GroupId = currentGroupId,
                            TimeSlot = timeSlot,
                            DayOfWeek = day,
                        };
                        if (!_cache.Dicts.MappingByAll.TryGetValue(allKey, out var lessons))
                        {
                            continue;
                        }
                    }

                    {
                        var dayKey = new DayKey
                        {
                            DayOfWeek = day,
                            TimeSlot = timeSlot,
                        };
                        var allLessonsThisDay = _cache.Dicts.MappingByDay[dayKey];
                    }


                    bool MinInAnyLesson()
                    {
                        foreach (var lesson in lessons)
                        {
                            var t = GetLessonMinGroup(lesson);
                            if (t.Group == currentGroupId)
                            {
                                return true;
                            }
                        }
                        return false;
                    }

                    if (!MinInAnyLesson())
                    {
                        continue;
                    }


                    var cell = table.Cell();
                    cell.ColumnSpan((uint) lesson.Lesson.Groups.Count);
                    cell.Row(dayRowIndex + timeSlotIndex);

                    {
                        var col = GetLessonCol(_groupOrder[currentGroupId]);
                        cell.Column(col);
                    }

                    {
                        cell.Text(text =>
                        {

                        });
                    }
                }
            }
        }
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            // TODO: handle overlaps (do two-per or a dynamic N-per)
            var dayRowIndex = timeSlotCount * dayIndex;

            {
                var weekDay = days[dayIndex];
                var dayCell = table.Cell();
                dayCell.RowSpan(timeSlotCount);
                dayCell.Column(1);
                dayCell.Row(dayRowIndex);

                var textContainer = dayCell.Container();
                textContainer.RotateLeft();
                textContainer.Text(text =>
                {
                    var weekDayText = _params.DayNameProvider.GetDayName(weekDay);
                    text.Span(weekDayText);
                    text.DefaultTextStyle(style => style.Bold());
                });
            }

            {
                var timeSlots = TimeSlots();
                for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
                {
                    var timeCell = table.Cell();
                    timeCell.AlignCenter();
                    timeCell.Column(2);
                    timeCell.Row(dayRowIndex + timeSlotIndex);

                    // ReSharper disable once AccessToModifiedClosure
                    timeCell.Text(x => TimeSlotTextBox(x, timeSlotIndex));
                }
            }

        }
    }

    private void TimeSlotTextBox(TextDescriptor text, uint timeSlotIndex)
    {
        // This is deliberately not passed, because the formatting might depend on the index.
        var timeSlot = TimeSlots()[timeSlotIndex];

        {
            var romanTimeSlot = ToRoman(timeSlot.Index + 1);
            text.Line(romanTimeSlot);
        }

        {
            var interval = _params.LessonTimeConfig.GetTimeSlotInterval(timeSlot);
            var formattedInterval = $"{interval.Start.Hour}:{interval.Start.Minute:00}-{interval.End.Hour}:{interval.End.Minute:00}";
            text.Line(formattedInterval);
        }
    }

    private static string ToRoman(int num)
    {
        string romanLetter = num switch
        {
            1 => "I",
            2 => "II",
            3 => "III",
            4 => "IV",
            5 => "V",
            6 => "VI",
            7 => "VII",
            8 => "VIII",
            9 => "IX",
            10 => "X",
            _ => throw new NotImplementedException("Unimplemented for higher time slots."),
        };
        return romanLetter;
    }
}



// TODO: A non-recursive impl
#if false
public struct ArrangementEnumerator<T>
{
    private T[] _items;
    public T[] Current { get; }
    private BitArray32 _includedMask;

    public ArrangementEnumerator(T[] items, T[] current)
    {
        Current = current;
        _items = items;

        if (!BitArray32.CanCreate(items.Length))
        {
            throw new NotSupportedException("The max supported items is 32 currently");
        }

    }

    public bool MoveNext()
    {
        if (_includedMask.Length == 0)
        {
            _includedMask = BitArray32.NSet(_items.Length, Current.Length);
            if (_includedMask.Length == 0)
            {
                return false;
            }
            return true;
        }

        using var setIndexEnumerator = _includedMask.SetBitIndicesHighToLow.GetEnumerator();
        {
            bool s = setIndexEnumerator.MoveNext();
            Debug.Assert(s);
        }

        var lastIndex = setIndexEnumerator.Current;
        int currentLastIndex = _items.Length - 1;
        if (lastIndex == currentLastIndex)
        {
            while (setIndexEnumerator.MoveNext())
            {
                var prev = setIndexEnumerator.Current;
                currentLastIndex--;
                if (prev == currentLastIndex)
                {

                }
            }
        }

    }
}
#endif

public static class ArrangementHelper
{
    public static IEnumerable<T[]> GenerateWithSingleOutputArray<T>(T[] items, int slots)
    {
        if (items.Length == 0)
        {
            return [];
        }
        if (slots == 0)
        {
            return [];
        }

        var resultMem = new T[slots];

        return Generate(items, resultMem);
    }

    public static IEnumerable<T[]> Generate<T>(T[] items, T[] resultMem)
    {
        foreach (var x in Generate(new ArraySegment<T>(items), new(resultMem)))
        {
            _ = x;
            yield return resultMem;
        }
    }

    public static IEnumerable<ArraySegment<T>> Generate<T>(ArraySegment<T> items, ArraySegment<T> resultMem)
    {
        if (items.Count == 0)
        {
            yield break;
        }
        if (resultMem.Count == 0)
        {
            yield break;
        }

        {
            resultMem[0] = items[0];
            var items1 = items[1 ..];
            var resultMem1 = resultMem[1 ..];
            foreach (var x1 in Generate(items1, resultMem1))
            {
                _ = x1;
                yield return resultMem;
            }
        }

        if (items.Count > resultMem.Count)
        {
            var items1 = items[1 ..];
            foreach (var x1 in Generate(items1, resultMem))
            {
                _ = x1;
                yield return resultMem;
            }
        }
    }
}
