using System.Diagnostics;
using System.Runtime.InteropServices;

namespace App;

public record struct AllKey
{
    public required TimeSlot TimeSlot;
    public required DayOfWeek DayOfWeek;
    public required GroupId GroupId;
}

public record struct DayKey
{
    public required TimeSlot TimeSlot;
    public required DayOfWeek DayOfWeek;
}

public static class KeyHelper
{
    public static AllKey DefaultAllKey(this RegularLesson lesson)
    {
        return new AllKey
        {
            TimeSlot = lesson.Date.TimeSlot,
            DayOfWeek = lesson.Date.DayOfWeek,
            GroupId = lesson.Lesson.Group,
        };
    }

    public static AllKey AllKey(this in RegularLessonDate date, GroupId groupId)
    {
        return new AllKey
        {
            TimeSlot = date.TimeSlot,
            DayOfWeek = date.DayOfWeek,
            GroupId = groupId,
        };
    }

    public static DayKey DayKey(this AllKey key)
    {
        return new DayKey
        {
            TimeSlot = key.TimeSlot,
            DayOfWeek = key.DayOfWeek,
        };
    }

    public static DayKey DayKey(this in RegularLessonDate date)
    {
        return new DayKey
        {
            TimeSlot = date.TimeSlot,
            DayOfWeek = date.DayOfWeek,
        };
    }

    public static AllKey AllKey(this DayKey dayKey, GroupId groupId)
    {
        return new AllKey
        {
            TimeSlot = dayKey.TimeSlot,
            DayOfWeek = dayKey.DayOfWeek,
            GroupId = groupId,
        };
    }
}

public struct GeneratorCacheDicts()
{
    public Dictionary<GroupId, int> GroupOrder = new();
    public Dictionary<AllKey, List<RegularLesson>> MappingByAll = new();
    public Dictionary<DayKey, List<RegularLesson>> MappingByDay = new();

    public readonly bool IsSeparatedLayout => SharedCellStart == null;
    // For cell size
    public HashSet<(RegularLesson Lesson, int Order)>? SharedCellStart;
    public Dictionary<AllKey, int>? SharedMaxCount;
    // For position
    public Dictionary<RegularLesson, uint>? LessonVerticalOrder;
}

public struct GeneratorCache
{
    public required GeneratorCacheDicts Dicts;
    public required GroupId[] GroupOrderArray;
    public required int MaxRowsInOneCell;

    public static GeneratorCache Create(in FilteredSchedule schedule)
    {
        var dicts = new GeneratorCacheDicts();

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

        void DoDefaultOrder(in FilteredSchedule schedule)
        {
            var groups = schedule.Groups;
            for (int gindex = 0; gindex < groups.Length; gindex++)
            {
                var g = groups[gindex];
                dicts.GroupOrder.Add(g, gindex);
            }
        }

        {
            bool success = GroupArrangementSearchHelper.Search(dicts, schedule.Groups);
            if (success)
            {
                dicts.LessonVerticalOrder = new();
                dicts.SharedCellStart = new();

                // Singular lessons go on top, have higher priority.
                // Shared lessons go below.
                static int LessonPriority(RegularLesson lesson) => lesson.Lesson.Groups.Count;

                Dict<AllKey, int> perGroupCounters = new();
                dicts.SharedMaxCount = perGroupCounters._dict;

                foreach (var group in schedule.Lessons.GroupBy(LessonPriority))
                {
                    foreach (var lesson in group)
                    {
                        ProcessLesson();

                        void ProcessLesson()
                        {
                            var dayKey = lesson.Date.DayKey();

                            int max = MoveToAfterFurthestInGrouping(dayKey);
                            dicts.LessonVerticalOrder.Add(lesson, (uint) max);

                            var firstOrder = FindFirstOrder();
                            dicts.SharedCellStart.Add((lesson, firstOrder));
                        }

                        int FindFirstOrder()
                        {
                            int order = int.MaxValue;
                            foreach (var groupId in lesson.Lesson.Groups)
                            {
                                var otherOrder = dicts.GroupOrder[groupId];
                                if (otherOrder < order)
                                {
                                    order = otherOrder;
                                }
                            }
                            return order;
                        }

                        int MoveToAfterFurthestInGrouping(DayKey dayKey)
                        {
                            int maxAmongGroups = -1;
                            foreach (var groupId in lesson.Lesson.Groups)
                            {
                                var allKey = dayKey.AllKey(groupId);
                                var counter = perGroupCounters.Add(allKey, out bool justAdded);
                                if (justAdded)
                                {
                                    continue;
                                }
                                maxAmongGroups = Math.Max(maxAmongGroups, counter);
                            }

                            int maxCurrent = maxAmongGroups + 1;

                            foreach (var groupId in lesson.Lesson.Groups)
                            {
                                var allKey = dayKey.AllKey(groupId);
                                ref var counter = ref perGroupCounters.Ref(allKey);
                                counter = maxCurrent;
                            }

                            return maxCurrent;
                        }
                    }
                }
            }
            else
            {
                // Add no shared cells, for now.
                dicts.GroupOrder.Clear();
                DoDefaultOrder(schedule);
            }
        }

        int MaxLessonsInOneCell()
        {
            if (dicts.IsSeparatedLayout)
            {
                int ret = 0;
                foreach (var lessons in dicts.MappingByAll.Values)
                {
                    ret = Math.Max(ret, lessons.Count);
                }
                return ret;
            }

            {
                int ret = -1;
                foreach (var k in dicts.LessonVerticalOrder!.Values)
                {
                    ret = Math.Max(ret, (int) k);
                }
                return ret + 1;
            }
        }

        var orderArray = new GroupId[dicts.GroupOrder.Count];
        foreach (var (group, index) in dicts.GroupOrder)
        {
            orderArray[index] = group;
        }

        return new GeneratorCache
        {
            GroupOrderArray = orderArray,
            Dicts = dicts,
            MaxRowsInOneCell = MaxLessonsInOneCell(),
        };
    }
}

