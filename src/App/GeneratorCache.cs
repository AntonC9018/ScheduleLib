using System.Runtime.InteropServices;

namespace App;

public record struct CellKey
{
    public required RowKey RowKey;
    public required GroupId ColumnKey;
}

public record struct RowKey
{
    public required TimeSlot TimeSlot;
    public required DayOfWeek DayOfWeek;
}

public static class KeyHelper
{
    public static RowKey RowKey(this in RegularLessonDate date)
    {
        return new RowKey
        {
            TimeSlot = date.TimeSlot,
            DayOfWeek = date.DayOfWeek,
        };
    }

    public static CellKey CellKey(this RowKey rowKey, GroupId groupId)
    {
        return new CellKey
        {
            RowKey = rowKey,
            ColumnKey = groupId,
        };
    }
}

public readonly struct ColumnOrderBuilder()
{
    public readonly Dictionary<GroupId, int> Dict = new();

    private GroupId[] ToArray()
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

    public ColumnOrder Build() => new(Dict, ToArray());
}

public readonly struct ColumnOrder
{
    private readonly GroupId[] _columns;
    private readonly Dictionary<GroupId, int> _dict;

    public ColumnOrder(Dictionary<GroupId, int> dict, GroupId[] columns)
    {
        _dict = dict;
        _columns = columns;
    }

    public int this[GroupId key] => _dict[key];
    public GroupId[] Columns => _columns;
}

public struct GeneratorCacheMappings()
{
    public Dictionary<CellKey, List<RegularLesson>> MappingByCell = new();
    public Dictionary<RowKey, List<RegularLesson>> MappingByRow = new();
}

public struct GeneratorCache
{
    public required GeneratorCacheMappings Mappings;
    public required ColumnOrder ColumnOrder;
    public required int MaxRowsInOneCell;
    public required SharedLayout? SharedLayout;

    public static GeneratorCache Create(FilteredSchedule schedule)
    {
        var mappings = CreateMappings(schedule);
        var (columnOrder, layout) = ColumnArrangementHelper.OptimizeColumnOrder(schedule);

        var ret = new GeneratorCache
        {
            ColumnOrder = columnOrder,
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
                foreach (var lessons in mappings.MappingByCell.Values)
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
                var rowKey = new RowKey
                {
                    TimeSlot = lesson.Date.TimeSlot,
                    DayOfWeek = lesson.Date.DayOfWeek,
                };
                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(dicts.MappingByRow, rowKey, out bool exists);
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
                    var cellKey = new CellKey
                    {
                        ColumnKey = group,
                        RowKey = new()
                        {
                            TimeSlot = lesson.Date.TimeSlot,
                            DayOfWeek = lesson.Date.DayOfWeek,
                        },
                    };
                    ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(dicts.MappingByCell, cellKey, out bool exists);
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

