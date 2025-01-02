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

public struct CacheDicts()
{
    public Dictionary<GroupId, int> GroupOrder = new();
    public Dictionary<AllKey, List<RegularLesson>> MappingByAll = new();
    public Dictionary<DayKey, List<RegularLesson>> MappingByDay = new();
    public Dictionary<RegularLesson, int> LessonOrder = new();
}

public struct Cache
{
    public required CacheDicts Dicts;
    public required int MaxLessonsInOneCell;

    public bool IsInitialized => Dicts.GroupOrder == null;

    public static Cache Create(in FilteredSchedule schedule)
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
}

public static class GroupArrangementSearchHelper
{
    public static bool Search(in CacheDicts dicts, in FilteredSchedule schedule)
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
        Context1 CreateContext1(ref SearchContext context)
        {
            var indexArrangement = new int[addedMask.SetCount];
            var allowedIndices = new int[addedMask.SetCount];

            // These are almost free anyway
            var maskOfIncludedGroups = MaskOfPositionsOfIncludedGroups(ref context);
            var occupiedPositionsWithoutIncludedGroups = context.OccupiedGroupPositions.Intersect(maskOfIncludedGroups.Flipped);

            var context1 = new Context1
            {
                AllowedIndices = allowedIndices,
                IdArray = idArray,
                IndexArrangement = indexArrangement,
                IncludedGroupPositionsMask = occupiedPositionsWithoutIncludedGroups,
                OccupiedPositionsWithoutIncludedGroups = occupiedPositionsWithoutIncludedGroups,
            };
            return context1;
        }

        var context1 = CreateContext1(ref context);
        var arrangements = context1.GenerateArrangements();

        foreach (var indexArrangement in arrangements)
        {
            // Not the best way to do this, could integrate it into the arrangement algorithm somehow.
            {
                foreach (var addedIndex in indexArrangement)
                {
                    context.OccupiedGroupPositions.Set(addedIndex);
                }
                for (int i = 0; i < idArray.Length; i++)
                {
                    context.GroupOrder.Add(idArray[i], indexArrangement[i]);
                }
                context.Index++;
            }

            if (DoSearch(ref context))
            {
                return true;
            }

            {
                context.Index--;
                for (int i = 0; i < idArray.Length; i++)
                {
                    context.GroupOrder.Remove(idArray[i]);
                }
                foreach (var addedIndex in indexArrangement)
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
        public required int[] IndexArrangement;
        public required BitArray32 OccupiedPositionsWithoutIncludedGroups;
        public required BitArray32 IncludedGroupPositionsMask;

        public IEnumerable<int[]> GenerateArrangements()
        {
            var basePositions = GetBasePositions();
            foreach (var basePosition in basePositions)
            {
                var arrangements = ArrangementHelper.GenerateWithSingleOutputArray(IdArray, basePosition.Length);
                foreach (var arrangement in arrangements)
                {
                    _ = arrangement;
                    yield return IndexArrangement;
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
            var windows = OccupiedPositionsWithoutIncludedGroups.SlidingWindowLowToHigh(IndexArrangement.Length);
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
