using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace App;


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

    public ScheduleTableDocument(Params p)
    {
        _params = p;
        _cache = Cache.Create(_params.Schedule);
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