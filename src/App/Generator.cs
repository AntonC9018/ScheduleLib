using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace App;

public sealed class ScheduleTableDocument : IDocument
{
    public struct Params
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required FilteredSchedule Schedule;
        public required DayNameProvider DayNameProvider;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
    }

    private readonly Params _params;
    private readonly GeneratorCache _cache;

    public ScheduleTableDocument(Params p)
    {
        _params = p;
        _cache = GeneratorCache.Create(_params.Schedule);
    }

    private GroupId[] Groups() => _cache.GroupOrderArray;
    private TimeSlot[] TimeSlots() => _params.Schedule.TimeSlots;
    private DayOfWeek[] Days() => _params.Schedule.Days;
    private const int maxGroupsPerPage = 5;

    private IContainer CenteredBordered(IContainer x)
    {
        x = x.Border(1);
        x = x.AlignCenter();
        x = x.AlignMiddle();
        return x;
    }

    private IContainer CenteredBorderedThick(IContainer x)
    {
        x = x.Border(2);
        x = x.AlignCenter();
        x = x.AlignMiddle();
        return x;
    }

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
                    table.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(30);
                        cols.ConstantColumn(75);
                        var count = CurrentGroups().Count;
                        for (int j = 0; j < count; j++)
                        {
                            cols.RelativeColumn();
                        }
                    });
                    table.Header(h =>
                    {
                        var corner = h.Cell();
                        _ = corner;
                        corner.Column(1);
                        corner.Border(2);

                        var time = h.Cell();
                        _ = time;
                        time.Column(2);
                        time.Border(2);

                        var ids = CurrentGroups();
                        for (uint groupIdIndex = 0; groupIdIndex < ids.Count; groupIdIndex++)
                        {
                            var groupId = ids[(int) groupIdIndex];
                            var g = new GroupAccessor(_params.Schedule.Source, groupId);
                            var nameCell = h.Cell();
                            nameCell.Column(3 + groupIdIndex);
                            var x = nameCell.Element(CenteredBorderedThick);
                            _ = x.Text($"{g.Ref.Name}({g.Ref.Language.GetName()})");
                        }
                    });

                    TableBody(table, CurrentGroups());
                });
            });
        }
    }

    private uint GetLessonCol(int minOrder)
    {
        var ret = minOrder % maxGroupsPerPage;
        // 1-based indexing
        ret += 1;
        // day and time columns
        ret += 2;
        return (uint) ret;
    }

    private struct DayIter
    {
        public required uint Index;
        public required uint Row;
        public required DayOfWeek Day;

        public required uint CellsPerTimeSlot;
    }

    private struct Slot
    {
        public required DayIter DayIter;

        public required uint TimeSlotIndex;
        public required uint TimeSlotRow;
        public required TimeSlot TimeSlot;

        public uint CellsPerTimeSlot => DayIter.CellsPerTimeSlot;
    }

    private IEnumerable<DayIter> IterateDays()
    {
        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        uint cellsPerTimeSlot = (uint) _cache.MaxRowsInOneCell;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            uint dayRowIndex = timeSlotCount * dayIndex * cellsPerTimeSlot + 1;
            yield return new()
            {
                Day = day,
                Index = dayIndex,
                Row = dayRowIndex,
                CellsPerTimeSlot = cellsPerTimeSlot,
            };
        }
    }

    private IEnumerable<Slot> Slots(DayIter d)
    {
        var timeSlots = TimeSlots();
        for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
        {
            var timeSlot = timeSlots[timeSlotIndex];
            var rowStartIndex = d.Row + timeSlotIndex * d.CellsPerTimeSlot;

            yield return new()
            {
                DayIter = d,
                TimeSlot = timeSlot,
                TimeSlotIndex = timeSlotIndex,
                TimeSlotRow = rowStartIndex,
            };
        }
    }

    private IEnumerable<Slot> Slots()
    {
        foreach (var d in IterateDays())
        {
            foreach (var s in Slots(d))
            {
                yield return s;
            }
        }
    }

    private void TableBody(TableDescriptor table, ArraySegment<GroupId> currentGroups)
    {
        TableLeftHeader(table);

        foreach (var s in Slots())
        {
            for (int currentGroupIndex = 0; currentGroupIndex < currentGroups.Count; currentGroupIndex++)
            {
                var dayKey = new DayKey
                {
                    TimeSlot = s.TimeSlot,
                    DayOfWeek = s.DayIter.Day,
                };

                var groupId = currentGroups[currentGroupIndex];

                var allKey = dayKey.AllKey(groupId);

                if (!_cache.Dicts.MappingByAll.TryGetValue(allKey, out var lessons))
                {
                    _ = BasicCell();
                    continue;
                }

                if (_cache.Dicts.IsSeparatedLayout)
                {
                    var x = BasicCell();
                    x.Text(text =>
                    {
                        for (int index = 0; index < lessons.Count; index++)
                        {
                            var lesson = lessons[index];
                            // Temporary.
                            text.Line(lesson.Lesson.Course.Name);
                        }
                    });
                }
                else
                {
                    var lessonCountThisDay = _cache.Dicts.SharedMaxOrder![allKey] + 1;

                    int ComputeRowOffsetOf(int lessonIndex)
                    {
                        var total = _cache.MaxRowsInOneCell;
                        var x = (float) (lessonIndex + 1) / (float) lessonCountThisDay;
                        return (int)(x * total);
                    }

                    uint ComputeRowSpan(int lessonIndex)
                    {
                        var a = ComputeRowOffsetOf(lessonIndex - 1);
                        var b = ComputeRowOffsetOf(lessonIndex);
                        return (uint)(b - a);
                    }

                    uint lastOrderNeedingBorder = 0;

                    for (int lessonIndex = 0; lessonIndex < lessons.Count; lessonIndex++)
                    {
                        var lesson = lessons[lessonIndex];
                        {
                            var key = (lesson, _cache.Dicts.GroupOrder[groupId]);
                            // We're not the cell that defines this.
                            if (!_cache.Dicts.SharedCellStart!.Contains(key))
                            {
                                continue;
                            }
                        }

                        var lessonOrder = _cache.Dicts.LessonVerticalOrder![lesson];
                        OrderNeedingBorder(lessonOrder);

                        {
                            uint size = (uint) lesson.Lesson.Groups.Count;
                            var cell = table.Cell();
                            var rowSpan = ComputeRowSpan((int) lessonOrder);
                            lastOrderNeedingBorder = lessonOrder + rowSpan - 1;
                            cell.RowSpan(rowSpan);
                            cell.ColumnSpan(size);
                            cell.Row(s.TimeSlotRow + lessonOrder);

                            {
                                var col = GetLessonCol(currentGroupIndex);
                                cell.Column(col);
                            }

                            var x = CenteredBordered(cell);
                            x.Text(text =>
                            {
                                text.Span(lesson.Lesson.Course.Name);
                            });
                        }
                    }
                    OrderNeedingBorder(s.CellsPerTimeSlot - 1);

                    void OrderNeedingBorder(uint lessonOrder)
                    {
                        if (lastOrderNeedingBorder <= lessonOrder)
                        {
                            return;
                        }

                        var cell = table.Cell();
                        cell.ColumnSpan(1);
                        cell.Row(s.TimeSlotRow + lastOrderNeedingBorder);
                        var rowSpan = lessonOrder - lastOrderNeedingBorder;
                        cell.RowSpan(rowSpan);

                        {
                            var col = GetLessonCol(currentGroupIndex);
                            cell.Column(col);
                        }

                        var x = CenteredBordered(cell);
                        _ = x;
                    }

                }

                IContainer BasicCell()
                {
                    var cell = table.Cell();
                    cell.ColumnSpan(1);
                    cell.Row(s.TimeSlotRow);
                    cell.RowSpan(s.CellsPerTimeSlot);

                    {
                        var col = GetLessonCol(currentGroupIndex);
                        cell.Column(col);
                    }

                    var x = CenteredBordered(cell);
                    return x;
                }
            }
        }
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        foreach (var d in IterateDays())
        {
            uint timeSlotCount = (uint) TimeSlots().Length;

            {
                var dayCell = table.Cell();
                dayCell.RowSpan(timeSlotCount * d.CellsPerTimeSlot);
                dayCell.Column(1);
                dayCell.Row(d.Row);

                var x = CenteredBorderedThick(dayCell);
                x = x.Container();
                x = x.RotateLeft();
                x.Text(text =>
                {
                    var weekDayText = _params.DayNameProvider.GetDayName(d.Day);
                    text.Span(weekDayText);
                    text.DefaultTextStyle(style => style.Bold());
                });
            }
        }

        foreach (var s in Slots())
        {
            var timeCell = table.Cell();
            timeCell.Column(2);
            timeCell.Row(s.DayIter.Row + s.TimeSlotIndex * s.CellsPerTimeSlot);
            timeCell.RowSpan(s.CellsPerTimeSlot);

            var x = CenteredBordered(timeCell);
            // ReSharper disable once AccessToModifiedClosure
            x.Text(x1 => TimeSlotTextBox(x1, s.TimeSlotIndex));
        }
    }

    private void TimeSlotTextBox(TextDescriptor text, uint timeSlotIndex)
    {
        // This is deliberately not passed, because the formatting might depend on the index.
        var timeSlot = TimeSlots()[timeSlotIndex];

        {
            var t = _params.TimeSlotDisplay.IndexDisplay((int) timeSlotIndex + 1);
            text.Line(t);
        }

        {
            var interval = _params.LessonTimeConfig.GetTimeSlotInterval(timeSlot);
            var formattedInterval = _params.TimeSlotDisplay.IntervalDisplay(interval);
            text.Span(formattedInterval);
        }
    }
}