public static class GroupArrangementSearchHelper
{
    public static bool Search(in GeneratorCacheDicts dicts, GroupId[] groups)
    {
        var groupings = new Dictionary<DayKey, HashSet<GroupId>>();

        foreach (var (day, lessons) in dicts.MappingByDay)
        {
            foreach (var lesson in lessons)
            {
                if (lesson.Lesson.Groups.IsSingleGroup)
                {
                    continue;
                }

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
        Debug.Assert(order.Count == 0);

        var groupingsValues = groupings.Values.ToArray();
        var searchContext = new SearchContext
        {
            Groups = groups,
            AllGroupingSets = groupingsValues,
            OccupiedGroupPositions = BitArray32.Empty(groups.Length),
            AllGroupingArrays = groupingsValues.Select(x => x.ToArray()).ToArray(),
            GroupOrder = order,
        };

        // TODO: 4 or 5 per page limiter.
        // TODO: If no solution exists, return the solution with the least mismatches.
        var success = DoSearch(ref searchContext);
        return success;
    }

    public struct SearchContext
    {
        public required BitArray32 OccupiedGroupPositions;
        public required Dictionary<GroupId, int> GroupOrder;
        public required HashSet<GroupId>[] AllGroupingSets;
        public required GroupId[][] AllGroupingArrays;
        public required GroupId[] Groups;
        public int Index;

        public readonly (HashSet<GroupId> Set, GroupId[] Array) Current
            => (AllGroupingSets[Index], AllGroupingArrays[Index]);
        public readonly bool IsDone => Index == AllGroupingSets.Length;
        public readonly int GroupCount => OccupiedGroupPositions.Length;
    }

    public static bool DoSearch(ref SearchContext context)
    {
        if (context.IsDone)
        {
            using var indices = context.OccupiedGroupPositions.UnsetBitIndicesLowToHigh.GetEnumerator();
            foreach (var group in context.Groups)
            {
                ref var groupIndex = ref CollectionsMarshal.GetValueRefOrAddDefault(
                    context.GroupOrder,
                    group,
                    out bool exists);
                if (exists)
                {
                    continue;
                }

                var moved = indices.MoveNext();
                Debug.Assert(moved, "Incorrect count!");
                context.OccupiedGroupPositions.Set(indices.Current);
                groupIndex = indices.Current;
            }

            Debug.Assert(indices.MoveNext() == false);

            return true;
        }

        var (idSet, idArray) = context.Current;
        _ = idSet;

        // Try to apply one set
        // Backtrack if can't apply
        // Recurse if can't
        var existingMask = BitArray32.Empty(idArray.Length);
        {
            for (int index = 0; index < idArray.Length; index++)
            {
                if (context.GroupOrder.ContainsKey(idArray[index]))
                {
                    existingMask.Set(index);
                }
            }
        }
        var addedMask = existingMask.Flipped;


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
        Context1 CreateContext1(ref SearchContext context)
        {
            var permutation = new int[addedMask.SetCount];

            // These are almost free anyway
            var maskOfIncludedGroups = MaskOfPositionsOfIncludedGroups(ref context);
            var occupiedPositionsWithoutIncludedGroups = context.OccupiedGroupPositions.Intersect(maskOfIncludedGroups.Flipped);

            var context1 = new Context1
            {
                AllowedIndices = permutation,
                IdArray = idArray,
                IncludedGroupPositionsMask = occupiedPositionsWithoutIncludedGroups,
                OccupiedPositionsWithoutIncludedGroups = occupiedPositionsWithoutIncludedGroups,
            };
            return context1;
        }

        var context1 = CreateContext1(ref context);
        var permutations = context1.GeneratePermutations();

        foreach (var indexPermutation in permutations)
        {
            // Not the best way to do this, could integrate it into the arrangement algorithm somehow.
            {
                foreach (var addedIndex in indexPermutation)
                {
                    context.OccupiedGroupPositions.Set(addedIndex);
                }

                using var itemIndex = addedMask.SetBitIndicesLowToHigh.GetEnumerator();
                for (int i = 0; i < indexPermutation.Length; i++)
                {
                    var b = itemIndex.MoveNext();
                    Debug.Assert(b);
                    context.GroupOrder.Add(idArray[itemIndex.Current], indexPermutation[i]);
                }

                Debug.Assert(!itemIndex.MoveNext());

                context.Index++;
            }

            if (DoSearch(ref context))
            {
                return true;
            }

            {
                context.Index--;

                foreach (int index in addedMask.SetBitIndicesLowToHigh)
                {
                    context.GroupOrder.Remove(idArray[index]);
                }

                foreach (var addedIndex in indexPermutation)
                {
                    context.OccupiedGroupPositions.Clear(addedIndex);
                }
            }
        }

        return false;
    }

    private sealed class Context1
    {
        public required GroupId[] IdArray;
        public required int[] AllowedIndices;
        public required BitArray32 OccupiedPositionsWithoutIncludedGroups;
        public required BitArray32 IncludedGroupPositionsMask;

        public IEnumerable<int[]> GeneratePermutations()
        {
            var basePositions = GetBasePositions();
            foreach (var basePosition in basePositions)
            {
                var perms = PermutationHelper.Generate(basePosition);
                foreach (var p in perms)
                {
                    yield return p;
                }
            }
        }

        private IEnumerable<int[]> GetBasePositions()
        {
            if (IncludedGroupPositionsMask.IsEmpty)
            {
                return NoIncludedGroupsBasePositions();
            }
            else
            {
                return SomeIncludedGroupsBasePositions();
            }
        }

        private IEnumerable<int[]> SomeIncludedGroupsBasePositions()
        {
            var interval = OccupiedPositionsWithoutIncludedGroups.SetBitInterval;
            if (interval.Length > IdArray.Length)
            {
                yield break;
            }

            // Do sliding windows that contain the whole interval.
            var length = IdArray.Length;
            int wiggleRoom = length - interval.Length;
            int startIndex = Math.Max(interval.Start - wiggleRoom, 0);
            for (int i = startIndex; i < startIndex + interval.Length; i++)
            {
                var slice = OccupiedPositionsWithoutIncludedGroups.Slice(i, length);
                if (!slice.AreNoneSet)
                {
                    continue;
                }

                int resultIndex = 0;
                for (int offset = 0; offset < length; offset++)
                {
                    int groupStartIndex = offset + i;
                    if (IncludedGroupPositionsMask.IsSet(groupStartIndex))
                    {
                        continue;
                    }

                    AllowedIndices[resultIndex] = groupStartIndex;
                    resultIndex++;
                }

                yield return AllowedIndices;
            }
        }

        private IEnumerable<int[]> NoIncludedGroupsBasePositions()
        {
            var windows = OccupiedPositionsWithoutIncludedGroups.SlidingWindowLowToHigh(AllowedIndices.Length);
            foreach (var (offset, slice) in windows)
            {
                if (!slice.AreNoneSet)
                {
                    continue;
                }
                for (int i = 0; i < AllowedIndices.Length; i++)
                {
                    AllowedIndices[i] = offset + i;
                }
                yield return AllowedIndices;
            }
        }
    }
}

file readonly struct Dict<TKey, TValue>()
    where TKey : notnull
{
    internal readonly Dictionary<TKey, TValue> _dict = new();

    public ref TValue? Add(TKey key, out bool added)
    {
        ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(_dict, key, out bool exists);
        added = !exists;
        return ref value;
    }

    public ref TValue Ref(TKey key)
    {
        ref var value = ref CollectionsMarshal.GetValueRefOrNullRef(_dict, key);
        return ref value!;
    }
}
