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

public readonly struct ColumnOrderDictBuilder()
{
    public readonly Dictionary<GroupId, int> Dict = new();

    public GroupId[] ToArray()
    {
        var ret = new GroupId[Dict.Count];
        foreach (var (group, index) in Dict)
        {
            ret[index] = group;
        }
        return ret;
    }

    public bool ContainsKey(GroupId key) => Dict.ContainsKey(key);
    public ref int GetRefOrAddDefault(GroupId key, out bool exists)
    {
        return ref CollectionsMarshal.GetValueRefOrAddDefault(Dict, key, out exists);
    }

    public int? MaybeGet(GroupId key)
    {
        if (Dict.TryGetValue(key, out int value))
        {
            return value;
        }
        return null;
    }
    public void Add(GroupId key, int value) => Dict.Add(key, value);
    public void Remove(GroupId key) => Dict.Remove(key);
    public int Count => Dict.Count;
    public bool IsEmpty => Dict.Count == 0;

    public int this[GroupId key]
    {
        get => Dict[key];
        set => Dict[key] = value;
    }

    public ColumnOrderDict AsReadOnly() => new(Dict);
}

public readonly struct ColumnOrderDict
{
    private readonly Dictionary<GroupId, int> _dict;
    public ColumnOrderDict(Dictionary<GroupId, int> dict) => _dict = dict;

    public int this[GroupId key] => _dict[key];
}

public struct GeneratorCacheMappings()
{
    public Dictionary<AllKey, List<RegularLesson>> MappingByAll = new();
    public Dictionary<DayKey, List<RegularLesson>> MappingByDay = new();
}

public struct GeneratorCache
{
    public required GeneratorCacheMappings Mappings;
    public required GroupId[] ColumnOrderArray;
    public required ColumnOrderDict ColumnOrder;
    public required int MaxRowsInOneCell;
    public required SharedLayout? SharedLayout;

    public static GeneratorCache Create(FilteredSchedule schedule)
    {
        var mappings = CreateMappings(schedule);
        var (columnOrder, layout) = ColumnArrangementHelper.OptimizeColumnOrder(schedule);

        var ret = new GeneratorCache
        {
            ColumnOrder = columnOrder.AsReadOnly(),
            ColumnOrderArray = columnOrder.ToArray(),
            Mappings = mappings,
            MaxRowsInOneCell = MaxLessonsInOneCell(layout),
            SharedLayout = layout,
        };
        return ret;

        int MaxLessonsInOneCell(SharedLayout? layout1)
        {
            if (layout1 is { } layout2)
            {
                int ret = -1;
                foreach (var k in layout2.LessonVerticalOrder.Values)
                {
                    ret = Math.Max(ret, (int) k);
                }
                return ret + 1;
            }

            {
                int ret = 0;
                foreach (var lessons in mappings.MappingByAll.Values)
                {
                    ret = Math.Max(ret, lessons.Count);
                }
                return ret;
            }
        }
    }

    private static GeneratorCacheMappings CreateMappings(FilteredSchedule schedule)
    {
        var dicts = new GeneratorCacheMappings();
        InitColumnMappings();
        InitCellMappings();
        return dicts;

        void InitColumnMappings()
        {
            foreach (var lesson in schedule.Lessons)
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
        }

        void InitCellMappings()
        {
            foreach (var lesson in schedule.Lessons)
            {
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
        }
    }
}

