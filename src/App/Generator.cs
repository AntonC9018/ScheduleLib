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
    public struct Params
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required FilteredSchedule Schedule;
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

    private IContainer Centered(IContainer x)
    {
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

    private struct SizesConfig()
    {
        public float WeekDayColumnWidth = 30;
        public float TimeSlotColumnWidth = 75;
        public int MaxGroupsPerPage = 5;
        public PageSize PageSize = PageSizes.A4;
        public float PageMargin = 15;
        public float FontSize = 10f;

        public readonly float UsefulGroupsWidth => PageSize.Width - 2 * PageMargin - WeekDayColumnWidth - TimeSlotColumnWidth;
    }

    private readonly SizesConfig _sizesConfig = new();

    private struct CurrentSizes
    {
        public required float RegularColumnWidth;
    }

    private struct CurrentContext
    {
        public required CurrentSizes Sizes;
        public required ArraySegment<GroupId> Groups;
    }

    public void Compose(IDocumentContainer container)
    {
        for (int i = 0; i < Groups().Length; i += _sizesConfig.MaxGroupsPerPage)
        {
            ArraySegment<GroupId> CurrentGroups()
            {
                var groups = Groups();
                // ReSharper disable once AccessToModifiedClosure
                var count = int.Min(groups.Length - i, _sizesConfig.MaxGroupsPerPage);
                // ReSharper disable once AccessToModifiedClosure
                var ret = new ArraySegment<GroupId>(groups, i, count);
                return ret;
            }

            var currentContext = new CurrentContext
            {
                Groups = CurrentGroups(),
                Sizes = new()
                {
                    RegularColumnWidth = _sizesConfig.UsefulGroupsWidth / CurrentGroups().Count,
                },
            };

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
                        var count = CurrentGroups().Count;
                        for (int j = 0; j < count; j++)
                        {
                            cols.RelativeColumn(currentContext.Sizes.RegularColumnWidth);
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
                            var g = _params.Schedule.Source.Get(groupId);
                            var nameCell = h.Cell();
                            nameCell.Column(3 + groupIdIndex);
                            var x = nameCell.Element(CenteredBorderedThick);
                            _ = x.Text($"{g.Name}({g.Language.GetName()})");
                        }
                    });

                    TableBody(table, currentContext);
                });
            });
        }
    }

    private uint GetLessonCol(int minOrder)
    {
        var ret = minOrder % _sizesConfig.MaxGroupsPerPage;
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

        IContainer ret = descriptor;
        ret = ReflectedBorder(ret, bottom: bottom, top: top, left: 1, right: 1);
        return ret;
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

    private void TableBody(TableDescriptor table, CurrentContext context)
    {
        TableLeftHeader(table);

        foreach (var s in Slots())
        {
            for (int currentGroupIndex = 0; currentGroupIndex < context.Groups.Count; currentGroupIndex++)
            {
                var dayKey = new DayKey
                {
                    TimeSlot = s.TimeSlot,
                    DayOfWeek = s.DayIter.Day,
                };

                var groupId = context.Groups[currentGroupIndex];

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
                        foreach (var lesson in lessons)
                        {
                            LessonText(text, lesson, columnWidth: 1);
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
                            {
                                var col = GetLessonCol(currentGroupIndex);
                                cell.Column(col);
                            }

                            var x = BorderedRow(cell, s.TimeSlotRow + lessonOrder);
                            x = Centered(x);
                            x.Text(text =>
                            {
                                LessonText(text, lesson, columnWidth: size);
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
                        var rowSpan = lessonOrder - lastOrderNeedingBorder;
                        cell.RowSpan(rowSpan);

                        {
                            var col = GetLessonCol(currentGroupIndex);
                            cell.Column(col);
                        }

                        var x = BorderedRow(cell, s.TimeSlotRow + lastOrderNeedingBorder);
                        x = Centered(x);
                        _ = x;
                    }

                }

                IContainer BasicCell()
                {
                    var cell = table.Cell();
                    cell.ColumnSpan(1);
                    cell.RowSpan(s.CellsPerTimeSlot);

                    {
                        var col = GetLessonCol(currentGroupIndex);
                        cell.Column(col);
                    }

                    var x = BorderedRow(cell, s.TimeSlotRow);
                    x = Centered(x);
                    return x;
                }
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
            var course = _params.Schedule.Source.Get(lesson.Lesson.Course);
            if (columnWidth == 1)
            {
                return course.Names[^1];
            }

            return course.Names[0];
        }

        var sb = _params.GetCleanStringBuilder();
        {
            var subGroupNumber = _params.SubGroupNumberDisplay.Get(lesson.Lesson.SubGroup);
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
            var lessonType = _params.LessonTypeDisplay.Get(lesson.Lesson.Type);
            var parity = _params.ParityDisplay.Get(lesson.Date.Parity);
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
            var teacher = _params.Schedule.Source.Get(lesson.Lesson.Teacher);
            sb.Append(teacher.Name);

            sb.Append("  ");
            var room = _params.Schedule.Source.Get(lesson.Lesson.Room);
            sb.Append(room);
        }
        {
            var str = sb.ToStringAndClear();
            text.Span(str);
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
            timeCell.RowSpan(s.CellsPerTimeSlot);

            IContainer x = timeCell.Row(s.DayIter.Row + s.TimeSlotIndex * s.CellsPerTimeSlot);
            x = CenteredBorderedThick(x);
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
            var span = text.Span(formattedInterval);
            _ = span;
        }
    }
}
