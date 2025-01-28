using System.Runtime.InteropServices;

namespace ScheduleLib.Generation;

public record struct CellKey<TColumnKey>
{
    public required RowKey RowKey;
    public required TColumnKey ColumnKey;
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

    public static CellKey<T> CellKey<T>(this RowKey rowKey, T columnKey)
    {
        return new CellKey<T>
        {
            RowKey = rowKey,
            ColumnKey = columnKey,
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

public sealed class RegularLessonByCellKey<ColumnKey>
    : Dictionary<CellKey<ColumnKey>, List<RegularLesson>>
{
}

public struct GeneratorCacheMappings<TColumnKey>()
{
    public RegularLessonByCellKey<TColumnKey> MappingByCell = new();
    public Dictionary<RowKey, List<RegularLesson>> MappingByRow = new();
}

public struct GeneratorCache
{
    public required GeneratorCacheMappings<GroupId> Mappings;
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
                int ret1 = -1;
                foreach (var k in layout2.LessonVerticalOrder.Values)
                {
                    ret1 = Math.Max(ret1, (int) k);
                }
                return ret1 + 1;
            }

            {
                int ret1 = 0;
                foreach (var lessons in mappings.MappingByCell.Values)
                {
                    ret1 = Math.Max(ret1, lessons.Count);
                }
                return ret1;
            }
        }
    }

    private static GeneratorCacheMappings<GroupId> CreateMappings(FilteredSchedule schedule)
    {
        var dicts = new GeneratorCacheMappings<GroupId>();
        dicts.MappingByCell = MappingsCreationHelper.CreateCellMappings(schedule.Lessons, l => l.Lesson.Groups);
        InitCellMappings();
        return dicts;

        void InitCellMappings()
        {
            foreach (var lesson in schedule.Lessons)
            {
                foreach (var group in lesson.Lesson.Groups)
                {
                    var cellKey = new CellKey<GroupId>
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
                        list = new(2);
                    }

                    list!.Add(lesson);
                }
            }
        }
    }
}


public static class MappingsCreationHelper
{
    public static RegularLessonByCellKey<TColumnKey> CreateCellMappings<TColumnKey>(
        IEnumerable<RegularLesson> lessons,
        // TODO: Remove the use of this IEnumerable
        Func<RegularLesson, IEnumerable<TColumnKey>> colFunc)
    {
        var ret = new RegularLessonByCellKey<TColumnKey>();
        foreach (var lesson in lessons)
        {
            var rowKey = lesson.Date.RowKey();
            var columnKeys = colFunc(lesson);
            foreach (var columnKey in columnKeys)
            {
                var cellKey = rowKey.CellKey(columnKey);
                ref var list = ref CollectionsMarshal.GetValueRefOrAddDefault(ret, cellKey, out bool exists);
                if (!exists)
                {
                    list = new(2);
                }

                list!.Add(lesson);
            }
        }
        return ret;
    }
}
