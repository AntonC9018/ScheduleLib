using System.Diagnostics;
using System.Runtime.InteropServices;

namespace App;

public sealed class SharedLayout
{
    // For cell size
    public required HashSet<(RegularLesson Lesson, int Order)> SharedCellStart;
    public required Dictionary<AllKey, int> SharedMaxOrder;
    // For position
    public required Dictionary<RegularLesson, uint> LessonVerticalOrder;

    public static SharedLayout Create(
        IEnumerable<RegularLesson> lessons,
        ColumnOrder columnOrder)
    {
        Dict<AllKey, int> perGroupCounters = new();
        var layout = new SharedLayout
        {
            LessonVerticalOrder = new(),
            SharedCellStart = new(),
            SharedMaxOrder = perGroupCounters._dict,
        };

        // Singular lessons go on top, have higher priority.
        // Shared lessons go below.
        static int LessonPriority(RegularLesson lesson) => lesson.Lesson.Groups.Count;

        foreach (var group in lessons.GroupBy(LessonPriority))
        {
            foreach (var lesson in group)
            {
                ProcessLesson();
                continue;

                void ProcessLesson()
                {
                    var dayKey = lesson.Date.DayKey();

                    int max = MoveToAfterFurthestInGrouping(dayKey);
                    layout.LessonVerticalOrder.Add(lesson, (uint) max);

                    var firstOrder = FindFirstOrder();
                    layout.SharedCellStart.Add((lesson, firstOrder));
                }

                int FindFirstOrder()
                {
                    int order = int.MaxValue;
                    foreach (var groupId in lesson.Lesson.Groups)
                    {
                        var otherOrder = columnOrder[groupId];
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
        return layout;
    }
}


public static class ColumnArrangementHelper
{
    public static (ColumnOrder ColumnOrder, SharedLayout? Layout) OptimizeColumnOrder(FilteredSchedule schedule)
    {
        ColumnOrderBuilder columnOrder = new();

        bool success = Search(schedule, columnOrder);
        if (!success)
        {
            Debug.Assert(columnOrder.Count == 0);
            DoDefaultOrder(schedule, columnOrder);
            return (columnOrder.Build(), null);
        }

        {
            var columnOrder1 = columnOrder.Build();
            var ret = SharedLayout.Create(schedule.Lessons, columnOrder1);
            return (columnOrder1, ret);
        }
    }

    private static void DoDefaultOrder(FilteredSchedule schedule, ColumnOrderBuilder columnOrder)
    {
        var groups = schedule.Groups;
        for (int gindex = 0; gindex < groups.Length; gindex++)
        {
            var g = groups[gindex];
            columnOrder.Add(g, gindex);
        }
    }

    public static bool Search(FilteredSchedule schedule, ColumnOrderBuilder columnOrder)
    {
        var groupings = new Dictionary<DayKey, HashSet<GroupId>>();

        foreach (var lesson in schedule.Lessons)
        {
            if (lesson.Lesson.Groups.IsSingleGroup)
            {
                continue;
            }

            var day = lesson.Date.DayKey();

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

        var groupingsValues = groupings.Values.ToArray();
        var searchContext = new SearchContext
        {
            Groups = schedule.Groups,
            AllGroupingSets = groupingsValues,
            OccupiedGroupPositions = BitArray32.Empty(schedule.Groups.Length),
            AllGroupingArrays = groupingsValues.Select(x => x.ToArray()).ToArray(),
            ColumnOrder = columnOrder,
        };

        // TODO: 4 or 5 per page limiter.
        // TODO: If no solution exists, return the solution with the least mismatches.
        var success = DoSearch(ref searchContext);
        return success;
    }

    private struct SearchContext
    {
        public required BitArray32 OccupiedGroupPositions;
        public required ColumnOrderBuilder ColumnOrder;
        public required HashSet<GroupId>[] AllGroupingSets;
        public required GroupId[][] AllGroupingArrays;
        public required GroupId[] Groups;
        public int Index;

        public readonly (HashSet<GroupId> Set, GroupId[] Array) Current
            => (AllGroupingSets[Index], AllGroupingArrays[Index]);
        public readonly bool IsDone => Index == AllGroupingSets.Length;
        public readonly int GroupCount => OccupiedGroupPositions.Length;
    }

    private static bool DoSearch(ref SearchContext context)
    {
        if (context.IsDone)
        {
            FillInUnconstrainedColumns(ref context);
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
                if (context.ColumnOrder.ContainsKey(idArray[index]))
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
                if (context.ColumnOrder.MaybeGet(id) is { } index)
                {
                    ret.Set(index);
                }
            }
            return ret;
        }

        // 0 indices existing -> try all positions
        // 1 index existing -> Find positions conjoined to the index
        // more than 1 existing -> must be conjoined or reachable, fill the gap in all ways,
        //                         then spill to left or right.
        ArrangementGenerationContext CreateContext1(ref SearchContext context)
        {
            var permutation = new int[addedMask.SetCount];

            // These are almost free anyway
            var maskOfIncludedGroups = MaskOfPositionsOfIncludedGroups(ref context);
            var occupiedPositionsWithoutIncludedGroups = context.OccupiedGroupPositions.Intersect(maskOfIncludedGroups.Flipped);

            var context1 = new ArrangementGenerationContext
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
                    context.ColumnOrder.Add(idArray[itemIndex.Current], indexPermutation[i]);
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
                    context.ColumnOrder.Remove(idArray[index]);
                }

                foreach (var addedIndex in indexPermutation)
                {
                    context.OccupiedGroupPositions.Clear(addedIndex);
                }
            }
        }

        return false;
    }

    private static void FillInUnconstrainedColumns(ref SearchContext context)
    {
        using var indices = context.OccupiedGroupPositions.UnsetBitIndicesLowToHigh.GetEnumerator();
        foreach (var group in context.Groups)
        {
            ref int groupIndex = ref context.ColumnOrder.GetRefOrAddDefault(group, out bool exists);
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
    }

    private sealed class ArrangementGenerationContext
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
