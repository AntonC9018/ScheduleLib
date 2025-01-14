using System.Diagnostics.Metrics;
using System.Reflection;
using System.Text;
using QuestPDF.Elements.Table;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace App;

public static class StringBuilderHelper
{
    public static string ToStringAndClear(this StringBuilder sb)
    {
        var ret = sb.ToString();
        sb.Clear();
        return ret;
    }
}
public sealed class ScheduleTableDocument : IDocument
{
    public struct Services
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required DayNameProvider DayNameProvider;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
        public required SubGroupNumberDisplayHandler SubGroupNumberDisplay;
        public required ParityDisplayHandler ParityDisplay;
        public required LessonTypeDisplayHandler LessonTypeDisplay;
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

    public ScheduleTableDocument(FilteredSchedule schedule, in Services p)
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
                // ReSharper disable once AccessToModifiedClosure
                var count = int.Min(groups.Length - i, _sizesConfig.MaxAdditionalColumnsPerPage);
                // ReSharper disable once AccessToModifiedClosure
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
                        cols.RelativeColumn(context.Sizes.RegularColumnWidth);
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
        foreach (var s in Cells())
        {
            var rowKey = s.Key;

            for (int currentGroupIndex = 0; currentGroupIndex < context.Columns.Count; currentGroupIndex++)
            {
                var groupId = context.Columns[currentGroupIndex];
                var cellKey = rowKey.CellKey(groupId);
                var column = GetLessonCol(currentGroupIndex);

                if (!_cache.Mappings.MappingByCell.TryGetValue(cellKey, out var lessons))
                {
                    _ = BasicCell(column);
                    continue;
                }

                if (_cache.SharedLayout is not {} layout)
                {
                    var x = BasicCell(column);
                    x.Text(text =>
                    {
                        foreach (var lesson in lessons)
                        {
                            LessonText(text, lesson, columnWidth: 1);
                        }
                    });
                    continue;
                }

                // Shared layout
                {
                    var lessonCountThisDay = layout.SharedMaxOrder[cellKey] + 1;
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
                            cell.Column(column);

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
                            cell.Column(column);

                            var x = BorderedRow(cell, s.TimeSlotRow + lastOrderNeedingBorder);
                            x = x.Centered();
                            _ = x;
                        }
                    }


                    IEnumerable<(RegularLesson Lesson, uint LessonOrder, uint RowSpan)> OwnedLessons()
                    {
                        foreach (var lesson in lessons)
                        {
                            {
                                var key = (lesson, _cache.ColumnOrder[groupId]);
                                // We're not the cell that defines this.
                                if (!layout.SharedCellStart.Contains(key))
                                {
                                    continue;
                                }
                            }

                            var order = layout.LessonVerticalOrder[lesson];
                            var rowSpan = ComputeRowSpan((int) order);
                            yield return (lesson, order, rowSpan);
                        }
                    }

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
        string CourseName()
        {
            // It's impossible to measure text in this library. Yikes.
            var course = _schedule.Source.Get(lesson.Lesson.Course);
            if (columnWidth == 1)
            {
                return course.Names[^1];
            }

            return course.Names[0];
        }

        var sb = _services.GetCleanStringBuilder();
        {
            var subGroupNumber = _services.SubGroupNumberDisplay.Get(lesson.Lesson.SubGroup);
            if (subGroupNumber is { } s1)
            {
                sb.Append(s1);
                sb.Append(". ");
            }
        }
        {
            var str = sb.ToStringAndClear();
            var span = text.Span(str);
            span.Bold();
        }
        {
            var courseName = CourseName();
            sb.Append(courseName);
        }
        {
            var lessonType = _services.LessonTypeDisplay.Get(lesson.Lesson.Type);
            var parity = _services.ParityDisplay.Get(lesson.Date.Parity);
            bool appendAny = lessonType != null || parity != null;
            if (appendAny)
            {
                sb.Append(" (");

                bool written = false;
                void Write(string? str)
                {
                    if (str is not { } notNullS)
                    {
                        return;
                    }
                    if (written)
                    {
                        sb.Append(", ");
                    }
                    else
                    {
                        written = true;
                    }

                    sb.Append(notNullS);
                }

                Write(lessonType);
                Write(parity);
                sb.Append(")");
            }
        }
        {
            var str = sb.ToStringAndClear();
            text.Line(str);
        }
        {
            var teacher = _schedule.Source.Get(lesson.Lesson.Teacher);
            sb.Append(teacher.Name);

            sb.Append("  ");
            var room = _schedule.Source.Get(lesson.Lesson.Room);
            sb.Append(room);
        }
        {
            var str = sb.ToStringAndClear();
            text.Span(str);
        }
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        foreach (var d in IterateRows())
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

        foreach (var s in Cells())
        {
            var timeCell = table.Cell();
            timeCell.Column(2);
            timeCell.RowSpan(s.CellsPerTimeSlot);

            IContainer x = timeCell.Row(s.RowIter.Row + s.TimeSlotIndex * s.CellsPerTimeSlot);
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

    private struct RowIter
    {
        public required uint Index;
        public required uint Row;
        public required DayOfWeek Day;

        public required uint CellsPerTimeSlot;
    }

    private struct Cell
    {
        public required RowIter RowIter;

        public required uint TimeSlotIndex;
        public required uint TimeSlotRow;
        public required TimeSlot TimeSlot;

        public uint CellsPerTimeSlot => RowIter.CellsPerTimeSlot;

        public RowKey Key => new()
        {
            TimeSlot = TimeSlot,
            DayOfWeek = RowIter.Day,
        };
    }

    private IEnumerable<RowIter> IterateRows()
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

    private IEnumerable<Cell> Cells(RowIter d)
    {
        var timeSlots = TimeSlots();
        for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
        {
            var timeSlot = timeSlots[timeSlotIndex];
            var rowStartIndex = d.Row + timeSlotIndex * d.CellsPerTimeSlot;

            yield return new()
            {
                RowIter = d,
                TimeSlot = timeSlot,
                TimeSlotIndex = timeSlotIndex,
                TimeSlotRow = rowStartIndex,
            };
        }
    }

    private IEnumerable<Cell> Cells()
    {
        foreach (var d in IterateRows())
        {
            foreach (var s in Cells(d))
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

