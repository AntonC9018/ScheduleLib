using System.Runtime.InteropServices;

namespace ScheduleLib.Generation;

public record struct CellKey<TRowKey, TColumnKey>
{
    public required TRowKey RowKey;
    public required TColumnKey ColumnKey;
}

public record struct DefaultRowKey
{
    public required TimeSlot TimeSlot;
    public required DayOfWeek DayOfWeek;
}

public static class KeyHelper
{
    public static DefaultRowKey DefaultRowKey(this in WeeklyLessonDate date)
    {
        return new DefaultRowKey
        {
            TimeSlot = date.TimeSlot,
            DayOfWeek = date.DayOfWeek,
        };
    }

    public static CellKey<DefaultRowKey, T> DefaultCellKey<T>(this DefaultRowKey rowKey, T columnKey)
    {
        return new CellKey<DefaultRowKey, T>
        {
            RowKey = rowKey,
            ColumnKey = columnKey,
        };
    }
}

public readonly struct ColumnOrderBuilder<TKey>() where TKey : IComparable<TKey>
{
    public readonly Dictionary<TKey, int> Dict = new();

    private TKey[] ToArray()
    {
        var ret = new TKey[Dict.Count];
        foreach (var (group, index) in Dict)
        {
            ret[index] = group;
        }
        return ret;
    }

    public bool ContainsKey(TKey key) => Dict.ContainsKey(key);
    public ref int GetRefOrAddDefault(TKey key, out bool exists)
    {
        return ref CollectionsMarshal.GetValueRefOrAddDefault(Dict, key, out exists);
    }

    public int? MaybeGet(TKey key)
    {
        if (Dict.TryGetValue(key, out int value))
        {
            return value;
        }
        return null;
    }
    public void Add(TKey key, int value) => Dict.Add(key, value);
    public void Remove(TKey key) => Dict.Remove(key);
    public int Count => Dict.Count;

    public ColumnOrder<TKey> Build() => new(Dict, ToArray());
}

public readonly struct ColumnOrder<TKey> where TKey : IComparable<TKey>
{
    private readonly TKey[] _columns;
    private readonly Dictionary<TKey, int> _dict;

    public ColumnOrder(Dictionary<TKey, int> dict, TKey[] columns)
    {
        _dict = dict;
        _columns = columns;
    }

    public int this[TKey key] => _dict[key];
    public TKey[] Columns => _columns;
}

public sealed class RegularLessonsByCellKey<TRowKey, TColumnKey>
    : Dictionary<CellKey<TRowKey, TColumnKey>, List<WeeklyLessonAccessor>>
{
}

public struct GeneratorCacheMappings<TColumnKey>()
{
    public required RegularLessonsByCellKey<DefaultRowKey, TColumnKey> MappingByCell;
}

public struct GeneratorCache
{
    public required GeneratorCacheMappings<GroupId> Mappings;
    public required ColumnOrder<GroupId> ColumnOrder;
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
        var mappingByCell = MappingsCreationHelper.CreateCellMappings(schedule.Lessons, l => l.Lesson.Groups);
        return new()
        {
            MappingByCell = mappingByCell,
        };
    }
}


public static class MappingsCreationHelper
{
    public static RegularLessonsByCellKey<TRowKey, TColumnKey> CreateCellMappings<TRowKey, TColumnKey>(
        IEnumerable<WeeklyLessonAccessor> lessons,
        // TODO: Remove the use of this IEnumerable
        Func<WeeklyLessonRef, TRowKey> rowFunc,
        Func<WeeklyLessonRef, IEnumerable<TColumnKey>> colFunc)
    {
        var ret = new RegularLessonsByCellKey<TRowKey, TColumnKey>();
        foreach (var lesson in lessons)
        {
            var rowKey = rowFunc(lesson.Ref);
            var columnKeys = colFunc(lesson.Ref);
            foreach (var columnKey in columnKeys)
            {
                var cellKey = new CellKey<TRowKey, TColumnKey>
                {
                    RowKey = rowKey,
                    ColumnKey = columnKey,
                };
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

    public static RegularLessonsByCellKey<DefaultRowKey, TColumnKey> CreateCellMappings<TColumnKey>(
        IEnumerable<WeeklyLessonAccessor> lessons,
        // TODO: Remove the use of this IEnumerable
        Func<WeeklyLessonRef, IEnumerable<TColumnKey>> colFunc)
    {
        return CreateCellMappings(
            lessons,
            rowFunc: x => x.Date.DefaultRowKey(),
            colFunc: colFunc);
    }
}
