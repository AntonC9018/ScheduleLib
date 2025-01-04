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

    public IContainer CenteredBordered(IContainer x)
    {
        x = x.Border(1);
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
                        cols.ConstantColumn(50);
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
                        corner.Border(1);

                        var time = h.Cell();
                        _ = time;
                        time.Column(2);
                        time.Border(1);

                        var ids = CurrentGroups();
                        for (uint groupIdIndex = 0; groupIdIndex < ids.Count; groupIdIndex++)
                        {
                            var groupId = ids[(int) groupIdIndex];
                            var g = new GroupAccessor(_params.Schedule.Source, groupId);
                            var nameCell = h.Cell();
                            nameCell.Column(3 + groupIdIndex);
                            var x = nameCell.Element(CenteredBordered);
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

    private struct Slot
    {
        public required uint DayIndex;
        public required uint DayRow;
        public required DayOfWeek Day;

        public required uint TimeSlotIndex;
        public required uint TimeSlotRow;
        public required TimeSlot TimeSlot;

        public required uint CellsPerTimeSlot;
    }

    private IEnumerable<Slot> Slots()
    {
        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        uint cellsPerTimeSlot = (uint) _cache.MaxRowsInOneCell;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            uint dayRowIndex = timeSlotCount * dayIndex * cellsPerTimeSlot + 1;

            var timeSlots = TimeSlots();
            for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
            {
                var timeSlot = timeSlots[timeSlotIndex];
                var rowStartIndex = dayRowIndex + timeSlotIndex * cellsPerTimeSlot;

                yield return new()
                {
                    DayIndex = dayIndex,
                    DayRow = dayRowIndex,
                    Day = day,
                    TimeSlot = timeSlot,
                    TimeSlotIndex = timeSlotIndex,
                    TimeSlotRow = rowStartIndex,
                    CellsPerTimeSlot = cellsPerTimeSlot,
                };
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
                    DayOfWeek = s.Day,
                };

                var groupId = currentGroups[currentGroupIndex];

                var allKey = dayKey.AllKey(groupId);

                if (!_cache.Dicts.MappingByAll.TryGetValue(allKey, out var lessons))
                {
                    continue;
                }

                if (_cache.Dicts.IsSeparatedLayout)
                {
                    var cell = table.Cell();
                    cell.ColumnSpan(1);
                    cell.RowSpan(s.CellsPerTimeSlot);

                    {
                        var col = GetLessonCol(_cache.Dicts.GroupOrder[groupId]);
                        cell.Column(col);
                    }

                    var x = cell.Element(CenteredBordered);
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
                    foreach (var lesson in lessons)
                    {
                        var lessonOrder = _cache.Dicts.LessonVerticalOrder![lesson];
                        {
                            var key = (lessonOrder, allKey);
                            // We're not the cell that defines this.
                            if (!_cache.Dicts.SharedCellStart!.Contains(key))
                            {
                                continue;
                            }
                        }
                        {
                            uint size = (uint) lesson.Lesson.Groups.Count;
                            var cell = table.Cell();
                            cell.RowSpan(1);
                            cell.ColumnSpan(size);
                            cell.Row(s.TimeSlotRow + (uint) lessonOrder);

                            {
                                var col = GetLessonCol(currentGroupIndex);
                                cell.Column(col);
                            }

                            var x = cell.Element(CenteredBordered);
                            x.Text(text =>
                            {
                                text.Span(lesson.Lesson.Course.Name);
                            });
                        }
                    }
                }
            }
        }
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        uint timeSlotRowSpan = (uint) _cache.MaxRowsInOneCell;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var dayRowIndex = timeSlotCount * dayIndex * timeSlotRowSpan + 1;

            {
                var weekDay = days[dayIndex];
                var dayCell = table.Cell();
                dayCell.RowSpan(timeSlotCount * timeSlotRowSpan);
                dayCell.Column(1);
                dayCell.Row(dayRowIndex);

                var x = dayCell.Element(CenteredBordered);
                x = x.Container();
                x = x.RotateLeft();
                x.Text(text =>
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
                    timeCell.Column(2);
                    timeCell.Row(dayRowIndex + timeSlotIndex * timeSlotRowSpan);
                    timeCell.RowSpan(timeSlotRowSpan);

                    var x = timeCell.Element(CenteredBordered);
                    // ReSharper disable once AccessToModifiedClosure
                    x.Text(x1 => TimeSlotTextBox(x1, timeSlotIndex));
                }
            }

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
