using System.Reflection;
using System.Text;
using QuestPDF.Elements.Table;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ScheduleLib.Generation;

public static class StringBuilderHelper
{
    public static string ToStringAndClear(this StringBuilder sb)
    {
        var ret = sb.ToString();
        sb.Clear();
        return ret;
    }
}
public sealed class GroupColumnScheduleTableDocument : IDocument
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
    private readonly GeneratorCache _cache;
    private readonly SchedulePdfSizesConfig _sizesConfig = new();

    public GroupColumnScheduleTableDocument(FilteredSchedule schedule, in Services p)
    {
        _schedule = schedule;
        _services = p;
        _cache = GeneratorCache.Create(schedule);
    }

    private GroupId[] Columns() => _cache.ColumnOrder.Columns;
    private TimeSlot[] TimeSlots() => _schedule.TimeSlots;
    private DayOfWeek[] Days() => _schedule.Days;

    private struct CurrentSizes
    {
        public required float RegularColumnWidth;
    }

    private struct CurrentContext
    {
        public required CurrentSizes Sizes;
        public required ArraySegment<GroupId> Columns;
    }

    public void Compose(IDocumentContainer container)
    {
        for (int i = 0; i < Columns().Length; i += _sizesConfig.MaxAdditionalColumnsPerPage)
        {
            ArraySegment<GroupId> CurrentColumns()
            {
                var groups = Columns();
                var count = int.Min(groups.Length - i, _sizesConfig.MaxAdditionalColumnsPerPage);
                return new(groups, i, count);
            }

            var currentContext = new CurrentContext
            {
                Columns = CurrentColumns(),
                Sizes = new()
                {
                    RegularColumnWidth = _sizesConfig.UsefulGroupsWidth / CurrentColumns().Count,
                },
            };

            Table(container, currentContext);
        }
    }

    private void Table(IDocumentContainer container, CurrentContext context)
    {
        container.Page(page =>
        {
            page.Size(_sizesConfig.PageSize);
            page.Margin(_sizesConfig.PageMargin);

            page.DefaultTextStyle(s =>
            {
                s = s.FontSize(_sizesConfig.FontSize);
                return s;
            });

            var content = page.Content();
            content.Table(table =>
            {
                table.ColumnsDefinition(cols =>
                {
                    cols.ConstantColumn(_sizesConfig.WeekDayColumnWidth);
                    cols.ConstantColumn(_sizesConfig.TimeSlotColumnWidth);
                    var count = context.Columns.Count;
                    for (int j = 0; j < count; j++)
                    {
                        cols.ConstantColumn(context.Sizes.RegularColumnWidth);
                    }
                });
                TableHeader(table, context);
                TableLeftHeader(table);
                TableBody(table, context);
            });
        });
    }

    private void TableHeader(TableDescriptor table, CurrentContext context)
    {
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

            var ids = context.Columns;
            for (uint groupIdIndex = 0; groupIdIndex < ids.Count; groupIdIndex++)
            {
                var groupId = ids[(int) groupIdIndex];
                var g = _schedule.Source.Get(groupId);
                var nameCell = h.Cell();
                nameCell.Column(3 + groupIdIndex);
                var x = nameCell.CenteredBorderedThick();
                _ = x.Text($"{g.Name}({g.Language.GetName()})");
            }
        });
    }

    private void TableBody(TableDescriptor table, CurrentContext context)
    {
        foreach (var s in IterateRows())
        {
            var rowKey = s.Key;

            for (int columnIndex = 0; columnIndex < context.Columns.Count; columnIndex++)
            {
                var columnKey = context.Columns[columnIndex];
                var cellKey = rowKey.CellKey(columnKey);
                var columnNumber = GetLessonCol(columnIndex);

                if (!_cache.Mappings.MappingByCell.TryGetValue(cellKey, out var lessons))
                {
                    _ = BasicCell(columnNumber);
                    continue;
                }

                SizeComputer SizeComputer(int lessonCount) => new(_cache.MaxRowsInOneCell, lessonCount);

                if (_cache.SharedLayout is not {} layout)
                {
                    var sizeComputer = SizeComputer(lessons.Count);
                    for (int index = 0; index < lessons.Count; index++)
                    {
                        var lesson = lessons[index];

                        // Very important gotcha:
                        // the text will end up stacking if not all rows in a row group are taken.
                        // So it's very important to do correct computations of the row and column span.

                        var rowSpan = sizeComputer.ComputeRowSpan(index);
                        var cell = table.Cell();
                        cell = cell.ColumnSpan(1);
                        cell = cell.RowSpan(rowSpan);
                        cell = cell.Column(columnNumber);

                        int offset = sizeComputer.ComputeRowOffsetOf(index);
                        var rowNumber = (uint) (s.TimeSlotRow + offset);

                        var x = BorderedRow(cell, rowNumber);
                        x = x.Centered();
                        x.Text(text =>
                        {
                            LessonText(text, lesson, columnWidth: 1);
                        });
                    }
                    continue;
                }

                // Shared layout
                {
                    var lessonCountThisDay = layout.SharedMaxOrder[cellKey] + 1;
                    var sizeComputer = SizeComputer(lessonCountThisDay);
                    HandleLessons();
                    HandleMissingBorders();

                    void HandleLessons()
                    {
                        foreach (var t in OwnedLessons())
                        {
                            uint size = (uint) t.Lesson.Lesson.Groups.Count;
                            var cell = table.Cell();
                            cell.RowSpan(t.RowSpan);
                            cell.ColumnSpan(size);
                            cell.Column(columnNumber);

                            var x = BorderedRow(cell, s.TimeSlotRow + t.LessonOrder);
                            x = x.Centered();
                            x.Text(text =>
                            {
                                LessonText(text, t.Lesson, columnWidth: size);
                            });
                        }
                    }

                    void HandleMissingBorders()
                    {
                        uint lastOrderNeedingBorder = 0;

                        foreach (var t in OwnedLessons())
                        {
                            Handle(t.LessonOrder);
                            lastOrderNeedingBorder = t.LessonOrder + t.RowSpan - 1;
                        }
                        Handle(s.CellsPerTimeSlot - 1);

                        void Handle(uint lessonOrder)
                        {
                            if (lastOrderNeedingBorder <= lessonOrder)
                            {
                                return;
                            }

                            var cell = table.Cell();
                            cell.ColumnSpan(1);
                            var rowSpan = lessonOrder - lastOrderNeedingBorder;
                            cell.RowSpan(rowSpan);
                            cell.Column(columnNumber);

                            var x = BorderedRow(cell, s.TimeSlotRow + lastOrderNeedingBorder);
                            x = x.Centered();
                            _ = x;
                        }
                    }

                    IEnumerable<OwnedLesson> OwnedLessons()
                    {
                        foreach (var lesson in lessons)
                        {
                            {
                                var key = (lesson, _cache.ColumnOrder[columnKey]);
                                // We're not the cell that defines this.
                                if (!layout.SharedCellStart.Contains(key))
                                {
                                    continue;
                                }
                            }

                            var order = layout.LessonVerticalOrder[lesson];
                            var rowSpan = sizeComputer.ComputeRowSpan((int) order);
                            yield return new(lesson, order, rowSpan);
                        }
                    }
                }
            }

            IContainer BasicCell(uint column)
            {
                var cell = table.Cell();
                cell.ColumnSpan(1);
                cell.RowSpan(s.CellsPerTimeSlot);
                cell.Column(column);

                var x = BorderedRow(cell, s.TimeSlotRow);
                x = x.Centered();
                return x;
            }
        }
    }

    private void LessonText(
        TextDescriptor text,
        RegularLesson lesson,
        uint columnWidth)
    {
        _services.LessonTextDisplayHandler.Handle(new()
        {
            TextDescriptor = text,
            Lesson = lesson,
            StringBuilder = _services.GetCleanStringBuilder(),
            Schedule = _schedule.Source,
            ColumnWidth = columnWidth,
            LessonTimeConfig = _services.LessonTimeConfig,
        });
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        foreach (var d in IterateRowGroups())
        {
            uint timeSlotCount = (uint) TimeSlots().Length;

            {
                var dayCell = table.Cell();
                dayCell.RowSpan(timeSlotCount * d.CellsPerTimeSlot);
                dayCell.Column(1);
                dayCell.Row(d.Row);

                var x = dayCell.CenteredBorderedThick();
                x = x.Container();
                x = x.RotateLeft();
                x.Text(text =>
                {
                    var weekDayText = _services.DayNameProvider.GetDayName(d.Day);
                    text.Span(weekDayText);
                    text.DefaultTextStyle(style => style.Bold());
                });
            }
        }

        foreach (var s in IterateRows())
        {
            var timeCell = table.Cell();
            timeCell.Column(2);
            timeCell.RowSpan(s.CellsPerTimeSlot);

            IContainer x = timeCell.Row(s.RowGroupIter.Row + s.TimeSlotIndex * s.CellsPerTimeSlot);
            x = x.CenteredBorderedThick();
            // ReSharper disable once AccessToModifiedClosure
            x.Text(x1 => TimeSlotTextBox(x1, s.TimeSlotIndex));
        }
    }

    private void TimeSlotTextBox(TextDescriptor text, uint timeSlotIndex)
    {
        // This is deliberately not passed, because the formatting might depend on the index.
        var timeSlot = TimeSlots()[timeSlotIndex];

        {
            var t = _services.TimeSlotDisplay.IndexDisplay((int) timeSlotIndex);
            text.Line(t);
        }

        {
            var interval = _services.LessonTimeConfig.GetTimeSlotInterval(timeSlot);
            var formattedInterval = _services.TimeSlotDisplay.IntervalDisplay(interval);
            var span = text.Span(formattedInterval);
            _ = span;
        }
    }

    private uint GetLessonCol(int minOrder)
    {
        var ret = minOrder % _sizesConfig.MaxAdditionalColumnsPerPage;
        // 1-based indexing
        ret += 1;
        // day and time columns
        ret += 2;
        return (uint) ret;
    }

    private struct RowGroupIter
    {
        public required uint Index;
        public required uint Row;
        public required DayOfWeek Day;

        public required uint CellsPerTimeSlot;
    }

    private struct RowIter
    {
        public required RowGroupIter RowGroupIter;

        public required uint TimeSlotIndex;
        public required uint TimeSlotRow;
        public required TimeSlot TimeSlot;

        public uint CellsPerTimeSlot => RowGroupIter.CellsPerTimeSlot;

        public RowKey Key => new()
        {
            TimeSlot = TimeSlot,
            DayOfWeek = RowGroupIter.Day,
        };
    }

    private IEnumerable<RowGroupIter> IterateRowGroups()
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

    private IContainer BorderedRow(ITableCellContainer descriptor, uint row)
    {
        descriptor.Row(row);
        uint zeroBasedRow = row - 1;
        uint rowsPerDay = (uint) (_cache.MaxRowsInOneCell * TimeSlots().Length);
        uint rem = zeroBasedRow % rowsPerDay;

        float top = 1;
        float bottom = 1;
        if (rem == 0)
        {
            top = 2;
        }
        else if (rem == rowsPerDay - 1)
        {
            bottom = 2;
        }

        var ret = descriptor.Border(bottom: bottom, top: top, left: 1, right: 1);
        return ret;
    }

    private IEnumerable<RowIter> IterateRows(RowGroupIter d)
    {
        var timeSlots = TimeSlots();
        for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
        {
            var timeSlot = timeSlots[timeSlotIndex];
            var rowStartIndex = d.Row + timeSlotIndex * d.CellsPerTimeSlot;

            yield return new()
            {
                RowGroupIter = d,
                TimeSlot = timeSlot,
                TimeSlotIndex = timeSlotIndex,
                TimeSlotRow = rowStartIndex,
            };
        }
    }

    private IEnumerable<RowIter> IterateRows()
    {
        foreach (var d in IterateRowGroups())
        {
            foreach (var s in IterateRows(d))
            {
                yield return s;
            }
        }
    }
}

