using System.Text;
using QuestPDF.Elements.Table;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ScheduleLib.Generation.TeacherCute;

public sealed class Generator : IDocument
{
    public struct Services
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required DayNameProvider DayNameProvider;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
        public required PdfLessonTextDisplayHandler LessonTextDisplayHandler;
        public required StringBuilder StringBuilder;

        public readonly StringBuilder GetCleanStringBuilder()
        {
            StringBuilder.Clear();
            return StringBuilder;
        }
    }

    private readonly Services _services;
    private readonly FilteredSchedule _schedule;

    public Generator(Services services, FilteredSchedule schedule)
    {
        _services = services;
        _schedule = schedule;
    }

    private readonly record struct RowKey(TimeSlot Time);
    private readonly record struct ColumnKey(DayOfWeek Day);

    public void Compose(IDocumentContainer container)
    {
        var mappings = MappingsCreationHelper.CreateCellMappings<RowKey, ColumnKey>(
            _schedule.EnumerateWeeklyLessons(),
            rowFunc: x => new RowKey(x.Date.TimeSlot),
            colFunc: x => [ new ColumnKey(x.Date.DayOfWeek) ]);

        List<WeeklyLessonAccessor> Lessons(CellKey<RowKey, ColumnKey> cell)
        {
            return mappings.GetValueOrDefault(cell, []);
        }

        IEnumerable<ColumnKey> Columns()
        {
            return _schedule.Days.Select(x => new ColumnKey(x));
        }

        uint RowSizeOfRow(RowKey row)
        {
            uint maxSize = 0;
            foreach (var col in Columns())
            {
                var lessons = Lessons(new()
                {
                    ColumnKey = col,
                    RowKey = row,
                });
                maxSize = (uint) Math.Max(lessons.Count, maxSize);
            }
            // Skip the row?
            if (maxSize == 0)
            {
                return 0;
            }
            return maxSize;
        }

        container.Page(page =>
        {
            page.DefaultTextStyle(s =>
            {
                s = s.FontSize(15);
                return s;
            });

            var content = page.Content();
            content.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    // Time
                    cols.RelativeColumn(1);

                    foreach (var day in _schedule.Days)
                    {
                        _ = day;
                        cols.RelativeColumn(1);
                    }
                });

                CellPos cellPos = default;

                // Skip the empty top left
                cellPos.X += 1;

                foreach (var day in _schedule.Days)
                {
                    var cell = table.Cell();
                    cell = cell.Pos(cellPos);
                    var t = cell.CenteredBorderedThick();
                    var text = _services.DayNameProvider.GetDayName(day);
                    var cellText = t.Text(text);
                    _ = cellText;
                    cellPos.X += 1;
                }

                cellPos.MoveByRows(1);

                foreach (var timeSlot in _schedule.TimeSlots)
                {
                    var rowKey = new RowKey(timeSlot);
                    var rowSize = RowSizeOfRow(rowKey);
                    if (rowSize == 0)
                    {
                        continue;
                    }
                    DoRow(rowKey, rowSize);
                    cellPos.MoveByRows(rowSize);
                    cellPos.X = 0;
                }

                cellPos.MoveByRows(1);

                void DoRow(RowKey row, uint rowSize)
                {
                    {
                        var timeInterval = _services.LessonTimeConfig.GetTimeSlotInterval(row.Time);
                        var time = _services.TimeSlotDisplay.IntervalDisplay(timeInterval);

                        var cell = table.Cell();
                        cell = cell.RowSpan(rowSize);
                        cell = cell.Pos(cellPos);
                        var c = cell.CenteredBorderedThick();

                        c.Text(time);
                        cellPos.X += 1;
                    }

                    foreach (var column in Columns())
                    {
                        var lessons = Lessons(new()
                        {
                            RowKey = row,
                            ColumnKey = column,
                        });
                        var sizeComputer = new SizeComputer(
                            maxRowsInOneCell: (int) rowSize,
                            lessonCount: lessons.Count);
                        for (int i = 0; i < lessons.Count; i++)
                        {
                            var lesson = lessons[i];
                            var offset = sizeComputer.ComputeRowOffsetOf(i);
                            var span = sizeComputer.ComputeRowSpan(i);
                            var cell = table.Cell();

                            var newPos = cellPos with
                            {
                                Y = (uint) (cellPos.Y + offset),
                            };

                            cell = cell.Pos(newPos);
                            cell = cell.RowSpan(span);
                            var t = cell.Border(1);
                            t = t.AlignMiddle();
                            t = t.AlignCenter();
                            t.Text(d =>
                            {
                                var sb = _services.GetCleanStringBuilder();
                                _services.LessonTextDisplayHandler.Handle(new()
                                {
                                    Lesson = lesson,
                                    Schedule = _schedule.Source,
                                    StringBuilder = sb,
                                    ColumnWidth = 1,
                                    TextDescriptor = d,
                                    LessonTimeConfig = _services.LessonTimeConfig,
                                });
                            });
                        }
                        if (lessons.Count == 0)
                        {
                            var cell = table.Cell();
                            cell = cell.Pos(cellPos);
                            cell = cell.RowSpan(rowSize);
                            cell.Border(1);
                            _ = cell;
                        }
                        cellPos.X += 1;
                    }
                }
            });
        });
    }
}

file record struct CellPos(uint X, uint Y)
{
    public void MoveByRows(uint yOffset)
    {
        X = 0;
        Y += yOffset;
    }
    public void MoveToRow(uint y)
    {
        X = 0;
        Y = y;
    }
}

file static class Helper
{
    public static ITableCellContainer Pos(this ITableCellContainer c, CellPos pos)
    {
        c = c.Row(pos.Y + 1);
        c = c.Column(pos.X + 1);
        return c;
    }
}