public static class PdfHelper
{
    public static IContainer Centered(this IContainer x)
    {
        x = x.AlignCenter();
        x = x.AlignMiddle();
        return x;
    }

    public static IContainer CenteredBorderedThick(this IContainer x)
    {
        x = x.Border(2);
        x = x.AlignCenter();
        x = x.AlignMiddle();
        return x;
    }

    // Why in the world is this method private???
    private delegate IContainer BorderDelegate(
        IContainer element,
        float top = 0,
        float bottom = 0,
        float left = 0,
        float right = 0);
    // ReSharper disable once ReplaceWithSingleCallToSingle
    private static readonly BorderDelegate ReflectedBorder = typeof(BorderExtensions)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Where(x => x.GetParameters().Length == 5)
        .Single()
        .CreateDelegate<BorderDelegate>();

    public static IContainer Border(
        this IContainer element,
        float top,
        float bottom,
        float left,
        float right)
    {
        return ReflectedBorder(element, top, bottom, left, right);
    }
}

public struct SchedulePdfSizesConfig()
{
    public float WeekDayColumnWidth = 30;
    public float TimeSlotColumnWidth = 75;
    public int MaxAdditionalColumnsPerPage = 5;
    public PageSize PageSize = PageSizes.A4;
    public float PageMargin = 15;
    public float FontSize = 10f;

    public readonly float UsefulGroupsWidth => PageSize.Width - 2 * PageMargin - WeekDayColumnWidth - TimeSlotColumnWidth;
}


public readonly struct SizeComputer
{
    private readonly int _maxRowsInOneCell;
    private readonly int _lessonCount;

    public SizeComputer(int maxRowsInOneCell, int lessonCount)
    {
        _maxRowsInOneCell = maxRowsInOneCell;
        _lessonCount = lessonCount;
    }

    public int ComputeRowOffsetOf(int lessonIndex)
    {
        var total = _maxRowsInOneCell;
        var x = (float) lessonIndex / (float) _lessonCount;
        return (int)(x * total);
    }

    public uint ComputeRowSpan(int lessonIndex)
    {
        var a = ComputeRowOffsetOf(lessonIndex);
        var b = ComputeRowOffsetOf(lessonIndex + 1);
        return (uint)(b - a);
    }

}

file record struct OwnedLesson(RegularLesson Lesson, uint LessonOrder, uint RowSpan);
